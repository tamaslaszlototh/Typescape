using System.Text.RegularExpressions;

namespace Typescape.Analyzer.Infrastructure;

/// <summary>Keeps absolute paths out of what a Snapshot stores.</summary>
internal static class SolutionPaths
{
    /// <summary>The root that stands for a NuGet package folder in a key, e.g. <c>{nuget}/xunit.v3.core/4.0.3/_content/A.cs</c>.</summary>
    public const string PackagesRoot = "{nuget}";

    /// <summary>The stable key of a project or document file: its path relative to <paramref name="directory"/>, forward slashes.</summary>
    /// <param name="path">Absolute, or relative to <paramref name="directory"/> (never to the working directory).</param>
    /// <param name="directory">The absolute solution directory.</param>
    /// <remarks>A path on another Windows drive has no relative form and stays absolute.</remarks>
    public static string Key(string path, string directory) => Key(path, directory, []);

    /// <summary>
    /// As <see cref="Key(string, string)"/>, except that a file outside <paramref name="directory"/> but in one of
    /// <paramref name="packageFolders"/> is keyed by its path in that folder under <see cref="PackagesRoot"/>, so a file
    /// compiled from a package gets the same key on every machine.
    /// </summary>
    /// <param name="packageFolders">Absolute NuGet package folders, in the order they are tried.</param>
    public static string Key(string path, string directory, IReadOnlyList<string> packageFolders)
    {
        var full = Path.Combine(directory, path);
        var relative = Relative(directory, full);
        if (!Outside(relative))
        {
            return relative;
        }

        foreach (var folder in packageFolders)
        {
            var inFolder = Relative(folder, full);
            if (!Outside(inFolder))
            {
                return $"{PackagesRoot}/{inFolder}";
            }
        }

        return relative;
    }

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

    private static string Relative(string directory, string path) =>
        Path.GetRelativePath(directory, path).Replace('\\', '/');

    // Another Windows drive has no relative form, so its path stays rooted.
    private static bool Outside(string relative) =>
        relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative);
}
