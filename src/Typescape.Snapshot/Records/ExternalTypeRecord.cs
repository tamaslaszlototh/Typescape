namespace Typescape.Snapshot.Records;

/// <summary>
/// A type referenced by the solution but defined outside it (framework or package), stored as its open definition.
/// Only types that are the target of at least one Dependency are listed; an unresolved reference creates none.
/// </summary>
/// <param name="Key">Stable key: assembly name + documentation comment ID (<see cref="StableKeys.Type"/>).</param>
/// <param name="Name">The display name, without namespace.</param>
/// <param name="Assembly">The defining assembly's name.</param>
/// <param name="Namespace">The full namespace name; empty for the global namespace.</param>
/// <param name="Kind">The kind of type.</param>
public sealed record ExternalTypeRecord(
    string Key,
    string Name,
    string Assembly,
    string Namespace,
    DeclaredTypeKind Kind);
