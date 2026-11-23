using Microsoft.CodeAnalysis;

namespace Typescape.Analyzer.Pipeline;

/// <summary>The output of Load: the Roslyn solution and the projects to analyze. Disposing it disposes the workspace.</summary>
/// <param name="workspace">The workspace that owns <paramref name="solution"/>.</param>
/// <param name="solution">The loaded solution.</param>
/// <param name="directory">The absolute solution directory.</param>
/// <param name="projects">The projects to analyze, each once.</param>
internal sealed class LoadedSolution(
    Workspace workspace,
    Solution solution,
    string directory,
    IReadOnlyList<LoadedProject> projects) : IDisposable
{
    public Solution Solution { get; } = solution;

    public string Directory { get; } = directory;

    public IReadOnlyList<LoadedProject> Projects { get; } = projects;

    public void Dispose() => workspace.Dispose();
}

/// <summary>A project Load opened.</summary>
/// <param name="Key">The project's stable key: its file path relative to the solution directory, forward slashes.</param>
/// <param name="Project">The Roslyn project.</param>
internal sealed record LoadedProject(string Key, Project Project);
