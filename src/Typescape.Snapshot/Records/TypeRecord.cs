namespace Typescape.Snapshot.Records;

/// <summary>
/// One of the solution's own types. A partial type is one row with several declarations; a nested type is its own row.
/// Generic types are stored as their open definition.
/// </summary>
/// <param name="Key">Stable key: assembly name + documentation comment ID (<see cref="StableKeys.Type"/>).</param>
/// <param name="Name">The type's name, without namespace or containing type.</param>
/// <param name="Kind">The kind of type.</param>
/// <param name="Accessibility">The declared accessibility.</param>
/// <param name="Abstract">Whether the type is abstract.</param>
/// <param name="Sealed">Whether the type is sealed.</param>
/// <param name="Static">Whether the type is static.</param>
/// <param name="Arity">The number of generic type parameters; 0 for a non-generic type.</param>
/// <param name="Namespace">Index into <see cref="SnapshotRecord.Namespaces"/>.</param>
/// <param name="ContainingType">Index into <see cref="SnapshotRecord.Types"/> of the enclosing type, for a nested type.</param>
/// <param name="Declarations">The type's declarations, one per partial part, sorted by document index, then start line.</param>
/// <param name="Loc">Lines of code over the type's own declarations, summed across them, nested type bodies excluded.</param>
public sealed record TypeRecord(
    string Key,
    string Name,
    DeclaredTypeKind Kind,
    DeclaredAccessibility Accessibility,
    bool Abstract,
    bool Sealed,
    bool Static,
    int Arity,
    int Namespace,
    int? ContainingType,
    IReadOnlyList<TypeDeclaration> Declarations,
    int Loc);

/// <summary>One declaration (partial part) of a type.</summary>
/// <param name="Document">Index into <see cref="SnapshotRecord.Documents"/>.</param>
/// <param name="Lines">The lines the declaration spans.</param>
/// <param name="Loc">Lines of code in this declaration, nested type bodies excluded.</param>
/// <param name="Generated">
/// Whether this declaration is generated: in a generated document, carrying <c>[GeneratedCode]</c> or
/// <c>[CompilerGenerated]</c>, or nested in a generated declaration of the containing type. Per declaration, because a
/// partial type can be half generated.
/// </param>
public sealed record TypeDeclaration(int Document, LineSpan Lines, int Loc, bool Generated);

/// <summary>A range of physical source lines, 1-based and inclusive, as in the original file (not <c>#line</c>-mapped).</summary>
/// <param name="Start">The first line.</param>
/// <param name="End">The last line.</param>
public sealed record LineSpan(int Start, int End);

public enum DeclaredTypeKind
{
    Class,
    Interface,
    Struct,
    Record,
    RecordStruct,
    Enum,
    Delegate,
}

public enum DeclaredAccessibility
{
    Public,
    Internal,
    Protected,
    ProtectedInternal,
    PrivateProtected,
    Private,
}
