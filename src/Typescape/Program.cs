using System.CommandLine;
using Typescape.Analyze;

namespace Typescape;

internal static class Program
{
    private static Task<int> Main(string[] args) =>
        InvokeAsync(
            CreateRootCommand(Environment.CurrentDirectory, (request, cancellationToken) => Analyzer.Analyzer.AnalyzeAsync(request, null, cancellationToken)),
            args,
            new InvocationConfiguration(),
            CancellationToken.None);

    internal static RootCommand CreateRootCommand(string currentDirectory, AnalyzeCommand.AnalyzeAsync analyze) =>
        new("Analyzes a C# solution and shows its structure.")
        {
            new AnalyzeCommand(currentDirectory, analyze),
        };

    /// <summary>Runs the command <paramref name="args"/> name; a parse error is a usage error, reported on stderr only.</summary>
    internal static async Task<int> InvokeAsync(
        RootCommand root,
        string[] args,
        InvocationConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var parseResult = root.Parse(args);
        if (parseResult.Errors.Count == 0)
        {
            return await parseResult.InvokeAsync(configuration, cancellationToken);
        }

        // Not System.CommandLine's error action: it writes help to stdout, which carries only the Snapshot path.
        foreach (var error in parseResult.Errors)
        {
            await configuration.Error.WriteLineAsync(error.Message);
        }

        return ExitCodes.Usage;
    }
}
