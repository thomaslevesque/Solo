using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Solo;

public class SingleInstanceApp : IDisposable
{
    private readonly object _syncLock = new();
    private readonly string _pipeName;
    private readonly Action<string>? _log;
    private readonly Action<string[]>? _onNewInstance;

    private NamedPipeServerStream? _serverStream;
    private CancellationTokenSource? _cancellationTokenSource;

    public SingleInstanceApp(
        string appId,
        Action<string[]>? onNewInstance = null,
        Action<string>? log = null)
    {
        _onNewInstance = onNewInstance;
        _pipeName = $"{appId}-Pipe";
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
                    PipeOptions.FirstPipeInstance | PipeOptions.Asynchronous);
                Log("Successfully created named pipe server, listening for connections.");

                var cts = _cancellationTokenSource = new CancellationTokenSource();
                Task.Run(() => RunServerAsync(cts.Token), cts.Token);
                return true;
            }
            catch (IOException)
            {
                Log("Another instance is already running. Activating existing instance.");
                ActivateExistingInstance(args);
                return false;
            }
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
        }
        catch (Exception ex)
        {
            Log($"Failed to send args to existing instance: {ex}");
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
}
