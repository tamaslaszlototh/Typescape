using Microsoft.CodeAnalysis;

namespace Typescape.Analyzer.Pipeline;

/// <summary>The output of Load: the Roslyn solution and the projects to analyze. Disposing it disposes the workspace.</summary>
/// <param name="workspace">The workspace that owns <paramref name="solution"/>.</param>
/// <param name="solution">The loaded solution.</param>
/// <param name="directory">The absolute solution directory.</param>
/// <param name="projects">The projects to analyze, each once, sorted by key.</param>
/// <param name="unopenedProjects">The keys of the solution's projects that MSBuildWorkspace opened no project for, sorted.</param>
/// <param name="diagnostics">Every diagnostic MSBuildWorkspace reported while opening the solution.</param>
internal sealed class LoadedSolution(
    Workspace workspace,
    Solution solution,
    string directory,
    IReadOnlyList<LoadedProject> projects,
    IReadOnlyList<string> unopenedProjects,
    IReadOnlyList<WorkspaceDiagnostic> diagnostics) : IDisposable
{
    public Solution Solution { get; } = solution;

    public string Directory { get; } = directory;

    public IReadOnlyList<LoadedProject> Projects { get; } = projects;

    /// <summary>
    /// The keys of the C# projects the solution file lists but MSBuildWorkspace did not open, e.g. a missing project
    /// file. They are failed projects; <see cref="Diagnostics"/> says why.
    /// </summary>
    public IReadOnlyList<string> UnopenedProjects { get; } = unopenedProjects;

    public IReadOnlyList<WorkspaceDiagnostic> Diagnostics { get; } = diagnostics;

    public void Dispose() => workspace.Dispose();
}

/// <summary>A project Load opened.</summary>
/// <param name="Key">The project's stable key: its file path relative to the solution directory, forward slashes.</param>
/// <param name="Project">The Roslyn project, the one for <paramref name="Tfm"/>.</param>
/// <param name="Tfm">The target framework the project is analyzed for, or <c>null</c> when MSBuild reported none.</param>
/// <param name="AvailableTfms">Every target framework MSBuild reported for the project file, sorted ordinally.</param>
internal sealed record LoadedProject(string Key, Project Project, string? Tfm, IReadOnlyList<string> AvailableTfms);
