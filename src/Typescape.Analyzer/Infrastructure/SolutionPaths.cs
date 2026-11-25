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
    /// dropping the directory prefix, with forward slashes as in <see cref="Key"/>. Case is ignored, as on Windows and
    /// macOS.
    /// </summary>
    /// <param name="directory">The absolute solution directory.</param>
    /// <remarks>
    /// A quoted path ends at its closing quote, an unquoted one at the first whitespace, so an unquoted path with a
    /// space keeps the separators after it.
    /// </remarks>
    public static string Relativize(string text, string directory)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(directory);

        // MSBuild writes either separator on Windows. The lookbehind keeps a path that only contains the directory.
        var roots = string.Join('|', new[] { trimmed, trimmed.Replace('\\', '/') }.Distinct(StringComparer.Ordinal).Select(Regex.Escape));
        var pattern = $@"(?<![\w.\-/\\])(?:(?<quote>['""])(?:{roots})[/\\](?<rest>[^'""\r\n]*)|(?:{roots})[/\\](?<rest>[^\s'""]*))";
        return Regex.Replace(
            text,
            pattern,
            match => match.Groups["quote"].Value + match.Groups["rest"].Value.Replace('\\', '/'),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
