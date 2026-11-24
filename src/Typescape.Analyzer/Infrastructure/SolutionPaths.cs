using System.Text.RegularExpressions;

namespace Typescape.Analyzer.Infrastructure;

/// <summary>Keeps absolute paths out of what a Snapshot stores.</summary>
internal static class SolutionPaths
{
    /// <summary>The stable key of a project or document file: its path relative to <paramref name="directory"/>, forward slashes.</summary>
    /// <param name="path">Absolute, or relative to <paramref name="directory"/> (never to the working directory).</param>
    /// <param name="directory">The absolute solution directory.</param>
    /// <remarks>A path on another Windows drive has no relative form and stays absolute.</remarks>
    public static string Key(string path, string directory) =>
        Path.GetRelativePath(directory, Path.Combine(directory, path)).Replace('\\', '/');

    /// <summary>
    /// Rewrites every path under <paramref name="directory"/> in <paramref name="text"/> to its form relative to it, by
    /// dropping the directory prefix. Case is ignored, as on Windows and macOS.
    /// </summary>
    /// <param name="directory">The absolute solution directory.</param>
    public static string Relativize(string text, string directory)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(directory);

        // MSBuild writes either separator on Windows. The lookbehind keeps a path that only contains the directory.
        var roots = new[] { trimmed, trimmed.Replace('\\', '/') }.Distinct(StringComparer.Ordinal).Select(Regex.Escape);
        var pattern = $@"(?<![\w.\-/\\])(?:{string.Join('|', roots)})[/\\]";
        return Regex.Replace(text, pattern, "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
