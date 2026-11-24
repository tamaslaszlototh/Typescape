using System.Text.RegularExpressions;

namespace Typescape.Analyzer.Loading;

/// <summary>Paths as project files write them, resolved on this machine.</summary>
internal static partial class MSBuildPaths
{
    // Two project paths that differ only in case are one file on Windows and macOS.
    public static StringComparer Comparer { get; } = StringComparer.OrdinalIgnoreCase;

    /// <summary>The full path of <paramref name="path"/> relative to <paramref name="directory"/>; <c>null</c> when it is not a valid path.</summary>
    public static string? Full(string path, string directory)
    {
        try
        {
            return Path.GetFullPath(Normalize(path), directory);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // Project files written on Windows use backslashes.
    public static string Normalize(string path) =>
        OperatingSystem.IsWindows() ? path : path.Replace('\\', '/');

    public static bool HasTrailingSlash(string path) => path.EndsWith('/') || path.EndsWith('\\');

    public static string EnsureTrailingSlash(string path) =>
        HasTrailingSlash(path) ? path : path + Path.DirectorySeparatorChar;

    public static bool HasWildcard(string path) => path.IndexOfAny(['*', '?']) >= 0;

    /// <summary>The nearest <paramref name="fileName"/> in <paramref name="directory"/> or above it, or <c>null</c>.</summary>
    public static string? FileAbove(string directory, string fileName)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
        {
            var path = Path.Combine(current.FullName, fileName);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// The full paths of an item's <c>Include</c>, <c>Exclude</c>, <c>Remove</c> or <c>Update</c> value, which MSBuild
    /// separates with semicolons, escapes as <c>%XX</c> and may write with wildcards.
    /// </summary>
    public static IEnumerable<string> Items(string value, string directory) =>
        value.Split(';')
            .Select(part => Unescape(part.Trim()))
            .Select(part => part.Length > 0 ? Full(part, directory) : null)
            .OfType<string>()
            .SelectMany(path => HasWildcard(path) ? Glob(path) : [path]);

    private static IEnumerable<string> Glob(string pattern)
    {
        var separator = Path.DirectorySeparatorChar;
        var segments = pattern.Split(separator);
        var fixedCount = Array.FindIndex(segments, HasWildcard);
        var root = string.Join(separator, segments[..fixedCount]);
        if (root.Length == 0 || !Directory.Exists(root))
        {
            return [];
        }

        var rest = string.Join('/', segments[fixedCount..]);
        var regex = new Regex(
            "^" + Regex.Escape(rest).Replace(@"\*\*/", "(?:.*/)?").Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]") + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = segments.Length - fixedCount > 1 || rest.Contains("**", StringComparison.Ordinal),
        };
        return Directory.EnumerateFiles(root, "*", options)
            .Where(file => regex.IsMatch(Path.GetRelativePath(root, file).Replace('\\', '/')))
            .Order(StringComparer.Ordinal);
    }

    private static string Unescape(string value) =>
        EscapedCharacter().Replace(value, match => ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString());

    [GeneratedRegex("%([0-9A-Fa-f]{2})")]
    private static partial Regex EscapedCharacter();
}
