using System.CommandLine;
using Typescape.Analyze;

namespace Typescape;

internal static class Program
{
    private static Task<int> Main(string[] args) =>
        InvokeAsync(
            CreateRootCommand(Environment.CurrentDirectory, !Console.IsErrorRedirected, Analyzer.Analyzer.AnalyzeAsync),
            args,
            new InvocationConfiguration(),
            CancellationToken.None);

    internal static RootCommand CreateRootCommand(string currentDirectory, bool interactive, AnalyzeCommand.AnalyzeAsync analyze) =>
        new("Analyzes a C# solution and shows its structure.")
        {
            new AnalyzeCommand(currentDirectory, interactive, analyze),
        };

    /// <summary>Parses <paramref name="args"/> and runs the command; a parse error exits 2 with the errors on stderr only.</summary>
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
