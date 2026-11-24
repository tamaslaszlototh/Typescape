using Typescape.Snapshot.Records;

namespace Typescape.Analyzer.Extraction;

/// <summary>
/// Facts extracted from a project, or from one part of it: its declarations or one of its documents. Key-based and
/// unsorted, with no array indices; Assemble merges, sorts and indexes them.
/// </summary>
internal sealed record ProjectFacts(IReadOnlyList<DocumentFact> Documents, IReadOnlyList<TypeFact> Types)
{
    /// <summary>The facts of all <paramref name="parts"/> together, in their order.</summary>
    public static ProjectFacts Combine(IEnumerable<ProjectFacts> parts)
    {
        var all = parts.ToArray();
        return new([.. all.SelectMany(part => part.Documents)], [.. all.SelectMany(part => part.Types)]);
    }
}

/// <param name="Path">Stable key: the path relative to the solution directory, forward slashes.</param>
internal sealed record DocumentFact(string Path);

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
internal sealed record TypeDeclarationFact(string Document, LineSpan Lines);
