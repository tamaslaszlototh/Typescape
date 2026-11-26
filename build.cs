// Local runner: dotnet run build.cs -- <target> [--coverage]
// A .NET 10 file-based app; it inherits Directory.Build.props (net10.0, nullable, warnings as errors).
// It references Typescape.Snapshot to export the Snapshot JSON Schema from the records in process (ADR 0003) and to read
// the Provenance of the Snapshots that bench and scale write.

#:project src/Typescape.Snapshot/Typescape.Snapshot.csproj

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Typescape.Snapshot.Files;
using Typescape.Snapshot.Json;
using Typescape.Snapshot.Records;

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
    case ["bench", .. var options] when Bench.TryParseOptions(options, out var machine, out var saveBaseline):
        return Bench.Run(Bench.Jellyfin, root, machine, saveBaseline);
    case ["scale"]:
        return Bench.Run(Bench.Aspire, root, machine: null, saveBaseline: false);
    case [var name, .. var options] when targets.TryGetValue(name, out var steps) && options is [] or ["--coverage"]:
        return RunTarget(name, steps, root, coverage: options is ["--coverage"]);
    default:
        Console.Error.WriteLine("Usage: dotnet run build.cs -- <target> [--coverage]");
        Console.Error.WriteLine($"Targets: {string.Join(", ", targets.Keys)}, bench, scale, schema, install-hooks");
        Console.Error.WriteLine("--coverage: collect line coverage from the dotnet test steps and print it (report only, never fails).");
        Console.Error.WriteLine($"bench [--machine <name>] [--save-baseline]: analyze Jellyfin and compare with {Bench.BaselineDirectory}/baseline.<machine>.json;");
        Console.Error.WriteLine($"  a regression over {Bench.Threshold} warns, never fails. <machine> defaults to the CPU model and RAM, e.g. apple-m3-16gb.");
        Console.Error.WriteLine("  --save-baseline records this run as the machine's baseline.");
        Console.Error.WriteLine("scale: analyze dotnet/aspire (needs .NET SDK 10.0.400 or later, about 9 GB of disk) and report its peak memory.");
        Console.Error.WriteLine($"schema: export the Snapshot JSON Schema from the records to {Schema.RelativePath}.");
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
        var exitCode = Tool.Run(step, extra, root);
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

static class Tool
{
    /// <summary>Runs <paramref name="step"/> with the console inherited; 127 when it cannot be started.</summary>
    public static int Run(Step step, string[] extraArguments, string workingDirectory)
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

    /// <summary>The trimmed stdout of a run that exits 0; <c>null</c> otherwise.</summary>
    public static string? Read(string fileName, string workingDirectory, params string[] arguments)
    {
        var info = new ProcessStartInfo(fileName) { WorkingDirectory = workingDirectory, RedirectStandardOutput = true };
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
            Console.Error.WriteLine($"Could not start '{fileName}': {e.Message}");
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
        var hooksPath = Tool.Read("git", root, "rev-parse", "--git-path", "hooks");
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

/// <param name="RequiresCleanLoad">Fail the run unless every project's Load Health is ok: other timings compare with nothing.</param>
record Benchmark(string Name, string Repository, string Commit, string Solution, bool RequiresCleanLoad, bool HasBaseline);

/// <summary>The numbers of one benchmark run, as written to TestResults/bench and to a baseline.</summary>
record BenchResult(
    string Benchmark,
    string Commit,
    string Machine,
    string Os,
    string SdkVersion,
    string ToolVersion,
    string? ToolCommit,
    DateTimeOffset Timestamp,
    BenchSeconds Seconds,
    long PeakMemoryBytes);

record BenchSeconds(double Load, double Compile, double Diagnostics, double Walk, double Assemble, double Total);

// Source-generated: a file-based app is analyzed for Native AOT, which rules out reflection-based serialization.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(BenchResult))]
partial class BenchJson : JsonSerializerContext;

static class Bench
{
    public const string BaselineDirectory = "bench";
    const double RegressionThreshold = 0.25;
    public static readonly string Threshold = RegressionThreshold.ToString("0%", System.Globalization.CultureInfo.InvariantCulture);

    // Typescape's exit code for a Snapshot written although --fail-on was crossed.
    const int LoadHealthFailure = 3;

    // A phase that grows by less than this is runner noise, however large the percentage.
    const double NoiseSeconds = 0.5;

    public static readonly Benchmark Jellyfin = new(
        "jellyfin", "https://github.com/jellyfin/jellyfin.git", "ac1ce5dc5189376c3d4571e83d0cc88f2a4bf2f0", "Jellyfin.slnx",
        RequiresCleanLoad: true, HasBaseline: true);

    public static readonly Benchmark Aspire = new(
        "aspire", "https://github.com/dotnet/aspire.git", "e076d8e427cb3afb528dbd605acd74c3aea69f94", "Aspire.slnx",
        RequiresCleanLoad: false, HasBaseline: false);

    public static bool TryParseOptions(string[] options, out string? machine, out bool saveBaseline)
    {
        machine = null;
        saveBaseline = false;
        for (var i = 0; i < options.Length; i++)
        {
            switch (options[i])
            {
                case "--save-baseline":
                    saveBaseline = true;
                    break;
                case "--machine" when i + 1 < options.Length && Regex.IsMatch(options[i + 1], "^[a-z0-9]+(-[a-z0-9]+)*$"):
                    machine = options[++i];
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    public static int Run(Benchmark benchmark, string root, string? machine, bool saveBaseline)
    {
        // Outside this repo, whose Directory.Build.props, Directory.Packages.props and global.json would otherwise apply to it.
        var checkout = Path.Combine(CacheRoot(), "bench", benchmark.Name);
        var solution = Path.Combine(checkout, benchmark.Solution);
        var results = Path.Combine(root, "TestResults", "bench");
        var snapshot = Path.Combine(results, $"{benchmark.Name}.typescape.json.gz");
        Directory.CreateDirectory(results);

        if (!Checkout(benchmark, checkout))
        {
            return 1;
        }

        Step restore = new($"restore {benchmark.Solution} (not measured)", "dotnet", ["restore", solution]);
        Step analyze = new(
            $"analyze {benchmark.Solution}",
            "dotnet",
            [
                "run", "--project", Path.Combine(root, "src", "Typescape", "Typescape.csproj"), "-c", "Release", "--",
                "analyze", solution, "-o", snapshot, "--no-restore", .. benchmark.RequiresCleanLoad ? ["--fail-on", "degraded"] : Array.Empty<string>(),
            ]);

        foreach (var step in new[] { restore, analyze })
        {
            Console.WriteLine($"==> {step.Name}");

            // From this repo, so its global.json picks the SDK that builds Typescape, not the benchmarked solution's.
            var exitCode = Tool.Run(step, [], root);
            if (exitCode == LoadHealthFailure && step == analyze && benchmark.RequiresCleanLoad)
            {
                Console.Error.WriteLine($"{benchmark.Solution} has a project whose Load Health is not ok, so its numbers compare with nothing.");
                return 1;
            }

            if (exitCode != 0)
            {
                Console.Error.WriteLine($"{step.Name} failed (exit {exitCode}).");
                return 1;
            }
        }

        BenchResult result;
        try
        {
            using var stream = File.OpenRead(snapshot);
            result = Result(benchmark, machine ?? MachineName(), SnapshotReader.Read(stream).Provenance);
        }
        catch (Exception e) when (e is IOException or SnapshotReadException)
        {
            Console.Error.WriteLine($"Could not read the Snapshot {snapshot}: {e.Message}");
            return 1;
        }

        Write(Path.Combine(results, $"{benchmark.Name}.json"), result);
        Console.WriteLine();
        Console.WriteLine($"{benchmark.Solution} at {benchmark.Commit[..8]} on {result.Machine} ({result.Os}, SDK {result.SdkVersion}):");

        if (!benchmark.HasBaseline)
        {
            Print(baseline: null, result);
            return 0;
        }

        var relativeBaseline = $"{BaselineDirectory}/baseline.{result.Machine}.json";
        var baselinePath = Path.Combine(root, relativeBaseline);
        if (saveBaseline)
        {
            Print(baseline: null, result);
            Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
            Write(baselinePath, result);
            Console.WriteLine($"Wrote {relativeBaseline}.");
            return 0;
        }

        if (!File.Exists(baselinePath))
        {
            Print(baseline: null, result);
            Console.WriteLine($"No baseline for {result.Machine}: rerun with --save-baseline to record {relativeBaseline}.");
            return 0;
        }

        var baseline = JsonSerializer.Deserialize(File.ReadAllText(baselinePath), BenchJson.Default.BenchResult)!;
        if (baseline.Commit != result.Commit || baseline.SdkVersion != result.SdkVersion)
        {
            Console.WriteLine(
                $"Note: the baseline is for {baseline.Commit[..8]} with SDK {baseline.SdkVersion}; this run is {result.Commit[..8]} with SDK {result.SdkVersion}.");
        }

        var regressions = Print(baseline, result);
        Console.WriteLine(regressions == 0
            ? $"No regression over {Threshold} against {relativeBaseline}."
            : $"{regressions} regression(s) over {Threshold} against {relativeBaseline}; investigate, but this never fails.");
        return 0;
    }

    static bool Checkout(Benchmark benchmark, string directory)
    {
        if (Directory.Exists(directory) && Tool.Read("git", directory, "rev-parse", "--verify", "--quiet", "HEAD") == benchmark.Commit)
        {
            return true;
        }

        Console.WriteLine($"==> fetch {benchmark.Repository} at {benchmark.Commit[..8]} into {directory}");
        Directory.CreateDirectory(directory);
        Step[] steps =
        [
            new("git init", "git", ["init", "--quiet"]),
            new("git fetch", "git", ["fetch", "--depth", "1", benchmark.Repository, benchmark.Commit]),
            new("git checkout", "git", ["-c", "advice.detachedHead=false", "-c", "core.longpaths=true", "checkout", "--force", "--detach", "FETCH_HEAD"]),
        ];
        foreach (var step in steps)
        {
            if (Tool.Run(step, [], directory) is not 0 and var exitCode)
            {
                Console.Error.WriteLine($"{step.Name} failed (exit {exitCode}).");
                return false;
            }
        }

        return true;
    }

    static void Write(string path, BenchResult result) =>
        File.WriteAllText(path, JsonSerializer.Serialize(result, BenchJson.Default.BenchResult) + "\n");

    static BenchResult Result(Benchmark benchmark, string machine, ProvenanceRecord provenance)
    {
        var phases = provenance.PhaseSeconds;
        var total = Math.Round(phases.Load + phases.Compile + phases.Diagnostics + phases.Walk + phases.Assemble, 3);
        return new BenchResult(
            benchmark.Name,
            benchmark.Commit,
            machine,
            provenance.Os,
            provenance.SdkVersion,
            provenance.Tool.Version,
            provenance.Tool.Commit,
            provenance.Timestamp,
            new BenchSeconds(phases.Load, phases.Compile, phases.Diagnostics, phases.Walk, phases.Assemble, total),
            provenance.PeakMemoryBytes);
    }

    /// <returns>How many measures regressed past the threshold, each with a warning on stderr.</returns>
    static int Print(BenchResult? baseline, BenchResult result)
    {
        static (string Name, double Value, bool IsMemory)[] Measures(BenchResult r) =>
        [
            ("load", r.Seconds.Load, false),
            ("compile", r.Seconds.Compile, false),
            ("diagnostics", r.Seconds.Diagnostics, false),
            ("walk", r.Seconds.Walk, false),
            ("assemble", r.Seconds.Assemble, false),
            ("total", r.Seconds.Total, false),
            ("peak memory", r.PeakMemoryBytes / (double)(1L << 30), true),
        ];

        static string Format(double value, bool isMemory) =>
            isMemory ? FormattableString.Invariant($"{value:F2} GiB") : FormattableString.Invariant($"{value:F2} s");

        if (baseline is null)
        {
            foreach (var (name, value, isMemory) in Measures(result))
            {
                Console.WriteLine($"  {name,-12} {Format(value, isMemory),10}");
            }

            return 0;
        }

        Console.WriteLine($"  {"",-12} {"baseline",10} {"this run",10} {"change",7}");
        var warnings = new List<string>();
        foreach (var ((name, before, isMemory), (_, after, _)) in Measures(baseline).Zip(Measures(result)))
        {
            var change = before == 0 ? 0 : (after - before) / before;
            Console.WriteLine(FormattableString.Invariant($"  {name,-12} {Format(before, isMemory),10} {Format(after, isMemory),10} {change,7:+0%;-0%;0%}"));
            if (change > RegressionThreshold && (isMemory || after - before >= NoiseSeconds))
            {
                warnings.Add(FormattableString.Invariant(
                    $"warning: {name} regressed {change:+0%} against the baseline ({Format(before, isMemory)} -> {Format(after, isMemory)})."));
            }
        }

        warnings.ForEach(Console.Error.WriteLine);
        return warnings.Count;
    }

    /// <summary>The CPU model and RAM, e.g. <c>apple-m3-16gb</c>: it tells machines apart without naming them.</summary>
    static string MachineName()
    {
        var cpu = OperatingSystem.IsWindows()
            ? Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null) as string
            : Tool.Read("sysctl", Environment.CurrentDirectory, "-n", "machdep.cpu.brand_string");
        var gib = Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (double)(1L << 30));
        return Regex.Replace(FormattableString.Invariant($"{cpu ?? "unknown cpu"} {gib}gb").ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
    }

    // The Analyzer's cache root (ReferenceAssemblies), which build.cs cannot reference.
    static string CacheRoot() =>
        OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Caches", "Typescape")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Typescape");
}

record Step(string Name, string FileName, string[] Arguments, bool CollectsCoverage = false);

record StepResult(Step Step, int ExitCode, TimeSpan Elapsed)
{
    public bool Passed => ExitCode == 0;
}
