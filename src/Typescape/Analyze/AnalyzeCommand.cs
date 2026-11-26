using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Globalization;
using Typescape.Analyzer;

namespace Typescape.Analyze;

/// <summary><c>typescape analyze [&lt;input&gt;]</c>: analyzes a solution and writes its Snapshot.</summary>
internal sealed class AnalyzeCommand : Command
{
    public delegate Task<AnalysisResult> AnalyzeAsync(
        AnalysisRequest request,
        IProgress<AnalysisProgress> progress,
        CancellationToken cancellationToken);

    private readonly Argument<string?> input = new("input")
    {
        Description = "A .sln or .slnx file, or a directory that holds exactly one. Default: the current directory.",
        Arity = ArgumentArity.ZeroOrOne,

        // System.CommandLine binds an unknown option such as -c to the argument; a path like that can be ./-c.
        Validators =
        {
            result =>
            {
                if (result.Tokens is [{ Value: ['-', ..] option }])
                {
                    result.AddError($"Unrecognized option '{option}'.");
                }
            },
        },
    };

    private readonly Option<string?> output = new("--output", "-o")
    {
        Description = $"Where to write the Snapshot. Default: ./<Solution>{SnapshotFile.Extension}.",
    };

    private readonly Option<string?> framework = new("--framework", "-f")
    {
        Description = "The target framework of a multi-targeting project to analyze. Default: its highest.",
        HelpName = "tfm",
    };

    private readonly Option<int> maxParallelism = new("--max-parallelism")
    {
        Description = "How many projects to compile and walk at once; 1 is sequential.",
        HelpName = "n",
        DefaultValueFactory = _ => Environment.ProcessorCount,
        CustomParser = result =>
        {
            var value = result.Tokens.Single().Value;
            if (int.TryParse(value, CultureInfo.InvariantCulture, out var n) && n >= 1)
            {
                return n;
            }

            result.AddError($"--max-parallelism takes a whole number of at least 1, not '{value}'.");
            return 0;
        },
    };

    private readonly Option<bool> noOccurrences = new("--no-occurrences")
    {
        Description = "Writes no Occurrences: the Viewer cannot expand a Dependency to the lines it appears on.",
    };

    private readonly Option<bool> noRestore = new("--no-restore")
    {
        Description = "Skips dotnet restore and the packages.config restore. Reference assemblies are still fetched.",
    };

    private readonly Option<IReadOnlyDictionary<string, string>> properties = new("--property", "-p")
    {
        Description = "An MSBuild global property, such as -p:Configuration=Release; one per -p, the value whole, ; included. Repeatable. Default configuration: Debug.",
        HelpName = "Name=Value",
        Arity = ArgumentArity.OneOrMore,
        DefaultValueFactory = _ => new Dictionary<string, string>(),
        CustomParser = ParseProperties,
    };

    private readonly Option<string[]> testPatterns = new("--test")
    {
        Description = "Marks the projects whose name matches as test projects: a wildcard pattern (*, ?), any case; repeatable.",
        HelpName = "pattern",
        DefaultValueFactory = _ => [],
    };

    private readonly Option<string[]> notTestPatterns = new("--not-test")
    {
        Description = "Marks the projects whose name matches as not test projects; wins over --test. Repeatable.",
        HelpName = "pattern",
        DefaultValueFactory = _ => [],
    };

    private readonly Option<FailOn> failOn = new Option<FailOn>("--fail-on")
    {
        Description = "Exits 3 when a project's Load Health is this or worse. Default: none.",
        HelpName = "none|degraded|failed",
    }.AcceptOnlyFromAmong("none", "degraded", "failed");

    private readonly Option<Verbosity> verbosity = new Option<Verbosity>("--verbosity", "-v")
    {
        Description = "How much progress and Load Health to print on stderr. Default: normal.",
        HelpName = "quiet|normal|detailed",
    }.AcceptOnlyFromAmong("quiet", "normal", "detailed");

    private readonly string currentDirectory;
    private readonly bool interactive;
    private readonly AnalyzeAsync analyze;

    /// <param name="currentDirectory">What the input and output paths are relative to.</param>
    /// <param name="interactive">Whether stderr is a terminal, which gets live counters.</param>
    /// <param name="analyze">Runs the analysis; the Analyzer outside tests.</param>
    public AnalyzeCommand(string currentDirectory, bool interactive, AnalyzeAsync analyze)
        : base("analyze", "Analyzes a solution and writes its Snapshot.")
    {
        this.currentDirectory = currentDirectory;
        this.interactive = interactive;
        this.analyze = analyze;
        Add(input);
        Add(output);
        Add(framework);
        Add(maxParallelism);
        Add(noOccurrences);
        Add(noRestore);
        Add(properties);
        Add(testPatterns);
        Add(notTestPatterns);
        Add(failOn);
        Add(verbosity);
        SetAction(RunAsync);
    }

    private async Task<int> RunAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var console = parseResult.InvocationConfiguration;
        if (!SolutionInput.TryResolve(parseResult.GetValue(input), currentDirectory, out var solution, out var error))
        {
            await console.Error.WriteLineAsync(error);
            return ExitCodes.Usage;
        }

        var path = parseResult.GetValue(output) is { } given
            ? Path.GetFullPath(given, currentDirectory)
            : SnapshotFile.DefaultPath(solution, currentDirectory);

        var progress = new AnalyzeOutput(console.Error, parseResult.GetValue(verbosity), interactive);
        var result = await analyze(new AnalysisRequest
        {
            SolutionPath = solution,
            Framework = parseResult.GetValue(framework),
            MaxParallelism = parseResult.GetValue(maxParallelism),
            Occurrences = !parseResult.GetValue(noOccurrences),
            Restore = !parseResult.GetValue(noRestore),
            Properties = parseResult.GetRequiredValue(properties),
            TestPatterns = parseResult.GetRequiredValue(testPatterns),
            NotTestPatterns = parseResult.GetRequiredValue(notTestPatterns),
        }, progress, cancellationToken);

        progress.Start("write");
        var stopwatch = Stopwatch.StartNew();
        await SnapshotFile.WriteAsync(result.Snapshot, path, cancellationToken);
        progress.Finish(stopwatch.Elapsed);
        progress.LoadHealth(result.LoadHealth, result.Snapshot);

        await console.Output.WriteLineAsync(path);
        return ExitCodes.Success;
    }

    private static Dictionary<string, string> ParseProperties(ArgumentResult result)
    {
        // MSBuild property names ignore case; a later property wins, with its spelling, as on the msbuild command line.
        var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in result.Tokens)
        {
            if (token.Value.Split('=', 2) is [{ Length: > 0 } name, var value])
            {
                parsed.Remove(name);
                parsed[name] = value;
            }
            else
            {
                result.AddError($"--property takes Name=Value, not '{token.Value}'.");
            }
        }

        return parsed;
    }
}
