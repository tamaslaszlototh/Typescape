using Microsoft.CodeAnalysis;
using Typescape.Analyzer.MSBuild;

namespace Typescape.Analyzer.Loading;

/// <summary>
/// Gives each loaded project its direct project references, read from the project files by
/// <see cref="ProjectFileReader"/>, and checks them against the references MSBuildWorkspace reports.
/// </summary>
internal static class DirectReferences
{
    /// <param name="properties">The global properties Load evaluates the projects with, the solution's included.</param>
    /// <returns>The projects in the same order, each with its references and mismatch flag.</returns>
    /// <remarks>
    /// MSBuildWorkspace reports an SDK-style project's whole closure, which NuGet's assets file adds, but only a classic
    /// project's direct references. It keeps analyzer references and follows them, and drops
    /// <c>ReferenceOutputAssembly=false</c> ones. So a project is flagged only when the two disagree under every
    /// reading: a direct reference the workspace lacks, or a workspace reference outside the closure of the code and
    /// analyzer references.
    /// </remarks>
    public static LoadedProject[] Resolve(
        IReadOnlyList<LoadedProject> projects,
        Solution solution,
        IReadOnlyDictionary<string, string> properties,
        CancellationToken cancellationToken)
    {
        var loaded = projects.ToDictionary(project => project.Project.FilePath!, MSBuildPaths.Comparer);

        // Projects that are not loaded, e.g. an F# one, still pass references on.
        var read = new Dictionary<string, IReadOnlyList<ProjectFileReference>>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<ProjectFileReference> Read(string path, string? tfm)
        {
            var key = $"{path}|{tfm}";
            if (!read.TryGetValue(key, out var references))
            {
                cancellationToken.ThrowIfCancellationRequested();
                read[key] = references = File.Exists(path) ? ProjectFileReader.ProjectReferences(path, tfm, properties) : [];
            }

            return references;
        }

        // NuGet gives a reference the target framework nearest to the referencing project's.
        string? TfmOf(string path, string? referencingTfm) => loaded.GetValueOrDefault(path) is not { } project
            ? null
            : (referencingTfm is null ? null : TargetFrameworks.Nearest(project.AvailableTfms, referencingTfm)) ?? project.Tfm;

        HashSet<string> Closure(LoadedProject project)
        {
            var seen = new HashSet<string>(MSBuildPaths.Comparer);
            var pending = new Stack<(string Path, string? Tfm)>([(project.Project.FilePath!, project.Tfm)]);
            while (pending.TryPop(out var next))
            {
                foreach (var reference in Read(next.Path, next.Tfm).Where(reference => reference.Kind != ProjectReferenceKind.OutputNotReferenced))
                {
                    if (seen.Add(reference.Path))
                    {
                        pending.Push((reference.Path, TfmOf(reference.Path, next.Tfm)));
                    }
                }
            }

            return seen;
        }

        HashSet<string> Keys(IEnumerable<string> paths, LoadedProject self) =>
        [
            .. paths
                .Select(loaded.GetValueOrDefault)
                .OfType<LoadedProject>()
                .Where(project => project != self)
                .Select(project => project.Key),
        ];

        return
        [
            .. projects.Select(project =>
            {
                var direct = Keys(
                    Read(project.Project.FilePath!, project.Tfm).Where(reference => reference.Kind == ProjectReferenceKind.Code).Select(reference => reference.Path),
                    project);
                var closure = Keys(Closure(project), project);
                var workspace = Keys(
                    project.Project.ProjectReferences.Select(reference => solution.GetProject(reference.ProjectId)?.FilePath).OfType<string>(),
                    project);

                return project with
                {
                    References = [.. direct.Order(StringComparer.Ordinal)],
                    ProjectReferenceMismatch = !direct.IsSubsetOf(workspace) || !workspace.IsSubsetOf(closure),
                };
            }),
        ];
    }
}
