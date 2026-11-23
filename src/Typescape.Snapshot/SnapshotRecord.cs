using System.Text.Json.Serialization;
using Typescape.Snapshot.Records;

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
/// <param name="ExternalMembers">
/// Members of External Types that are the target of an Occurrence, sorted by key. Empty when <paramref name="Occurrences"/> is <c>null</c>.
/// </param>
/// <param name="NonExecutableMembers">
/// Own members without an executable body that are the target of an Occurrence, sorted by key. Empty when
/// <paramref name="Occurrences"/> is <c>null</c>.
/// </param>
/// <param name="Dependencies">Type-level Dependencies, one per (source type, target type, Dependency Kind).</param>
/// <param name="LoadHealth">How completely each project was loaded, plus the workspace diagnostics of no project.</param>
/// <param name="Provenance">
/// How the Snapshot was produced. The only part that may differ between two analyses of the same code: compare two
/// Snapshots without it.
/// </param>
/// <param name="Occurrences">
/// The detachable Occurrence section, sorted by Dependency, document, line, source member, then target member.
/// <c>null</c> when the Snapshot was written without Occurrences (<c>--no-occurrences</c>): every view and filter
/// works from <paramref name="Dependencies"/> alone, only the drill-down into Occurrences is missing.
/// </param>
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
    IReadOnlyList<NonExecutableMemberRecord> NonExecutableMembers,
    IReadOnlyList<DependencyRecord> Dependencies,
    LoadHealthRecord LoadHealth,
    ProvenanceRecord Provenance,
    IReadOnlyList<OccurrenceRecord>? Occurrences)
{
    /// <summary>
    /// The <see cref="SchemaVersion"/> this assembly writes. A minor bump only adds things; a major bump breaks readers.
    /// </summary>
    public const string CurrentSchemaVersion = "1.0";

    /// <summary>
    /// The documentation comment ID and display name of an Occurrence's target member, whichever table holds it
    /// (see <see cref="OccurrenceRecord.TargetMember"/>); <c>null</c> when the Occurrence has no target member.
    /// </summary>
    public MemberReference? ResolveTargetMember(OccurrenceRecord occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);

        if (occurrence.TargetMember is not { } target)
        {
            return null;
        }

        if (Dependencies[occurrence.Dependency].External)
        {
            var external = ExternalMembers[target];
            return new MemberReference(external.Key, external.Name);
        }

        if (target >= 0)
        {
            var executable = ExecutableMembers[target];
            return new MemberReference(
                StableKeys.MemberDocumentationCommentId(executable.Key, Types[executable.Type].Key),
                executable.Signature);
        }

        var member = NonExecutableMembers[~target];
        return new MemberReference(
            StableKeys.MemberDocumentationCommentId(member.Key, Types[member.Type].Key),
            member.Name);
    }
}

/// <summary>A member as an Occurrence shows it.</summary>
/// <param name="DocumentationCommentId">Roslyn's documentation comment ID, e.g. <c>M:N.C.Run(System.Int32)</c>.</param>
/// <param name="Name">The display name.</param>
public readonly record struct MemberReference(string DocumentationCommentId, string Name);
