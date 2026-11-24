using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using NuGet.Frameworks;

namespace Typescape.Analyzer.Loading;

/// <summary>Picks the one target framework a project is analyzed for.</summary>
internal static partial class TargetFrameworks
{
    // Across families: modern .NET, then .NET Standard, then .NET Framework, then anything else. Within a family the
    // version decides, then the platform, then the text, so every machine makes the same choice.
    private static readonly Comparer<string> ByHeight = Comparer<string>.Create((x, y) =>
    {
        var (a, b) = (Parse(x), Parse(y));
        var order = Family(a).CompareTo(Family(b));
        order = order != 0 ? order : a.Version.CompareTo(b.Version);
        order = order != 0 ? order : string.Compare(a.Platform, b.Platform, StringComparison.OrdinalIgnoreCase);
        order = order != 0 ? order : a.PlatformVersion.CompareTo(b.PlatformVersion);
        return order != 0 ? order : string.CompareOrdinal(x, y);
    });

    /// <summary>
    /// <paramref name="requested"/> (<c>--framework</c>) when the project targets it, otherwise the highest of
    /// <paramref name="available"/>.
    /// </summary>
    /// <returns>The chosen TFM as the project spells it, or <c>null</c> when <paramref name="available"/> is empty.</returns>
    public static string? Choose(IReadOnlyCollection<string> available, string? requested)
    {
        ArgumentNullException.ThrowIfNull(available);

        if (requested is not null && available.FirstOrDefault(tfm => Same(tfm, requested)) is { } match)
        {
            return match;
        }

        return available.Count == 0 ? null : available.Max(ByHeight);
    }

    /// <summary>
    /// Whether <paramref name="tfm"/>, e.g. <c>net472</c>, is .NET Framework <paramref name="version"/>, e.g. <c>v4.7.2</c>.
    /// A profile does not count: <c>net40-client</c> is <c>v4.0</c>, as MSB3644 names it.
    /// </summary>
    public static bool IsNetFramework(string tfm, string version) =>
        Version.TryParse(version.TrimStart('v'), out var parsed)
        && Parse(tfm) is var framework
        && framework.Framework == FrameworkConstants.FrameworkIdentifiers.Net
        && framework.Version == new NuGetFramework(FrameworkConstants.FrameworkIdentifiers.Net, parsed).Version;

    /// <summary>
    /// The target framework of each classic project, which declares only <c>TargetFrameworkVersion</c> and so reports no
    /// TFM: read from the attributes file MSBuild generates for it, e.g. <c>.NETFramework,Version=v4.8.AssemblyAttributes.cs</c>
    /// → <c>net48</c>.
    /// </summary>
    /// <returns>
    /// Each TFM once, sorted ordinally; empty when no project generated the file, and several only while a stale file
    /// cannot be told from the current one.
    /// </returns>
    public static IReadOnlyList<string> OfClassic(IEnumerable<Project> projects) =>
        [.. projects.SelectMany(OfClassic).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    private static string[] OfClassic(Project project)
    {
        var candidates = project.Documents
            .Select(document => document.FilePath)
            .OfType<string>()
            .Select(FromAttributesFile)
            .OfType<NuGetFramework>()
            .Distinct()
            .ToArray();
        if (candidates.Length <= 1)
        {
            return [.. candidates.Select(framework => framework.GetShortFolderName())];
        }

        // A file left from before a retarget, picked up by a wildcard: the resolved framework references tell the current
        // one. Until they resolve, the project counts as targeting each, so the current version is still fetched.
        var referenced = project.MetadataReferences
            .OfType<PortableExecutableReference>()
            .Select(reference => FrameworkDirectory().Match(reference.FilePath ?? ""))
            .Where(match => match.Success)
            .Select(match => new NuGetFramework(FrameworkConstants.FrameworkIdentifiers.Net, Version.Parse(match.Groups["version"].Value)).Version)
            .ToHashSet();
        var matching = candidates.Where(framework => referenced.Contains(framework.Version)).ToArray();
        return [.. (matching.Length == 1 ? matching : candidates).Select(framework => framework.GetShortFolderName())];
    }

    private const string AssemblyAttributesSuffix = ".AssemblyAttributes.cs";

    private static NuGetFramework? FromAttributesFile(string path)
    {
        var name = Path.GetFileName(path);
        if (!name.EndsWith(AssemblyAttributesSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var framework = NuGetFramework.ParseFrameworkName(name[..^AssemblyAttributesSuffix.Length], DefaultFrameworkNameProvider.Instance);
            return framework.IsUnsupported ? null : framework;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"[\\/]\.NETFramework[\\/]v(?<version>\d+(?:\.\d+){1,2})[\\/]")]
    private static partial Regex FrameworkDirectory();

    private static bool Same(string tfm, string requested)
    {
        var (a, b) = (Parse(tfm), Parse(requested));
        return a.IsUnsupported || b.IsUnsupported
            ? string.Equals(tfm, requested, StringComparison.OrdinalIgnoreCase)
            : a.Equals(b);
    }

    private static int Family(NuGetFramework framework) => framework.Framework switch
    {
        FrameworkConstants.FrameworkIdentifiers.NetCoreApp => 3,
        FrameworkConstants.FrameworkIdentifiers.NetStandard => 2,
        FrameworkConstants.FrameworkIdentifiers.Net => 1,
        _ => 0,
    };

    private static NuGetFramework Parse(string tfm)
    {
        try
        {
            return NuGetFramework.Parse(tfm);
        }
        catch (ArgumentException)
        {
            return NuGetFramework.UnsupportedFramework;
        }
    }
}
