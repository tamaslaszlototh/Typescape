using System.IO.Enumeration;
using Microsoft.CodeAnalysis;

namespace Typescape.Analyzer.Loading;

/// <summary>
/// Decides which projects are test projects: by a known test framework among the metadata references, then by the
/// project name; <c>--test</c> / <c>--not-test</c> overrides win over both.
/// </summary>
/// <remarks>
/// The metadata references are what MSBuild resolved, so conditions, imports and central package versions all count. A
/// package that was not restored gives no reference, and the name is all that is left.
/// </remarks>
internal static class TestProjects
{
    // In order: the first match names the reason, so the SDK, which every framework's projects carry, comes last. The
    // SDK has no assembly of its own: testhost comes with it on .NET, the coverage shim on every target framework.
    private static readonly (string Name, string[] Assemblies)[] Frameworks =
    [
        ("xunit", ["xunit.core", "xunit.v3.core"]),
        ("nunit", ["nunit.framework"]),
        ("mstest", ["Microsoft.VisualStudio.TestPlatform.TestFramework", "Microsoft.VisualStudio.QualityTools.UnitTestFramework"]),
        ("tunit", ["TUnit.Core"]),
        ("mspec", ["Machine.Specifications"]),
        ("microsoft.net.test.sdk", ["testhost", "Microsoft.VisualStudio.CodeCoverage.Shim"]),
    ];

    private static readonly string[] NameSuffixes = [".Tests", ".Test", ".Specs"];

    private const string OverrideReason = "override";
    private const string NameReason = "name";
    private const string FrameworkReasonPrefix = "framework:";

    /// <returns>The projects in the same order, each with its test flag and reason.</returns>
    public static LoadedProject[] Detect(
        IReadOnlyList<LoadedProject> projects,
        IReadOnlyList<string> testPatterns,
        IReadOnlyList<string> notTestPatterns) =>
    [
        .. projects.Select(project =>
        {
            var (test, reason) = Classify(
                Path.GetFileNameWithoutExtension(project.Project.FilePath!),
                project.Project.MetadataReferences.OfType<PortableExecutableReference>().Select(reference => reference.FilePath).OfType<string>(),
                testPatterns,
                notTestPatterns);
            return project with { Test = test, TestReason = reason };
        }),
    ];

    /// <param name="projectName">The project file's name without its extension.</param>
    /// <param name="referencePaths">The file paths of the project's metadata references.</param>
    /// <param name="testPatterns">As <see cref="AnalysisRequest.TestPatterns"/>.</param>
    /// <param name="notTestPatterns">As <see cref="AnalysisRequest.NotTestPatterns"/>.</param>
    /// <returns>The values of <see cref="LoadedProject.Test"/> and <see cref="LoadedProject.TestReason"/>.</returns>
    public static (bool Test, string? Reason) Classify(
        string projectName,
        IEnumerable<string> referencePaths,
        IReadOnlyList<string> testPatterns,
        IReadOnlyList<string> notTestPatterns)
    {
        ArgumentNullException.ThrowIfNull(projectName);
        ArgumentNullException.ThrowIfNull(referencePaths);
        ArgumentNullException.ThrowIfNull(testPatterns);
        ArgumentNullException.ThrowIfNull(notTestPatterns);

        if (Matches(notTestPatterns, projectName))
        {
            return (false, OverrideReason);
        }

        if (Matches(testPatterns, projectName))
        {
            return (true, OverrideReason);
        }

        var assemblies = referencePaths.Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, frameworkAssemblies) in Frameworks)
        {
            if (frameworkAssemblies.Any(assemblies.Contains))
            {
                return (true, FrameworkReasonPrefix + name);
            }
        }

        return NameSuffixes.Any(suffix => projectName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            ? (true, NameReason)
            : (false, null);
    }

    private static bool Matches(IReadOnlyList<string> patterns, string projectName) =>
        patterns.Any(pattern => FileSystemName.MatchesSimpleExpression(pattern, projectName, ignoreCase: true));
}
