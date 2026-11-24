using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Typescape.Analyzer.Pipeline;

namespace Typescape.Analyzer.Loading;

/// <summary>
/// Opens the solution with MSBuildWorkspace (out-of-proc BuildHost) under <see cref="GlobalProperties"/>, and keeps one
/// Roslyn project per C# project file: the one for the chosen target framework.
/// </summary>
/// <remarks>
/// The first open shows which .NET Framework reference assemblies <see cref="ReferenceAssemblies"/> lacks. When some of
/// them can be fetched, the solution is opened again and the counter starts over; this happens only while the cache
/// is missing a version. MSBuild finds the versions itself, so imports, conditions and Directory.Build.props all count.
/// </remarks>
internal static class SolutionLoader
{
    // Two project paths that differ only in case are one file on Windows and macOS.
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    /// <inheritdoc cref="LoadAsync(AnalysisRequest, ReferenceAssemblies, PhaseCounter, CancellationToken)"/>
    public static Task<LoadedSolution> LoadAsync(AnalysisRequest request, PhaseCounter counter, CancellationToken cancellationToken) =>
        LoadAsync(request, ReferenceAssemblies.Default, counter, cancellationToken);

    /// <param name="referenceAssemblies">The .NET Framework reference-assembly root the projects get, and fetch into.</param>
    /// <param name="counter">Counts the C# projects of the solution file as MSBuildWorkspace finishes each one.</param>
    /// <exception cref="InvalidOperationException">Not a <c>.sln</c> or <c>.slnx</c> file.</exception>
    public static async Task<LoadedSolution> LoadAsync(
        AnalysisRequest request,
        ReferenceAssemblies referenceAssemblies,
        PhaseCounter counter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(referenceAssemblies);
        ArgumentNullException.ThrowIfNull(counter);

        var solutionPath = Path.GetFullPath(request.SolutionPath);
        var directory = Path.GetDirectoryName(solutionPath)!;
        var listed = (await SolutionFile.ProjectPathsAsync(solutionPath, cancellationToken))
            .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Distinct(PathComparer)
            .ToArray();
        var properties = GlobalProperties.For(request, referenceAssemblies);

        counter.Start(listed.Length);
        MSBuildWorkspace? workspace = null;
        try
        {
            (workspace, var solution, var progress) = await OpenAsync(solutionPath, properties, listed, counter, cancellationToken);

            IReadOnlyDictionary<string, string> failed = new Dictionary<string, string>();
            var missing = referenceAssemblies.Missing(workspace.Diagnostics);
            if (missing.Count > 0)
            {
                failed = await referenceAssemblies.FetchAsync(missing, directory, request.MaxParallelism, cancellationToken);
                if (failed.Count < missing.Count)
                {
                    workspace.Dispose();
                    workspace = null;
                    counter.Restart();
                    (workspace, solution, progress) = await OpenAsync(solutionPath, properties, listed, counter, cancellationToken);
                }
            }

            // Projects outside the solution file that a listed one references are loaded too, and analyzed with it.
            var projects = solution.Projects
                .Where(project => project.Language == LanguageNames.CSharp && project.FilePath is not null)
                .GroupBy(project => project.FilePath!, PathComparer)
                .Select(file => Choose(file.Key, [.. file], Available(file.Key, [.. file], progress), request.Framework, directory))
                .OrderBy(project => project.Key, StringComparer.Ordinal)
                .ToArray();

            var opened = solution.Projects.Select(project => project.FilePath).OfType<string>().ToHashSet(PathComparer);
            var unopened = listed
                .Where(path => !opened.Contains(path))
                .Select(path => Key(directory, path))
                .Order(StringComparer.Ordinal)
                .ToArray();

            var diagnostics = workspace.Diagnostics.ToArray();
            var failures = failed
                .OrderBy(failure => failure.Key, StringComparer.Ordinal)
                .Select(failure => new ReferenceAssemblyFailure(failure.Key, failure.Value, Targeting(failure.Key, projects, diagnostics)))
                .ToArray();

            return new LoadedSolution(workspace, solution, directory, projects, unopened, diagnostics, failures);
        }
        catch
        {
            workspace?.Dispose();
            throw;
        }
    }

    private static async Task<(MSBuildWorkspace Workspace, Solution Solution, LoadProgress Progress)> OpenAsync(
        string solutionPath,
        IReadOnlyDictionary<string, string> properties,
        IReadOnlyList<string> listed,
        PhaseCounter counter,
        CancellationToken cancellationToken)
    {
        var workspace = MSBuildWorkspace.Create(new Dictionary<string, string>(properties));
        try
        {
            var progress = new LoadProgress(listed, counter);
            var solution = await workspace.OpenSolutionAsync(solutionPath, progress, cancellationToken);
            progress.Finish();
            return (workspace, solution, progress);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    // The projects MSBuild could not find the version for, by the file path in the diagnostic. A project analyzed for
    // another target framework of its file is not affected.
    private static string[] Targeting(string version, IReadOnlyList<LoadedProject> projects, IReadOnlyList<WorkspaceDiagnostic> diagnostics)
    {
        var reports = diagnostics.Where(diagnostic => ReferenceAssemblies.Reports(diagnostic, version)).ToArray();
        return
        [
            .. projects
                .Where(project => project.Tfm is null || TargetFrameworks.IsNetFramework(project.Tfm, version))
                .Where(project => reports.Any(report => report.Message.Contains(project.Project.FilePath!, StringComparison.OrdinalIgnoreCase)))
                .Select(project => project.Key),
        ];
    }

    private static LoadedProject Choose(
        string path,
        IReadOnlyList<Project> perTfm,
        IReadOnlyList<string> available,
        string? requested,
        string directory)
    {
        var candidates = perTfm.Select(project => (Tfm: TfmOf(project, available), Project: project)).ToArray();
        var chosen = TargetFrameworks.Choose([.. candidates.Select(candidate => candidate.Tfm).OfType<string>()], requested);
        var (tfm, project) = chosen is null
            ? candidates.MinBy(candidate => candidate.Project.Name, StringComparer.Ordinal)
            : candidates.First(candidate => candidate.Tfm == chosen);

        return new LoadedProject(Key(directory, path), project, tfm, available);
    }

    private static IReadOnlyList<string> Available(string path, IReadOnlyList<Project> perTfm, LoadProgress progress) =>
        progress.TargetFrameworks(path) is { Count: > 0 } reported ? reported : TargetFrameworks.OfClassic(perTfm);

    // Roslyn names the projects of a multi-targeting file "Name(tfm)"; a single-target project keeps its plain name.
    private static string? TfmOf(Project project, IReadOnlyList<string> available) =>
        available.FirstOrDefault(tfm => project.Name.EndsWith($"({tfm})", StringComparison.Ordinal))
        ?? (available.Count == 1 ? available[0] : null);

    private static string Key(string directory, string path) =>
        Path.GetRelativePath(directory, path).Replace('\\', '/');

    /// <summary>
    /// Collects the target frameworks MSBuildWorkspace resolves per project file, and counts each listed project when its
    /// first Resolve arrives: that is the end of its MSBuild work.
    /// </summary>
    private sealed class LoadProgress : IProgress<ProjectLoadProgress>
    {
        private readonly Lock _lock = new();
        private readonly HashSet<string> _pending;
        private readonly Dictionary<string, SortedSet<string>> _tfms = new(PathComparer);
        private readonly PhaseCounter _counter;

        public LoadProgress(IReadOnlyList<string> listed, PhaseCounter counter)
        {
            _pending = new HashSet<string>(listed, PathComparer);
            _counter = counter;
        }

        public void Report(ProjectLoadProgress value)
        {
            if (value.Operation != ProjectLoadOperation.Resolve)
            {
                return;
            }

            lock (_lock)
            {
                if (value.TargetFramework is { Length: > 0 } tfm)
                {
                    if (!_tfms.TryGetValue(value.FilePath, out var tfms))
                    {
                        _tfms[value.FilePath] = tfms = new SortedSet<string>(StringComparer.Ordinal);
                    }

                    tfms.Add(tfm);
                }

                if (_pending.Remove(value.FilePath))
                {
                    _counter.Increment();
                }
            }
        }

        /// <summary>Counts the listed projects that never reported a Resolve, e.g. a missing project file.</summary>
        public void Finish()
        {
            lock (_lock)
            {
                foreach (var _ in _pending)
                {
                    _counter.Increment();
                }

                _pending.Clear();
            }
        }

        public IReadOnlyList<string> TargetFrameworks(string path)
        {
            lock (_lock)
            {
                return _tfms.TryGetValue(path, out var tfms) ? [.. tfms] : [];
            }
        }
    }
}
