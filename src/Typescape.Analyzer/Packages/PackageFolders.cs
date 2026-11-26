using NuGet.Configuration;
using Typescape.Analyzer.Infrastructure;

namespace Typescape.Analyzer.Packages;

/// <summary>The folders NuGet extracts packages into for a solution: the global packages folder, then the fallback folders.</summary>
internal static class PackageFolders
{
    /// <summary>
    /// Each folder as the solution's NuGet settings give it and, where that differs, its canonical spelling, since
    /// MSBuild may report a document under either.
    /// </summary>
    /// <returns>Empty when the NuGet settings cannot be read.</returns>
    public static IReadOnlyList<string> For(string solutionDirectory)
    {
        try
        {
            var settings = Settings.LoadDefaultSettings(solutionDirectory);
            return
            [
                .. new[] { SettingsUtility.GetGlobalPackagesFolder(settings) }
                    .Concat(SettingsUtility.GetFallbackPackageFolders(settings))
                    .SelectMany(folder => new[] { Path.GetFullPath(folder), CanonicalPath.Of(folder) })
                    .Distinct(StringComparer.Ordinal),
            ];
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return [];
        }
    }
}
