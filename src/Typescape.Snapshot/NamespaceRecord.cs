namespace Typescape.Snapshot;

/// <summary>
/// A namespace within one project. A namespace split across projects is one row per project.
/// Its stable key is the pair (project key, <paramref name="Name"/>).
/// </summary>
/// <param name="Project">Index into <see cref="SnapshotRecord.Projects"/>.</param>
/// <param name="Name">The full namespace name; empty for the global namespace.</param>
public sealed record NamespaceRecord(int Project, string Name);
