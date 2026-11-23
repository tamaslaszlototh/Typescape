// Local runner: dotnet run build.cs -- <target>
// A .NET 10 file-based app; it inherits Directory.Build.props (net10.0, nullable, warnings as errors).

using System.Diagnostics;

var root = AppContext.GetData("EntryPointFileDirectoryPath") as string ?? Directory.GetCurrentDirectory();

Step dotnetTests = new("dotnet tests (rule + golden)", "dotnet", ["test", "--solution", "Typescape.slnx"]);

var targets = new Dictionary<string, Step[]>
{
    ["test"] = [dotnetTests],
};

if (args.Length != 1 || !targets.TryGetValue(args[0], out var steps))
{
    Console.Error.WriteLine("Usage: dotnet run build.cs -- <target>");
    Console.Error.WriteLine($"Targets: {string.Join(", ", targets.Keys)}");
    return 2;
}

var results = new List<StepResult>();
foreach (var step in steps)
{
    Console.WriteLine($"==> {step.Name}");
    var watch = Stopwatch.StartNew();
    var exitCode = Run(step, root);
    results.Add(new StepResult(step, exitCode, watch.Elapsed));
}

Console.WriteLine();
Console.WriteLine($"Target '{args[0]}':");
foreach (var result in results)
{
    var status = result.Passed ? "passed" : $"FAILED (exit {result.ExitCode})";
    Console.WriteLine(FormattableString.Invariant($"  {status,-18} {result.Step.Name} [{result.Elapsed.TotalSeconds:F1}s]"));
}

return results.All(r => r.Passed) ? 0 : 1;

static int Run(Step step, string workingDirectory)
{
    var info = new ProcessStartInfo(step.FileName) { WorkingDirectory = workingDirectory };
    foreach (var argument in step.Arguments)
    {
        info.ArgumentList.Add(argument);
    }

    try
    {
        using var process = Process.Start(info)!;
        process.WaitForExit();
        return process.ExitCode;
    }
    catch (System.ComponentModel.Win32Exception e)
    {
        Console.Error.WriteLine($"Could not start '{step.FileName}': {e.Message}");
        return 127;
    }
}

record Step(string Name, string FileName, string[] Arguments);

record StepResult(Step Step, int ExitCode, TimeSpan Elapsed)
{
    public bool Passed => ExitCode == 0;
}
