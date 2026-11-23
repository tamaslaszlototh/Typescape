// Local runner: dotnet run build.cs -- <target>
// A .NET 10 file-based app; it inherits Directory.Build.props (net10.0, nullable, warnings as errors).

using System.Diagnostics;

var root = AppContext.GetData("EntryPointFileDirectoryPath") as string ?? Directory.GetCurrentDirectory();

Step dotnetTests = new("dotnet tests (rule + golden)", "dotnet", ["test", "--solution", "Typescape.slnx"]);
Step ruleTests = new("dotnet tests (rule)", "dotnet", ["test", "--solution", "Typescape.slnx", "--filter-not-trait", "Category=Golden"]);

var targets = new Dictionary<string, Step[]>
{
    ["test"] = [dotnetTests],
    ["pre-push"] = [ruleTests],
};

switch (args)
{
    case ["install-hooks"]:
        return Hooks.Install(root);
    case [var name] when targets.TryGetValue(name, out var steps):
        return RunTarget(name, steps, root);
    default:
        Console.Error.WriteLine("Usage: dotnet run build.cs -- <target>");
        Console.Error.WriteLine($"Targets: {string.Join(", ", targets.Keys)}, install-hooks");
        return 2;
}

static int RunTarget(string name, Step[] steps, string root)
{
    var results = new List<StepResult>();
    foreach (var step in steps)
    {
        Console.WriteLine($"==> {step.Name}");
        var watch = Stopwatch.StartNew();
        var exitCode = Run(step, root);
        results.Add(new StepResult(step, exitCode, watch.Elapsed));
    }

    Console.WriteLine();
    Console.WriteLine($"Target '{name}':");
    foreach (var result in results)
    {
        var status = result.Passed ? "passed" : $"FAILED (exit {result.ExitCode})";
        Console.WriteLine(FormattableString.Invariant($"  {status,-18} {result.Step.Name} [{result.Elapsed.TotalSeconds:F1}s]"));
    }

    return results.All(r => r.Passed) ? 0 : 1;
}

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

static class Git
{
    public static string? Read(string workingDirectory, params string[] arguments)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = workingDirectory, RedirectStandardOutput = true };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            Console.Error.WriteLine($"Could not start 'git': {e.Message}");
            return null;
        }
    }
}

static class Hooks
{
    const string PrePush =
        "#!/bin/sh\n" +
        "# Installed by `dotnet run build.cs -- install-hooks`; rerun it to update.\n" +
        "exec dotnet run --file \"$(git rev-parse --show-toplevel)/build.cs\" -- pre-push\n";

    public static int Install(string root)
    {
        var hooksPath = Git.Read(root, "rev-parse", "--git-path", "hooks");
        if (hooksPath is null)
        {
            Console.Error.WriteLine("Could not locate the git hooks directory.");
            return 1;
        }

        var directory = Path.GetFullPath(Path.Combine(root, hooksPath));
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "pre-push");
        File.WriteAllText(path, PrePush);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, (UnixFileMode)0b111_101_101);
        }

        Console.WriteLine($"Installed {path}");
        return 0;
    }
}

record Step(string Name, string FileName, string[] Arguments);

record StepResult(Step Step, int ExitCode, TimeSpan Elapsed)
{
    public bool Passed => ExitCode == 0;
}
