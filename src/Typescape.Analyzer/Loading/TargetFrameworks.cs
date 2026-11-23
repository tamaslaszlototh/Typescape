using NuGet.Frameworks;

namespace Typescape.Analyzer.Loading;

/// <summary>Picks the one target framework a project is analyzed for.</summary>
internal static class TargetFrameworks
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

    /// <summary>Whether <paramref name="tfm"/>, e.g. <c>net472</c>, is .NET Framework <paramref name="version"/>, e.g. <c>v4.7.2</c>.</summary>
    public static bool IsNetFramework(string tfm, string version) =>
        Version.TryParse(version.TrimStart('v'), out var parsed)
        && Parse(tfm).Equals(new NuGetFramework(FrameworkConstants.FrameworkIdentifiers.Net, parsed));

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
