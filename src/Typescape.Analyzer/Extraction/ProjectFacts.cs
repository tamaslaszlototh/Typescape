using Typescape.Snapshot.Records;

namespace Typescape.Analyzer.Extraction;

/// <summary>
/// Facts extracted from a project, or from one part of it: its declarations or one of its documents. Key-based and
/// unsorted, with no array indices; Assemble merges, sorts and indexes them.
/// </summary>
internal sealed record ProjectFacts(
    IReadOnlyList<DocumentFact> Documents,
    IReadOnlyList<TypeFact> Types,
    IReadOnlyList<ExecutableMemberFact> ExecutableMembers,
    IReadOnlyList<AnalyzerErrorFact> AnalyzerErrors)
{
    public static ProjectFacts Empty { get; } = new([], [], [], []);

    /// <summary>The facts of all <paramref name="parts"/> together, in their order.</summary>
    public static ProjectFacts Combine(IEnumerable<ProjectFacts> parts)
    {
        var all = parts.ToArray();
        return new(
            [.. all.SelectMany(part => part.Documents)],
            [.. all.SelectMany(part => part.Types)],
            [.. all.SelectMany(part => part.ExecutableMembers)],
            [.. all.SelectMany(part => part.AnalyzerErrors)]);
    }
}

/// <param name="Path">Stable key: the path relative to the solution directory, forward slashes.</param>
/// <param name="GeneratedReason">Why the whole document is generated code; <c>null</c> when it is not.</param>
internal sealed record DocumentFact(string Path, GeneratedReason? GeneratedReason);

/// <summary>One of the project's own types, as its open definition.</summary>
/// <param name="Key">Stable key: assembly name + documentation comment ID.</param>
/// <param name="Name">The name without namespace, containing type or type parameters.</param>
/// <param name="Abstract">Declared <c>abstract</c>: only classes and records; an interface is not.</param>
/// <param name="Sealed">Declared <c>sealed</c>: only classes and records; a struct, enum or delegate is not.</param>
/// <param name="Static">Declared <c>static</c>: only classes.</param>
/// <param name="Namespace">The full namespace name; empty for the global namespace.</param>
/// <param name="ContainingType">The enclosing type's key, for a nested type.</param>
/// <param name="Declarations">One per partial part, in Roslyn's order.</param>
internal sealed record TypeFact(
    string Key,
    string Name,
    DeclaredTypeKind Kind,
    DeclaredAccessibility Accessibility,
    bool Abstract,
    bool Sealed,
    bool Static,
    int Arity,
    string Namespace,
    string? ContainingType,
    IReadOnlyList<TypeDeclarationFact> Declarations);

/// <param name="Document">The document's key.</param>
/// <param name="Lines">The physical lines the declaration spans, <c>#line</c> directives ignored.</param>
/// <param name="Generated">
/// In a generated document, carrying <c>[GeneratedCode]</c> or <c>[CompilerGenerated]</c>, or nested in a generated
/// declaration of the containing type.
/// </param>
internal sealed record TypeDeclarationFact(string Document, LineSpan Lines, bool Generated);

/// <summary>
/// A member with an executable body; each property, indexer and event accessor is its own. Its declaration is the one
/// of its type's declarations in <see cref="Document"/> whose lines contain <see cref="Lines"/>: the parts of a partial
/// type never overlap, so there is exactly one.
/// </summary>
/// <param name="Key">Stable key: type key + documentation comment ID.</param>
/// <param name="Type">The containing type's key.</param>
/// <param name="Signature">The display signature: name and parameter types, no containing type or return type.</param>
/// <param name="Document">The key of the document holding the body.</param>
/// <param name="Lines">The physical lines the member spans, from its attributes to its end; for an entry point, the top-level statements.</param>
/// <param name="Generated">
/// In a generated declaration, or carrying <c>[GeneratedCode]</c> or <c>[CompilerGenerated]</c>; an accessor also when
/// its property, indexer or event carries one.
/// </param>
internal sealed record ExecutableMemberFact(
    string Key,
    string Type,
    string Signature,
    ExecutableMemberKind Kind,
    DeclaredAccessibility Accessibility,
    string Document,
    LineSpan Lines,
    bool Generated);

/// <summary>An exception in Typescape's own walker, caught on one document.</summary>
/// <param name="Document">The document's key.</param>
/// <param name="ExceptionType">The exception's full type name, e.g. <c>System.InvalidCastException</c>.</param>
/// <param name="Frame">The top stack frame as a method with its parameter types, never its file and line.</param>
internal sealed record AnalyzerErrorFact(string Document, string ExceptionType, string Frame);
