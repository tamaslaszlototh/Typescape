using System.Text.RegularExpressions;

namespace Typescape.Analyzer;

/// <summary>Keeps absolute paths out of the messages a Snapshot stores.</summary>
internal static class SolutionPaths
{
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
