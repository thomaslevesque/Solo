namespace Solo;

public sealed class SingleInstanceAppBuilder
{
    private readonly string _appId;
    private Action<string[]>? _onNewInstance;
    private Action<string>? _onLogMessage;

    private SingleInstanceAppBuilder(string appId)
    {
        _appId = appId;
    }

    public static SingleInstanceAppBuilder WithId(string appId)
    {
        return new SingleInstanceAppBuilder(appId);
    }

    public SingleInstanceAppBuilder OnNewInstance(Action<string[]> onNewInstance)
    {
        ArgumentNullException.ThrowIfNull(onNewInstance);
        _onNewInstance = onNewInstance;
        return this;
    }

    public SingleInstanceAppBuilder OnLogMessage(Action<string> onLogMessage)
    {
        ArgumentNullException.ThrowIfNull(onLogMessage);
        _onLogMessage = onLogMessage;
        return this;
    }

    public SingleInstanceApp Build()
    {
        return new SingleInstanceApp(_appId, _onNewInstance, _onLogMessage);
    }
}
