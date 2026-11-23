using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Typescape.Analyzer.Pipeline;

namespace Typescape.Analyzer.Loading;

/// <summary>
/// Opens the solution with MSBuildWorkspace (out-of-proc BuildHost) under <see cref="GlobalProperties"/>, and keeps one
/// Roslyn project per C# project file: the one for the chosen target framework.
/// </summary>
internal static class SolutionLoader
{
    // Two project paths that differ only in case are one file on Windows and macOS.
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    /// <param name="counter">Counts the C# projects of the solution file as MSBuildWorkspace finishes each one.</param>
    /// <exception cref="InvalidOperationException">Not a <c>.sln</c> or <c>.slnx</c> file.</exception>
    public static async Task<LoadedSolution> LoadAsync(
        AnalysisRequest request,
        PhaseCounter counter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(counter);

        var solutionPath = Path.GetFullPath(request.SolutionPath);
        var directory = Path.GetDirectoryName(solutionPath)!;
        var listed = (await SolutionFile.ProjectPathsAsync(solutionPath, cancellationToken))
            .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Distinct(PathComparer)
            .ToArray();

        var workspace = MSBuildWorkspace.Create(new Dictionary<string, string>(GlobalProperties.For(request)));
        try
        {
            var progress = new LoadProgress(listed, counter);
            var solution = await workspace.OpenSolutionAsync(solutionPath, progress, cancellationToken);
            progress.Finish();

            // Projects outside the solution file that a listed one references are loaded too, and analyzed with it.
            var projects = solution.Projects
                .Where(project => project.Language == LanguageNames.CSharp && project.FilePath is not null)
                .GroupBy(project => project.FilePath!, PathComparer)
                .Select(file => Choose(file.Key, [.. file], progress.TargetFrameworks(file.Key), request.Framework, directory))
                .OrderBy(project => project.Key, StringComparer.Ordinal)
                .ToArray();

            var opened = solution.Projects.Select(project => project.FilePath).OfType<string>().ToHashSet(PathComparer);
            var unopened = listed
                .Where(path => !opened.Contains(path))
                .Select(path => Key(directory, path))
                .Order(StringComparer.Ordinal)
                .ToArray();

            return new LoadedSolution(workspace, solution, directory, projects, unopened, [.. workspace.Diagnostics]);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
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
            _counter.Start(_pending.Count);
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
