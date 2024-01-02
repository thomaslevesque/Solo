using System.Runtime.CompilerServices;
using static Bullseye.Targets;
using static SimpleExec.Command;

var commandLineOptions = CommandLineOptions.Parse(args);

Directory.SetCurrentDirectory(GetSolutionDirectory());

string artifactsDir = Path.GetFullPath("artifacts");
string logsDir = Path.Combine(artifactsDir, "logs");
string buildLogFile = Path.Combine(logsDir, "build.binlog");
string packagesDir = Path.Combine(artifactsDir, "packages");

string solutionFile = "Solo.sln";
string libraryProject = "src/Solo/Solo.csproj";

Target(
    "artifactDirectories",
    () =>
    {
        Directory.CreateDirectory(artifactsDir);
        Directory.CreateDirectory(logsDir);
        Directory.CreateDirectory(packagesDir);
    });

Target(
    "build",
    DependsOn("artifactDirectories"),
    () => Run(
        "dotnet",
        $"build -c \"{commandLineOptions.Configuration}\" /bl:\"{buildLogFile}\" \"{solutionFile}\""));

Target(
    "pack",
    DependsOn("artifactDirectories", "build"),
    () => Run(
        "dotnet",
        $"pack -c \"{commandLineOptions.Configuration}\" --no-build -o \"{packagesDir}\" \"{libraryProject}\""));

Target("default", DependsOn("pack"));

if (commandLineOptions.ShowHelp)
{
    await CommandLineOptions.PrintUsageAsync();
    return;
}

await RunTargetsWithoutExitingAsync(commandLineOptions.BullseyeArgs);

static string GetSolutionDirectory() =>
    Path.GetFullPath(Path.Combine(GetScriptDirectory(), @"..\.."));

static string GetScriptDirectory([CallerFilePath] string filename = null) => Path.GetDirectoryName(filename);

record CommandLineOptions(string Configuration, bool ShowHelp, string[] BullseyeArgs)
{
    public static CommandLineOptions Parse(string[] args)
    {
        var bullseyeArgs = new List<string>();
        string configuration = "Release";
        bool showHelp = false;
        using var enumerator = ((IEnumerable<string>)args).GetEnumerator();
        while (enumerator.MoveNext())
        {
            var arg = enumerator.Current;
            if (arg is "-h" or "--help")
            {
                showHelp = true;
                break;
            }
            else if (arg is "-c" or "--configuration")
            {
                configuration = ReadOptionValue(arg);
            }
            else
            {
                bullseyeArgs.Add(arg);
            }
        }

        return new(configuration, showHelp, bullseyeArgs.ToArray());

        string ReadOptionValue(string arg)
        {
            if (!enumerator.MoveNext())
                throw new InvalidOperationException($"Expected value for option '{arg}', but none was found.");

            return enumerator.Current;
        }
    }

    public static async Task PrintUsageAsync()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  build [-c|--configuration <buildConfiguration>] <bullseyeArgs>");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  <bullseyeArguments>  Arguments to pass to Bullseye (targets and options, see below)");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -c, --configuration <buildConfiguration>  The configuration to build [default: Release]");
        Console.WriteLine("  -? -h, --help                             Show help and usage information");
        Console.WriteLine();
        Console.WriteLine("Bullseye help:");
        await RunTargetsWithoutExitingAsync(["--help"]);
    }
}
