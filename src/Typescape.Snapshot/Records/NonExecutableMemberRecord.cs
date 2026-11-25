namespace Typescape.Snapshot.Records;

/// <summary>
/// A member of an own type without an executable body (section 14, D5) that is the target of at least one Occurrence.
/// It exists only so such a target resolves to its documentation comment ID and a display name; it carries no metrics.
/// </summary>
/// <param name="Key">Stable key: type key + documentation comment ID (<see cref="StableKeys.Member"/>).</param>
/// <param name="Type">Index into <see cref="SnapshotRecord.Types"/> of the containing type.</param>
/// <param name="Name">The display name, with the containing type, e.g. <c>Color.Red</c>.</param>
public sealed record NonExecutableMemberRecord(string Key, int Type, string Name);
