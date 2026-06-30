using System.Diagnostics;

namespace Solo.Tests;

public class TestAppIntegrationTests
{
    [Fact]
    public async Task StartingSecondProcess_NotifiesFirstProcess()
    {
        string testAppDll = ResolveTestAppDllPath();
        string appId = CreateTestAppId();

        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var receivedOneArg = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var receivedTwoArg = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var firstProcess = CreateTestAppProcess(testAppDll, appId, "first");
        AttachOutputSignals(firstProcess, firstStarted, receivedOneArg, receivedTwoArg);
        firstProcess.Start();
        firstProcess.BeginOutputReadLine();
        firstProcess.BeginErrorReadLine();

        try
        {
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

            using var secondProcess = CreateTestAppProcess(testAppDll, appId, "one", "two");
            secondProcess.Start();
            secondProcess.BeginOutputReadLine();
            secondProcess.BeginErrorReadLine();

            await secondProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, secondProcess.ExitCode);

            await receivedOneArg.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await receivedTwoArg.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            if (!firstProcess.HasExited)
            {
                await firstProcess.StandardInput.WriteLineAsync();
                await firstProcess.StandardInput.FlushAsync();
                await WaitForExitOrKillAsync(firstProcess, TimeSpan.FromSeconds(5));
            }
        }
    }

    private static Process CreateTestAppProcess(string testAppDllPath, string appId, params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = CreateDotnetArguments(testAppDllPath, args),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.Environment["SOLO_TEST_APP_ID"] = appId;

        return new Process
        {
            StartInfo = startInfo
        };
    }

    private static void AttachOutputSignals(
        Process process,
        TaskCompletionSource firstStarted,
        TaskCompletionSource receivedOneArg,
        TaskCompletionSource receivedTwoArg)
    {
        process.OutputDataReceived += (_, e) =>
        {
            string? line = e.Data;
            if (line is null)
            {
                return;
            }

            if (line.Contains("Started as single instance with args:", StringComparison.Ordinal))
            {
                firstStarted.TrySetResult();
            }
            else if (line.Equals("- one", StringComparison.Ordinal))
            {
                receivedOneArg.TrySetResult();
            }
            else if (line.Equals("- two", StringComparison.Ordinal))
            {
                receivedTwoArg.TrySetResult();
            }
        };
    }

    private static async Task WaitForExitOrKillAsync(Process process, TimeSpan timeout)
    {
        Task waitTask = process.WaitForExitAsync();
        Task completedTask = await Task.WhenAny(waitTask, Task.Delay(timeout));
        if (completedTask == waitTask)
        {
            await waitTask;
            return;
        }

        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
    }

    private static string ResolveTestAppDllPath()
    {
        string testAppAssemblyPath = typeof(TestApp.TestAppAssemblyMarker).Assembly.Location;
        if (string.IsNullOrWhiteSpace(testAppAssemblyPath))
        {
            throw new DirectoryNotFoundException(
                "Could not resolve the TestApp assembly location from the project reference.");
        }

        if (!File.Exists(testAppAssemblyPath))
        {
            throw new FileNotFoundException(
                $"Resolved TestApp assembly path does not exist: '{testAppAssemblyPath}'.",
                testAppAssemblyPath);
        }

        return testAppAssemblyPath;
    }

    private static string CreateDotnetArguments(string testAppDllPath, string[] args)
    {
        var escapedArgs = new List<string>(args.Length + 1)
        {
            Quote(testAppDllPath)
        };
        escapedArgs.AddRange(args.Select(Quote));
        return string.Join(' ', escapedArgs);
    }

    private static string CreateTestAppId()
    {
        string suffix = Guid.NewGuid().ToString("N")[..8];
        return $"i-{suffix}";
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
