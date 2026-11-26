using System.Diagnostics.CodeAnalysis;

namespace Typescape.Analyze;

/// <summary>Finds the solution file <c>typescape analyze</c> analyzes.</summary>
internal static class SolutionInput
{
    private static readonly string[] SolutionExtensions = [".sln", ".slnx"];

    /// <summary>
    /// Resolves <paramref name="input"/>: a <c>.sln</c>, a <c>.slnx</c>, or a directory that holds exactly one of them.
    /// </summary>
    /// <param name="input">The path as given, relative to <paramref name="currentDirectory"/>; <c>null</c> for that directory.</param>
    /// <param name="solution">The absolute path of the solution file.</param>
    /// <param name="error">Why there is no solution to analyze, naming what was found.</param>
    public static bool TryResolve(
        string? input,
        string currentDirectory,
        [NotNullWhen(true)] out string? solution,
        [NotNullWhen(false)] out string? error)
    {
        solution = null;
        var path = Path.GetFullPath(input ?? ".", currentDirectory);

        if (File.Exists(path))
        {
            error = FileError(path);
            solution = error is null ? path : null;
            return error is null;
        }

        if (!Directory.Exists(path))
        {
            error = $"'{input ?? path}' does not exist.";
            return false;
        }

        var found = Directory.EnumerateFiles(path)
            .Where(IsSolution)
            .Order(StringComparer.Ordinal)
            .ToList();

        switch (found)
        {
            case [var only]:
                solution = only;
                error = null;
                return true;
            case []:
                error = $"No solution file (.sln or .slnx) in '{path}'.";
                return false;
            default:
                error = string.Join(
                    Environment.NewLine,
                    [$"Several solution files in '{path}'; pass the one to analyze:", .. found.Select(file => $"  {Path.GetFileName(file)}")]);
                return false;
        }
    }

    private static string? FileError(string path)
    {
        if (IsSolution(path))
        {
            return null;
        }

        var name = Path.GetFileName(path);
        return Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            ? $"'{name}' is a project file; pass its solution (.sln or .slnx) or the solution's directory."
            : $"'{name}' is not a solution file (.sln or .slnx).";
    }

    private static bool IsSolution(string path) =>
        SolutionExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
