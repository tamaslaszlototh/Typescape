using System.CommandLine;
using Typescape.Analyzer;

namespace Typescape.Analyze;

/// <summary><c>typescape analyze [&lt;input&gt;]</c>: analyzes a solution and writes its Snapshot.</summary>
internal sealed class AnalyzeCommand : Command
{
    public delegate Task<AnalysisResult> AnalyzeAsync(AnalysisRequest request, CancellationToken cancellationToken);

    private readonly Argument<string?> input = new("input")
    {
        Description = "A .sln or .slnx file, or a directory that holds exactly one. Default: the current directory.",
        Arity = ArgumentArity.ZeroOrOne,
    };

    private readonly Option<string?> output = new("--output", "-o")
    {
        Description = $"Where to write the Snapshot. Default: ./<Solution>{SnapshotFile.Extension}.",
    };

    private readonly string currentDirectory;
    private readonly AnalyzeAsync analyze;

    /// <param name="currentDirectory">What the input and output paths are relative to.</param>
    /// <param name="analyze">Runs the analysis; the Analyzer outside tests.</param>
    public AnalyzeCommand(string currentDirectory, AnalyzeAsync analyze)
        : base("analyze", "Analyzes a solution and writes its Snapshot.")
    {
        this.currentDirectory = currentDirectory;
        this.analyze = analyze;
        Add(input);
        Add(output);
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

        var result = await analyze(new AnalysisRequest { SolutionPath = solution }, cancellationToken);
        await SnapshotFile.WriteAsync(result.Snapshot, path, cancellationToken);

        await console.Output.WriteLineAsync(path);
        return ExitCodes.Success;
    }
}
