using System.CommandLine;
using Typescape.Analyze;

namespace Typescape;

internal static class Program
{
    private static Task<int> Main(string[] args) =>
        CreateRootCommand(Environment.CurrentDirectory, (request, cancellationToken) => Analyzer.Analyzer.AnalyzeAsync(request, null, cancellationToken))
            .Parse(args)
            .InvokeAsync();

    internal static RootCommand CreateRootCommand(string currentDirectory, AnalyzeCommand.AnalyzeAsync analyze) =>
        new("Analyzes a C# solution and shows its structure.")
        {
            new AnalyzeCommand(currentDirectory, analyze),
        };
}
