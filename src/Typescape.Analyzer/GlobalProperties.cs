using Typescape.Analyzer.Loading;

namespace Typescape.Analyzer;

/// <summary>The MSBuild global properties that Restore and Load both evaluate the projects with.</summary>
internal static class GlobalProperties
{
    public const string Configuration = nameof(Configuration);

    public const string DefaultConfiguration = "Debug";

    public const string CustomAfterMicrosoftCommonTargets = nameof(CustomAfterMicrosoftCommonTargets);

    public const string TargetFrameworkRootPath = nameof(TargetFrameworkRootPath);

    /// <summary>Ships next to the Analyzer dll; each tool version has its own copy.</summary>
    public static string TargetsPath { get; } = Path.Combine(AppContext.BaseDirectory, "Typescape.targets");

    /// <summary>
    /// The request's properties plus <see cref="DefaultConfiguration"/> unless the request sets a configuration, and
    /// two that always win: the hook that imports <see cref="TargetsPath"/>, and the root of
    /// <paramref name="referenceAssemblies"/> (<see cref="ReferenceAssemblies.Default"/> when <c>null</c>).
    /// </summary>
    public static IReadOnlyDictionary<string, string> For(AnalysisRequest request, ReferenceAssemblies? referenceAssemblies = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        // MSBuild property names ignore case.
        var properties = new Dictionary<string, string>(request.Properties, StringComparer.OrdinalIgnoreCase)
        {
            [CustomAfterMicrosoftCommonTargets] = TargetsPath,

            // Even over an installed Developer Pack, so every machine binds to the same reference assemblies.
            [TargetFrameworkRootPath] = (referenceAssemblies ?? ReferenceAssemblies.Default).Root,
        };

        // Set explicitly, so a project that defaults to another configuration cannot contradict Provenance.
        properties.TryAdd(Configuration, DefaultConfiguration);
        return properties;
    }
}
