using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Solo;

namespace Solo.Tests;

public class SingleInstanceAppTests
{
    [Fact]
    public void Build_ThrowsForEmptyAppId()
    {
        Assert.Throws<ArgumentException>(() => SingleInstanceAppBuilder.WithId(string.Empty).Build());
    }

    [Fact]
    public void Build_ThrowsForTooLongAppId()
    {
        string appId = new('a', 65);

        var ex = Assert.Throws<ArgumentException>(() => SingleInstanceAppBuilder.WithId(appId).Build());
        Assert.Equal("appId", ex.ParamName);
    }

    [Fact]
    public void Build_AllowsMaxLengthAppId()
    {
        string appId = new('a', 64);

        using var app = SingleInstanceAppBuilder.WithId(appId).Build();
        Assert.NotNull(app);
    }

    [Theory]
    [InlineData("app id")]
    [InlineData("é")]
    public void Build_ThrowsForInvalidAppIdCharacters(string appId)
    {
        var ex = Assert.Throws<ArgumentException>(() => SingleInstanceAppBuilder.WithId(appId).Build());

        Assert.Equal("appId", ex.ParamName);
        Assert.Contains("ASCII letters", ex.Message);
    }

    [Theory]
    [InlineData("app")]
    [InlineData("app.id")]
    [InlineData("app_id")]
    [InlineData("app-id")]
    [InlineData("App123.v2")]
    public void Build_AllowsValidAppIdNames(string appId)
    {
        using var app = SingleInstanceAppBuilder.WithId(appId).Build();
        Assert.NotNull(app);
    }

    [Fact]
    public void OnNewInstance_ThrowsForNullCallback()
    {
        Assert.Throws<ArgumentNullException>(() => SingleInstanceAppBuilder.WithId("app").OnNewInstance(null!));
    }

    [Fact]
    public void OnLogMessage_ThrowsForNullCallback()
    {
        Assert.Throws<ArgumentNullException>(() => SingleInstanceAppBuilder.WithId("app").OnLogMessage(null!));
    }

    [Fact]
    public void TryStart_ThrowsWhenCalledTwiceOnSameInstance()
    {
        string appId = CreateTestAppId();
        using var app = SingleInstanceAppBuilder.WithId(appId).Build();

        Assert.True(app.TryStart(["first-instance"]));
        Assert.Throws<InvalidOperationException>(() => app.TryStart(["second-attempt"]));
    }

    [Fact]
    public async Task TryStart_SecondInstanceNotifiesFirstInstance()
    {
        string appId = CreateTestAppId();
        var receivedContext = new TaskCompletionSource<NewInstanceStartedContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var firstInstance = SingleInstanceAppBuilder
            .WithId(appId)
            .OnNewInstance(context => receivedContext.TrySetResult(context))
            .Build();
        using var secondInstance = SingleInstanceAppBuilder.WithId(appId).Build();

        Assert.True(firstInstance.TryStart(["first"]));
        Assert.False(secondInstance.TryStart(["one", "two"]));

        NewInstanceStartedContext context = await receivedContext.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["one", "two"], context.Args);
    }

    [Fact]
    public void TryStart_RemovesStaleUnixSocket()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string appId = CreateTestAppId();
        string socketPath = GetUnixSocketPath(appId);
        try
        {
            using (var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
            {
                socket.Bind(new UnixDomainSocketEndPoint(socketPath));
                socket.Listen(1);
            }

            using var app = SingleInstanceAppBuilder.WithId(appId).Build();

            Assert.True(app.TryStart([]));
        }
        finally
        {
            File.Delete(socketPath);
        }
    }

    private static string CreateTestAppId()
    {
        string suffix = Guid.NewGuid().ToString("N")[..8];
        return $"t-{suffix}";
    }

    private static string GetUnixSocketPath(string appId)
    {
        string? xdgRuntimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        return !string.IsNullOrWhiteSpace(xdgRuntimeDir)
            ? Path.Combine(xdgRuntimeDir, $"{appId}.SoloPipe")
            : $"/tmp/{getuid()}-{appId}.SoloPipe";
    }

    [DllImport("libc")]
    private static extern uint getuid();
}
