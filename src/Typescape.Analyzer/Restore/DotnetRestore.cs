using System.ComponentModel;
using Typescape.Analyzer.Infrastructure;
using Typescape.Analyzer.MSBuild;

namespace Typescape.Analyzer.Restore;

/// <summary>Runs <c>dotnet restore</c> on the solution. A failure becomes a warning, never an exception.</summary>
internal static class DotnetRestore
{
    private const int MaxErrorLines = 10;

    /// <returns>The warning to show, or <c>null</c> when the restore succeeded.</returns>
    /// <exception cref="OperationCanceledException">The run was cancelled; the restore process is killed.</exception>
    public static async Task<string?> RunAsync(AnalysisRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // NuGet resolves each ProjectReference against the MSBuild node's working directory, which the OS reports
        // canonically. A solution reached through a symlink or in another case would then name each referenced project
        // twice, and NuGet restores both spellings in parallel into the same obj folder ("file already exists").
        var solution = CanonicalPath.Of(request.SolutionPath);
        List<string> arguments = ["restore", solution, "-tl:off", "-nologo", "-v:q"];
        arguments.AddRange(GlobalProperties.For(request).Select(property => $"-p:{property.Key}={property.Value}"));

        ProcessResult result;
        try
        {
            // The solution directory's global.json picks the SDK, as it does for the BuildHost.
            result = await ProcessRunner.RunAsync(
                "dotnet",
                arguments,
                Path.GetDirectoryName(solution)!,
                ProcessRunner.QuietEnvironment,
                cancellationToken);
        }
        catch (Win32Exception e)
        {
            return Warning($"dotnet could not be started: {e.Message}", []);
        }

        return result.ExitCode == 0 ? null : Warning($"dotnet restore exited with code {result.ExitCode}.", ErrorLines(result));
    }

    private static string Warning(string reason, IReadOnlyList<string> details) =>
        string.Join(
            Environment.NewLine,
            [$"Restore failed; the run continues and Load Health shows the projects it damaged. {reason}", .. details]);

    // MSBuild logs errors to stdout and repeats them in its summary.
    private static string[] ErrorLines(ProcessResult result)
    {
        var lines = $"{result.Output}\n{result.Error}"
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
        var errors = lines.Where(line => line.Contains(": error ", StringComparison.Ordinal)).Distinct().ToArray();
        var shown = errors.Length > 0 ? errors : lines;

        return shown.Length <= MaxErrorLines
            ? [.. shown.Select(line => $"  {line}")]
            : [.. shown.Take(MaxErrorLines).Select(line => $"  {line}"), $"  … and {shown.Length - MaxErrorLines} more."];
    }
}
