using Typescape.Analyzer.Loading;

namespace Typescape.Analyzer;

/// <summary>The MSBuild global properties that Restore and Load both evaluate the projects with.</summary>
internal static class GlobalProperties
{
    public const string Configuration = nameof(Configuration);

    public const string DefaultConfiguration = "Debug";

    public const string CustomAfterMicrosoftCommonTargets = nameof(CustomAfterMicrosoftCommonTargets);

    public const string TargetFrameworkRootPath = nameof(TargetFrameworkRootPath);

    public const string ResolveAssemblyReferencesSilent = nameof(ResolveAssemblyReferencesSilent);

    /// <summary>Ships next to the Analyzer dll; each tool version has its own copy.</summary>
    public static string TargetsPath { get; } = Path.Combine(AppContext.BaseDirectory, "Typescape.targets");

    /// <summary>
    /// The request's properties plus <see cref="DefaultConfiguration"/> unless the request sets a configuration, and
    /// three that always win: the hook that imports <see cref="TargetsPath"/>, the root of
    /// <paramref name="referenceAssemblies"/> (<see cref="ReferenceAssemblies.Default"/> when <c>null</c>), and loud
    /// reference resolution.
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

            // Design-time builds resolve references silently, so an unresolved one would vanish without a diagnostic.
            [ResolveAssemblyReferencesSilent] = "false",
        };

        // Set explicitly, so a project that defaults to another configuration cannot contradict Provenance.
        properties.TryAdd(Configuration, DefaultConfiguration);
        return properties;
    }

    /// <summary>
    /// <paramref name="properties"/> plus the <c>Solution*</c> properties MSBuild sets when it builds a solution, which
    /// MSBuildWorkspace sets too; a property already in <paramref name="properties"/> wins.
    /// </summary>
    public static IReadOnlyDictionary<string, string> WithSolution(IReadOnlyDictionary<string, string> properties, string solutionPath)
    {
        ArgumentNullException.ThrowIfNull(properties);

        var withSolution = new Dictionary<string, string>(properties, StringComparer.OrdinalIgnoreCase);
        withSolution.TryAdd("SolutionDir", Path.GetDirectoryName(solutionPath)! + Path.DirectorySeparatorChar);
        withSolution.TryAdd("SolutionPath", solutionPath);
        withSolution.TryAdd("SolutionName", Path.GetFileNameWithoutExtension(solutionPath));
        withSolution.TryAdd("SolutionFileName", Path.GetFileName(solutionPath));
        withSolution.TryAdd("SolutionExt", Path.GetExtension(solutionPath));
        return withSolution;
    }
}
