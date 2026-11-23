namespace Typescape.Analyzer;

/// <summary>The MSBuild global properties that Restore and Load both evaluate the projects with.</summary>
internal static class GlobalProperties
{
    public const string Configuration = nameof(Configuration);

    public const string DefaultConfiguration = "Debug";

    public const string CustomAfterMicrosoftCommonTargets = nameof(CustomAfterMicrosoftCommonTargets);

    /// <summary>Ships next to the Analyzer dll; each tool version has its own copy.</summary>
    public static string TargetsPath { get; } = Path.Combine(AppContext.BaseDirectory, "Typescape.targets");

    /// <summary>
    /// The request's properties plus the hook that imports <see cref="TargetsPath"/>, which always wins, and
    /// <see cref="DefaultConfiguration"/> unless the request sets a configuration.
    /// </summary>
    public static IReadOnlyDictionary<string, string> For(AnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // MSBuild property names ignore case.
        var properties = new Dictionary<string, string>(request.Properties, StringComparer.OrdinalIgnoreCase)
        {
            [CustomAfterMicrosoftCommonTargets] = TargetsPath,
        };

        // Set explicitly, so a project that defaults to another configuration cannot contradict Provenance.
        properties.TryAdd(Configuration, DefaultConfiguration);
        return properties;
    }
}
