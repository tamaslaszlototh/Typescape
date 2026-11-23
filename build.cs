// Local runner: dotnet run build.cs -- <target> [--coverage]
// A .NET 10 file-based app; it inherits Directory.Build.props (net10.0, nullable, warnings as errors).
// It references Typescape.Snapshot to export the Snapshot JSON Schema from the records in process (ADR 0003).

#:project src/Typescape.Snapshot/Typescape.Snapshot.csproj

using System.Diagnostics;
using System.Xml.Linq;
using Typescape.Snapshot.Serialization;

var root = AppContext.GetData("EntryPointFileDirectoryPath") as string ?? Directory.GetCurrentDirectory();

Step dotnetTests = new("dotnet tests (rule + golden)", "dotnet", ["test", "--solution", "Typescape.slnx"], CollectsCoverage: true);
Step ruleTests = new("dotnet tests (rule)", "dotnet", ["test", "--solution", "Typescape.slnx", "--filter-not-trait", "Category=Golden"], CollectsCoverage: true);

var targets = new Dictionary<string, Step[]>
{
    ["test"] = [dotnetTests],
    ["pre-push"] = [ruleTests],
};

switch (args)
{
    case ["install-hooks"]:
        return Hooks.Install(root);
    case ["schema"]:
        return Schema.Write(root);
    case [var name, .. var options] when targets.TryGetValue(name, out var steps) && options is [] or ["--coverage"]:
        return RunTarget(name, steps, root, coverage: options is ["--coverage"]);
    default:
        Console.Error.WriteLine("Usage: dotnet run build.cs -- <target> [--coverage]");
        Console.Error.WriteLine($"Targets: {string.Join(", ", targets.Keys)}, schema, install-hooks");
        Console.Error.WriteLine($"schema: export the Snapshot JSON Schema from the records to {Schema.RelativePath}.");
        Console.Error.WriteLine("--coverage: collect line coverage from the dotnet test steps and print it (report only, never fails).");
        return 2;
}

static int RunTarget(string name, Step[] steps, string root, bool coverage)
{
    var coverageDirectory = Path.Combine(root, "TestResults", "coverage");
    if (coverage && Directory.Exists(coverageDirectory))
    {
        Directory.Delete(coverageDirectory, recursive: true);
    }

    var results = new List<StepResult>();
    foreach (var step in steps)
    {
        Console.WriteLine($"==> {step.Name}");
        var watch = Stopwatch.StartNew();
        string[] extra = coverage && step.CollectsCoverage
            ? ["--coverage", "--coverage-output-format", "cobertura", "--results-directory", coverageDirectory]
            : [];
        var exitCode = Run(step, extra, root);
        results.Add(new StepResult(step, exitCode, watch.Elapsed));
    }

    Console.WriteLine();
    Console.WriteLine($"Target '{name}':");
    foreach (var result in results)
    {
        var status = result.Passed ? "passed" : $"FAILED (exit {result.ExitCode})";
        Console.WriteLine(FormattableString.Invariant($"  {status,-18} {result.Step.Name} [{result.Elapsed.TotalSeconds:F1}s]"));
    }

    if (coverage)
    {
        Coverage.Report(coverageDirectory);
    }

    return results.All(r => r.Passed) ? 0 : 1;
}

static int Run(Step step, string[] extraArguments, string workingDirectory)
{
    var info = new ProcessStartInfo(step.FileName) { WorkingDirectory = workingDirectory };
    foreach (var argument in step.Arguments.Concat(extraArguments))
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

static class Schema
{
    public const string RelativePath = "schema/snapshot.schema.json";

    public static int Write(string root)
    {
        var path = Path.Combine(root, RelativePath);
        var schema = SnapshotSchema.Export();
        if (File.Exists(path) && File.ReadAllText(path) == schema)
        {
            Console.WriteLine($"{RelativePath} is up to date.");
            return 0;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, schema);
        Console.WriteLine($"Wrote {RelativePath}.");
        return 0;
    }
}

static class Coverage
{
    public static void Report(string directory)
    {
        try
        {
            var files = Directory.Exists(directory)
                ? Directory.GetFiles(directory, "*.cobertura.xml", SearchOption.AllDirectories)
                : [];
            if (files.Length == 0)
            {
                Console.WriteLine("Coverage: no Cobertura files found.");
                return;
            }

            var hitsByLine = new Dictionary<(string Assembly, string File, string Line), bool>();
            foreach (var file in files)
            {
                foreach (var package in XDocument.Load(file).Descendants("package"))
                {
                    var assembly = (string?)package.Attribute("name") ?? "?";
                    if (assembly.EndsWith(".Tests", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    foreach (var @class in package.Descendants("class"))
                    {
                        var source = (string?)@class.Attribute("filename") ?? "?";
                        foreach (var line in @class.Elements("lines").Elements("line"))
                        {
                            var key = (assembly, source, (string?)line.Attribute("number") ?? "?");
                            var covered = ((long?)line.Attribute("hits") ?? 0) > 0;
                            hitsByLine[key] = hitsByLine.GetValueOrDefault(key) || covered;
                        }
                    }
                }
            }

            var rows = hitsByLine
                .GroupBy(entry => entry.Key.Assembly, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => (Name: group.Key, Covered: group.Count(entry => entry.Value), Total: group.Count()))
                .ToList();
            rows.Add(("Total", rows.Sum(row => row.Covered), rows.Sum(row => row.Total)));

            static string Percent(int covered, int total) =>
                total == 0 ? "n/a" : FormattableString.Invariant($"{100.0 * covered / total:F1}%");

            Console.WriteLine();
            Console.WriteLine("Line coverage (report only):");
            foreach (var row in rows)
            {
                Console.WriteLine($"  {Percent(row.Covered, row.Total),7}  {row.Covered}/{row.Total}  {row.Name}");
            }

            if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } summary)
            {
                var os = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : "Linux";
                var markdown = new List<string> { $"### Line coverage ({os})", "", "| Assembly | Lines | Coverage |", "|---|---:|---:|" };
                markdown.AddRange(rows.Select(row => $"| {row.Name} | {row.Covered}/{row.Total} | {Percent(row.Covered, row.Total)} |"));
                File.AppendAllLines(summary, [.. markdown, ""]);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException or FormatException)
        {
            Console.WriteLine($"Coverage: could not read the report ({e.Message}).");
        }
    }
}

record Step(string Name, string FileName, string[] Arguments, bool CollectsCoverage = false);

record StepResult(Step Step, int ExitCode, TimeSpan Elapsed)
{
    public bool Passed => ExitCode == 0;
}
