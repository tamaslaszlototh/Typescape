using System.Text.Json.Serialization;

namespace Typescape.Snapshot;

/// <summary>
/// The root of a Snapshot: the complete, self-contained result of analyzing one solution.
/// </summary>
/// <param name="SchemaVersion">
/// The format version, <c>major.minor</c>. Always the first property of the root object, because
/// <c>typescape view</c> reads it from the start of the stream without parsing the rest.
/// </param>
/// <param name="RulesVersion">
/// Bumped whenever the same code would produce a different Snapshot (metric definitions, Dependency extraction,
/// generated and test heuristics).
/// </param>
/// <param name="Projects">The solution's projects, sorted by key.</param>
/// <param name="Namespaces">One row per (project, namespace name), sorted by project key, then name.</param>
/// <param name="Documents">The source documents, sorted by path.</param>
/// <param name="Types">The solution's own types, sorted by key.</param>
/// <param name="ExternalTypes">Types referenced by the solution but defined outside it, sorted by key.</param>
/// <param name="ExecutableMembers">The members with an executable body, sorted by key.</param>
/// <param name="ExternalMembers">Members of External Types that the solution uses, sorted by key.</param>
/// <param name="Dependencies">Type-level Dependencies, one per (source type, target type, Dependency Kind).</param>
public sealed record SnapshotRecord(
    [property: JsonPropertyOrder(int.MinValue)] string SchemaVersion,
    int RulesVersion,
    IReadOnlyList<ProjectRecord> Projects,
    IReadOnlyList<NamespaceRecord> Namespaces,
    IReadOnlyList<DocumentRecord> Documents,
    IReadOnlyList<TypeRecord> Types,
    IReadOnlyList<ExternalTypeRecord> ExternalTypes,
    IReadOnlyList<ExecutableMemberRecord> ExecutableMembers,
    IReadOnlyList<ExternalMemberRecord> ExternalMembers,
    IReadOnlyList<DependencyRecord> Dependencies)
{
    /// <summary>
    /// The <see cref="SchemaVersion"/> this assembly writes. A minor bump only adds things; a major bump breaks readers.
    /// </summary>
    public const string CurrentSchemaVersion = "1.0";
}
