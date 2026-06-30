// See https://aka.ms/new-console-template for more information

using Solo;

using var singleInstanceApp = SingleInstanceAppBuilder
    .WithId("MyTestApp")
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
