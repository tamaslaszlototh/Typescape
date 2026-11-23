namespace Typescape.Snapshot;

/// <summary>
/// A type-level Dependency: a directed, typed link from one of the solution's own types to another type, own or External.
/// One row per (source type, target type, Dependency Kind). Its stable key is (source key, target key, Kind).
/// A type's use of itself creates no Dependency.
/// </summary>
/// <param name="Source">Index into <see cref="SnapshotRecord.Types"/>.</param>
/// <param name="Target">
/// Index into <see cref="SnapshotRecord.ExternalTypes"/> when <paramref name="External"/> is true, otherwise into
/// <see cref="SnapshotRecord.Types"/>.
/// </param>
/// <param name="External">Whether the target is an External Type.</param>
/// <param name="Kind">How the source uses the target.</param>
/// <param name="Count">The number of Occurrences.</param>
/// <param name="GeneratedCount">How many of the Occurrences are in generated code.</param>
public sealed record DependencyRecord(
    int Source,
    int Target,
    bool External,
    DependencyKind Kind,
    int Count,
    int GeneratedCount);

public enum DependencyKind
{
    Inherits,
    Implements,
    MemberType,
    Signature,
    New,
    Call,
    Attribute,
    GenericArg,
    MemberAccess,
    TypeUsage,
}
