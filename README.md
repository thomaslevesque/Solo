# Solo

[![NuGet version](https://img.shields.io/nuget/vpre/Solo.svg?logo=nuget)](https://www.nuget.org/packages/Solo/absoluteLatest)

A simple library to run a .NET app as a single instance and notify the existing instance if any.
It works on Windows, Linux and macOS.

The first instance of the app starts normally and listens for new instances in the background. When a new instance
starts, it sends its arguments to the existing instance, which can react appropriately, then exits.

Note: the single instance behavior only applies within the same user session. Different users are still able to run the
same app at the same time.

## Getting started

Install the package, and add the following at the beginning of your `Main` method (or directly in `Program.cs`, if using
top-level statements):

```csharp
using var singleInstanceApp = SingleInstanceAppBuilder
    .WithId("MyTestApp")
    .OnNewInstance(context =>
        Console.WriteLine($"New instance started with args:\n{string.Join("\n", context.Args)}"))
    .Build();
if (!singleInstanceApp.TryStart(args))
{
    return;
}

// The rest of your code goes here
```

The delegate passed to `OnNewInstance` is invoked when another instance of the app is started, and receives
a `NewInstanceStartedContext` containing the command line arguments.

`TryStart` returns `true` if the app is the first instance and is able to start, and `false` if another instance is
already running. If another instance is already running but Solo fails to activate it, `TryStart` throws an
`ExistingInstanceActivationException`.

`appId` may only contain ASCII letters, digits, `-`, `_` and '.', and must not exceed 64 characters.

## How it works

When the first instance of the app starts, Solo attempts to create a named pipe.
- If the named pipe already exists, it means another instance of the app is already running. Solo then connects to the
  existing named pipe, and sends the arguments to the existing instance, so that it can react appropriately.
- If it doesn't, the app can start normally. Solo waits for connections from other instances to receive their arguments.

On Unix-like systems, an app terminated abruptly can leave its domain socket file behind. If the existing socket does
not accept a connection within 500 ms, Solo removes the stale socket and retries startup once.

Additionally, on Windows, the new instance allows the existing instance to set the foreground window. This is
necessary because of the [rules](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow#remarks)
to prevent apps from stealing focus: without this, the existing instance could react when a new instance is started, but
it would stay in the background, which would lead to a poor user experience.
