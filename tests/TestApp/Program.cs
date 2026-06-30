using Solo;

string appId = Environment.GetEnvironmentVariable("SOLO_TEST_APP_ID") ?? "MyTestApp";

using var singleInstanceApp = SingleInstanceAppBuilder
    .WithId(appId)
    .OnNewInstance(args =>
    {
        Console.WriteLine("New instance started with args:");
        foreach (var arg in args)
        {
            Console.WriteLine($"- {arg}");
        }
    })
    .OnLogMessage(message => Console.WriteLine($"[SingleInstanceApp]: {message}"))
    .Build();

if (!singleInstanceApp.TryStart(args))
{
    return;
}

Console.WriteLine("Started as single instance with args:");
foreach (var arg in args)
{
    Console.WriteLine($"- {arg}");
}
Console.ReadLine();
