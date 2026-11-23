namespace Typescape.Snapshot.Records;

/// <summary>
/// A member with an executable body: the unit metrics are measured on. Each property, indexer and event accessor is its
/// own row. Lambdas, anonymous methods and local functions fold into their containing Executable Member.
/// </summary>
/// <param name="Key">Stable key: type key + documentation comment ID (<see cref="StableKeys.Member"/>).</param>
/// <param name="Type">Index into <see cref="SnapshotRecord.Types"/> of the containing type.</param>
/// <param name="Declaration">Index into the containing type's <see cref="TypeRecord.Declarations"/> that holds the member.</param>
/// <param name="Signature">The display signature.</param>
/// <param name="Kind">The kind of Executable Member.</param>
/// <param name="Accessibility">The declared accessibility.</param>
/// <param name="Document">Index into <see cref="SnapshotRecord.Documents"/>.</param>
/// <param name="Lines">The lines the member's declaration spans.</param>
/// <param name="Loc">Lines of code covered by the member's declaration.</param>
/// <param name="Cyclomatic">Cyclomatic complexity.</param>
/// <param name="Cognitive">Cognitive complexity.</param>
/// <param name="Generated">
/// Whether the member is generated: in a generated declaration, or carrying <c>[GeneratedCode]</c> or <c>[CompilerGenerated]</c>.
/// </param>
public sealed record ExecutableMemberRecord(
    string Key,
    int Type,
    int Declaration,
    string Signature,
    ExecutableMemberKind Kind,
    DeclaredAccessibility Accessibility,
    int Document,
    LineSpan Lines,
    int Loc,
    int Cyclomatic,
    int Cognitive,
    bool Generated);

public enum ExecutableMemberKind
{
    Method,
    Constructor,
    Destructor,
    Operator,
    Conversion,
    Accessor,
    EntryPoint,
}
