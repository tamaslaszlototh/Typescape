using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Typescape.Analyzer.Infrastructure;
using Typescape.Analyzer.MSBuild;
using Typescape.Analyzer.Packages;
using Typescape.Snapshot.Records;
using Kind = Typescape.Snapshot.Records.WorkspaceDiagnosticKind;
using RoslynKind = Microsoft.CodeAnalysis.WorkspaceDiagnosticKind;

namespace Typescape.Analyzer.Loading;

/// <summary>
/// Gives each project its load evidence: its style, the SDK fallback flag and its workspace diagnostics, classified.
/// </summary>
/// <remarks>
/// A diagnostic belongs to a project when Roslyn reports it for the project's id, or else when its message names the
/// project file (the first one it names). Messages are matched only on paths and on text Roslyn does not translate.
/// <para>
/// One exception: the build of a project that .NET SDK MSBuild loads evaluates its project references again, under .NET
/// SDK MSBuild. When Visual Studio or Mono MSBuild loaded a classic reference, that second evaluation can fail on an
/// import only they have, e.g. the web application targets, and Roslyn reports it as a failure naming the reference. It
/// is told by a path in a .NET SDK's directory, and belongs to each referencing project .NET SDK MSBuild loaded, as
/// <see cref="Kind.Info"/> when the workspace kept the reference anyway, else <see cref="Kind.Warning"/>. With no such
/// project, it stays a failure of the reference.
/// </para>
/// </remarks>
internal static partial class LoadEvidence
{
    // Roslyn's BuildHostProcessManager writes it untranslated, after the project file path.
    private const string SdkFallbackText = "will be loaded with the .NET Core SDK";

    /// <param name="projects">The loaded projects.</param>
    /// <param name="unopenedPaths">The full paths of the solution's C# projects MSBuildWorkspace did not open.</param>
    /// <param name="solution">The loaded solution.</param>
    /// <param name="diagnostics">Every workspace diagnostic of the load.</param>
    /// <param name="failures">The .NET Framework versions whose reference assemblies could not be fetched.</param>
    /// <param name="directory">The absolute solution directory.</param>
    /// <returns>
    /// The projects in the same order, each with its evidence; the unopened projects sorted by key; the diagnostics
    /// that belong to no project.
    /// </returns>
    public static (LoadedProject[] Projects, UnopenedProject[] Unopened, WorkspaceDiagnosticRecord[] SolutionDiagnostics) Collect(
        IReadOnlyList<LoadedProject> projects,
        IReadOnlyList<string> unopenedPaths,
        Solution solution,
        IReadOnlyList<WorkspaceDiagnostic> diagnostics,
        IReadOnlyList<ReferenceAssemblyFailure> failures,
        string directory)
    {
        var keys = new Dictionary<string, string>(MSBuildPaths.Comparer);
        foreach (var project in projects)
        {
            keys[project.Project.FilePath!] = project.Key;
        }

        foreach (var path in unopenedPaths)
        {
            keys[path] = SolutionPaths.Key(path, directory);
        }

        var owned = diagnostics.ToLookup(diagnostic => Owner(diagnostic, solution, keys), StringComparer.Ordinal);
        LoadedProject[] styled =
        [
            .. projects.Select(project => project with
            {
                Style = ProjectStyles.Of(project.Project.FilePath!),
                SdkFallback = owned[project.Key].Any(IsSdkFallback),
            }),
        ];
        var (reevaluations, referrers) = SdkReevaluations(styled, solution, owned, directory);

        WorkspaceDiagnosticRecord[] Records(string? key)
        {
            ReferenceAssemblyFailure[] failed = key is null ? [] : failures.Where(failure => failure.ProjectKeys.Contains(key)).ToArray();

            // The fetch failure is the cause. MSB3644 tells the user to install a Developer Pack, which Typescape does
            // not need, so it goes; when it cannot be told apart, the failure stands next to it.
            return
            [
                .. owned[key]
                    .Where(diagnostic => !reevaluations.Contains(diagnostic))
                    .Where(diagnostic => !failed.Any(failure => ReferenceAssemblies.AdvisesDeveloperPack(diagnostic, failure.Version)))
                    .Select(diagnostic => Classify(diagnostic, solution, directory))
                    .Concat(key is null ? [] : referrers[key])
                    .Concat(failed.Select(failure => new WorkspaceDiagnosticRecord(Kind.Failure, SolutionPaths.Relativize(failure.ProjectMessage, directory))))
                    .Distinct()
                    .OrderBy(record => record.Kind)
                    .ThenBy(record => record.Message, StringComparer.Ordinal),
            ];
        }

        return (
            [.. styled.Select(project => project with { WorkspaceDiagnostics = Records(project.Key) })],
            [
                .. unopenedPaths
                    .Select(path => new UnopenedProject(keys[path], ProjectStyles.Of(path), Records(keys[path])))
                    .OrderBy(project => project.Key, StringComparer.Ordinal),
            ],
            Records(null));
    }

    /// <summary>The failures of .NET SDK MSBuild evaluating a classic reference again, see the remarks.</summary>
    /// <returns>The diagnostics that move, and the records they become, by the key of the referencing project.</returns>
    private static (HashSet<WorkspaceDiagnostic> Moved, ILookup<string, WorkspaceDiagnosticRecord> Referrers) SdkReevaluations(
        IReadOnlyList<LoadedProject> projects,
        Solution solution,
        ILookup<string?, WorkspaceDiagnostic> owned,
        string directory)
    {
        var moved = new HashSet<WorkspaceDiagnostic>(ReferenceEqualityComparer.Instance);
        var records = new List<(string Key, WorkspaceDiagnosticRecord Record)>();
        var sdkLoaded = projects.Where(project => project.Style == ProjectStyle.Sdk || project.SdkFallback).ToArray();
        foreach (var reference in projects.Where(project => project.Style == ProjectStyle.Legacy && !project.SdkFallback))
        {
            var reevaluations = owned[reference.Key]
                .Where(diagnostic => diagnostic is not ProjectDiagnostic
                    && diagnostic.Kind == RoslynKind.Failure
                    && SdkDirectory().IsMatch(diagnostic.Message))
                .ToArray();
            if (reevaluations.Length == 0)
            {
                continue;
            }

            var referencing = sdkLoaded
                .Select(project => (project.Key, Kept: Keeps(project, reference, solution), Direct: project.References.Contains(reference.Key)))
                .Where(project => project.Kept || project.Direct)
                .ToArray();
            if (referencing.Length == 0)
            {
                continue;
            }

            moved.UnionWith(reevaluations);
            records.AddRange(
                from project in referencing
                from diagnostic in reevaluations
                select (project.Key, new WorkspaceDiagnosticRecord(project.Kept ? Kind.Info : Kind.Warning, SolutionPaths.Relativize(diagnostic.Message, directory))));
        }

        return (moved, records.ToLookup(record => record.Key, record => record.Record, StringComparer.Ordinal));
    }

    private static bool Keeps(LoadedProject project, LoadedProject reference, Solution solution) =>
        project.Project.ProjectReferences.Any(projectReference =>
            solution.GetProject(projectReference.ProjectId)?.FilePath is { } path
            && MSBuildPaths.Comparer.Equals(path, reference.Project.FilePath));

    // E.g. dotnet/sdk/10.0.100/: MSBuildExtensionsPath and MSBuildBinPath of .NET SDK MSBuild.
    [GeneratedRegex(@"[\\/]sdk[\\/]\d+\.\d+\.\d+[^\\/]*[\\/]", RegexOptions.IgnoreCase)]
    private static partial Regex SdkDirectory();

    private static string? Owner(WorkspaceDiagnostic diagnostic, Solution solution, IReadOnlyDictionary<string, string> keys)
    {
        if (diagnostic is ProjectDiagnostic { ProjectId: var id } && solution.GetProject(id)?.FilePath is { } file)
        {
            return keys.GetValueOrDefault(file);
        }

        return keys
            .Select(entry => (Key: entry.Value, Index: MSBuildPaths.IndexIn(diagnostic.Message, entry.Key), entry.Key.Length))
            .Where(match => match.Index >= 0)
            .OrderBy(match => match.Index)
            .ThenByDescending(match => match.Length)
            .Select(match => match.Key)
            .FirstOrDefault();
    }

    private static WorkspaceDiagnosticRecord Classify(WorkspaceDiagnostic diagnostic, Solution solution, string directory)
    {
        var kind = diagnostic.Kind switch
        {
            RoslynKind.Failure => Kind.Failure,
            _ when IsKeptProjectReference(diagnostic, solution) => Kind.Info,
            _ => Kind.Warning,
        };
        return new WorkspaceDiagnosticRecord(kind, SolutionPaths.Relativize(diagnostic.Message, directory));
    }

    /// <summary>
    /// "Found project reference without a matching metadata reference: &lt;referenced project&gt;", when the workspace
    /// kept the project reference anyway. That is how it reports an analyzer reference, and every reference of a
    /// classic project; the compilation sees the referenced project either way.
    /// </summary>
    private static bool IsKeptProjectReference(WorkspaceDiagnostic diagnostic, Solution solution) =>
        diagnostic is ProjectDiagnostic { ProjectId: var id }
        && solution.GetProject(id) is { } project
        && project.ProjectReferences.Any(reference =>
            solution.GetProject(reference.ProjectId)?.FilePath is { } referenced
            && MSBuildPaths.EndsWith(diagnostic.Message, referenced));

    private static bool IsSdkFallback(WorkspaceDiagnostic diagnostic) =>
        diagnostic.Kind == RoslynKind.Warning && diagnostic.Message.Contains(SdkFallbackText, StringComparison.Ordinal);
}
