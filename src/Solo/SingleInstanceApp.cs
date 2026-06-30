using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Solo;

public sealed class SingleInstanceApp : IDisposable
{
    private const int MaxAppIdLength = 64;
    private readonly object _syncLock = new();
    private readonly string _pipeName;
    private readonly Action<string>? _log;
    private readonly Action<string[]>? _onNewInstance;

    private NamedPipeServerStream? _serverStream;
    private CancellationTokenSource? _cancellationTokenSource;

    internal SingleInstanceApp(
        string appId,
        Action<string[]>? onNewInstance,
        Action<string>? log)
    {
        ValidateAppId(appId);
        _onNewInstance = onNewInstance;
        _pipeName = GetPipeName(appId);
        _log = log;
    }

    public bool TryStart(string[]? args)
    {
        lock (_syncLock)
        {
            if (_serverStream != null)
            {
                throw new InvalidOperationException("SingleInstanceApp already started.");
            }

            try
            {
                _serverStream = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.FirstPipeInstance | PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                Log("Successfully created named pipe server, listening for connections.");

                var cts = _cancellationTokenSource = new CancellationTokenSource();
                Task.Run(() => RunServerAsync(cts.Token), cts.Token);
                return true;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            Log("Another instance is already running. Activating existing instance.");
            ActivateExistingInstance(args);
            return false;
        }
    }

    private async Task RunServerAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await _serverStream!.WaitForConnectionAsync(cancellationToken);
                try
                {
                    Log("Received named pipe connection, trying to read args.");
                    using var reader = new StreamReader(_serverStream, leaveOpen: true);
                    var json = await reader.ReadToEndAsync(cancellationToken);
                    Log($"Received JSON: {json}");
                    string[] args = JsonSerializer.Deserialize<string[]>(json)!;
                    _onNewInstance?.Invoke(args);
                }
                catch (JsonException ex)
                {
                    Log($"Failed to deserialize args from JSON: {ex}");
                }
                catch (Exception ex)
                {
                    Log($"Error while receiving args: {ex}");
                }
                finally
                {
                    _serverStream.Disconnect();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore
        }
        catch (ObjectDisposedException)
        {
            // Ignore
        }
    }

    private void ActivateExistingInstance(string[]? args)
    {
        try
        {
            using var clientStream = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            clientStream.Connect(TimeSpan.FromSeconds(5));

            AllowExistingInstanceToSetForegroundWindow(clientStream.SafePipeHandle);

            using var writer = new StreamWriter(clientStream);
            writer.Write(JsonSerializer.Serialize(args ?? []));
            writer.Flush();
        }
        catch (Exception ex)
        {
            Log($"Failed to send args to existing instance: {ex}");
            throw new ExistingInstanceActivationException(
                "Another instance is already running, but failed to activate it.",
                ex);
        }
    }

    private void AllowExistingInstanceToSetForegroundWindow(SafePipeHandle pipeHandle)
    {
        if (OperatingSystem.IsWindows())
        {
            // On Windows, there are pretty strict rules about whether an application can make itself
            // the foreground application. Since the existing instance isn't in the foreground, we
            // need to allow it to make itself the foreground app, otherwise the browser will stay in
            // the background, leading to a poor user experience.
            if (GetNamedPipeServerProcessId(pipeHandle, out uint processId))
            {
                Log("Successfully obtained named pipe server process id");
                if (AllowSetForegroundWindow(processId))
                {
                    Log("Successfully allowed existing instance to make itself the foreground app");
                }
                else
                {
                    Log("Failed to allow existing instance to make itself the foreground app");
                }
            }
            else
            {
                Log("Failed to obtain named pipe server process id");
            }
        }
    }

    private static void ValidateAppId(string appId)
    {
        ArgumentException.ThrowIfNullOrEmpty(appId);

        if (appId.Length > MaxAppIdLength)
        {
            throw new ArgumentException(
                $"The app ID must not exceed {MaxAppIdLength} characters.",
                nameof(appId));
        }

        foreach (char c in appId)
        {
            if (!IsAllowedAppIdCharacter(c))
            {
                throw new ArgumentException(
                    "The app ID may only contain ASCII letters, digits, '-' and '_'.",
                    nameof(appId));
            }
        }

        static bool IsAllowedAppIdCharacter(char c) =>
            c is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '-'
                or '_';
    }

    private static string GetPipeName(string appId)
    {
        if (OperatingSystem.IsWindows())
        {
            return $"{appId}-SoloPipe";
        }

        // On Unix-like systems, named pipes are implemented with Unix domain sockets. The path to the socket file has
        // a max length of 104 characters (macOS) or 108 (Linux), and by default, it creates the socket file in the
        // temporary folder. It's usually OK on Linux, but on macOS, the temp folder path is per user and can be pretty
        // long, which limits the available length for appId.
        // To avoid this, we specify a full path to the socket file in a folder that is guaranteed to be short enough.
        // On Linux, we can use the XDG_RUNTIME_DIR environment variable, which is usually set to /run/user/<uid>, which
        // is short enough. On macOS, and on Linux if XDG_RUNTIME_DIR is not set, we can use /tmp, which is also short
        // enough. We also prefix the socket file name with the user ID to avoid conflicts between users on the same
        // machine.

        if (OperatingSystem.IsLinux())
        {
            string? xdgRuntimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (!string.IsNullOrWhiteSpace(xdgRuntimeDir))
            {
                return Path.Combine(xdgRuntimeDir, $"{appId}-SoloPipe");
            }
        }

        return $"/tmp/{GetUnixUserId()}-{appId}-SoloPipe";
    }

    private static uint GetUnixUserId()
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException();
        }

        return getuid();
    }

    private void Log(string message) => _log?.Invoke(message);

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        _serverStream?.Dispose();
        _serverStream = null;
    }

    [DllImport("kernel32")]
    private static extern bool GetNamedPipeServerProcessId(SafeHandle pipeHandle, out uint serverProcessId);

    [DllImport("user32")]
    private static extern bool AllowSetForegroundWindow(uint processId);

    [DllImport("libc")]
    private static extern uint getuid();
}
