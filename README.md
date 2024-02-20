# Solo

> [!WARNING]
> This library is a work in progress, and might not work as intended. In particular, it hasn't been tested _at all_ on Linux yet.

A simple library to run a .NET app as single-instance and notify the existing instance, if any.

When the first instance of the app starts, it attempts to create a named pipe.
- If the named pipe already exists, it means another instance of the app already exists. Solo then connects to the existing named pipe, and sends the arguments to the existing instance, so that it can react appropriately.
- If it doesn't, the app can start normally. Solo waits for connections from other instances to receive their arguments.

Additionally, on Windows only, the new instance allows the existing instance to set the foreground window. This is necessary because of the [rules](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow#remarks) to prevent windows from stealing focus: without this, the existing instance could react when a new instance is started, but it would stay in the background, which would lead to a poor user experience.

## Getting started

Install the package, and add this at the beginning of your `Main` method (or directly in `Program.cs`, if using top-level statements):

```csharp
using var singleInstanceApp = new SingleInstanceApp(
    "MyTestApp",
    args => Console.WriteLine("New instance started!"));
if (!singleInstanceApp.TryStart(args))
{
    return;
}

// The rest of your code goes here
```

The delegate passed to the `SingleInstanceApp` constructor is invoked when another instance of the app is started.
