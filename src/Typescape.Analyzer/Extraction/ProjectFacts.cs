using Typescape.Snapshot.Records;

namespace Typescape.Analyzer.Extraction;

/// <summary>
/// Facts extracted from a project, or from one part of it: its declarations or one of its documents. Key-based and
/// unsorted, with no array indices; Assemble merges, sorts and indexes them, and deduplicates what several parts share.
/// </summary>
/// <param name="Occurrences">Empty when the project is extracted without Occurrences (<see cref="ProjectContext.Occurrences"/>).</param>
/// <param name="ExternalTypes">Every External Type a Dependency targets, once per part.</param>
/// <param name="ExternalMembers">Every External member an Occurrence targets, once per part.</param>
/// <param name="NonExecutableMembers">Every own non-executable member an Occurrence targets, once per part.</param>
/// <param name="UnresolvedTypes">The type references that did not resolve, one row per name and part.</param>
internal sealed record ProjectFacts(
    IReadOnlyList<DocumentFact> Documents,
    IReadOnlyList<TypeFact> Types,
    IReadOnlyList<ExecutableMemberFact> ExecutableMembers,
    IReadOnlyList<DependencyFact> Dependencies,
    IReadOnlyList<OccurrenceFact> Occurrences,
    IReadOnlyList<ExternalTypeFact> ExternalTypes,
    IReadOnlyList<ExternalMemberFact> ExternalMembers,
    IReadOnlyList<NonExecutableMemberFact> NonExecutableMembers,
    IReadOnlyList<UnresolvedTypeFact> UnresolvedTypes,
    IReadOnlyList<AnalyzerErrorFact> AnalyzerErrors)
{
    public static ProjectFacts Empty { get; } = new([], [], [], [], [], [], [], [], [], []);

    /// <summary>The facts of all <paramref name="parts"/> together, in their order.</summary>
    public static ProjectFacts Combine(IEnumerable<ProjectFacts> parts)
    {
        var all = parts.ToArray();
        return new(
            [.. all.SelectMany(part => part.Documents)],
            [.. all.SelectMany(part => part.Types)],
            [.. all.SelectMany(part => part.ExecutableMembers)],
            [.. all.SelectMany(part => part.Dependencies)],
            [.. all.SelectMany(part => part.Occurrences)],
            [.. all.SelectMany(part => part.ExternalTypes)],
            [.. all.SelectMany(part => part.ExternalMembers)],
            [.. all.SelectMany(part => part.NonExecutableMembers)],
            [.. all.SelectMany(part => part.UnresolvedTypes)],
            [.. all.SelectMany(part => part.AnalyzerErrors)]);
    }
}

/// <param name="Path">Stable key: the path relative to the solution directory, forward slashes.</param>
/// <param name="GeneratedReason">Why the whole document is generated code; <c>null</c> when it is not.</param>
internal sealed record DocumentFact(string Path, GeneratedReason? GeneratedReason);

/// <summary>One of the project's own types, as its open definition.</summary>
/// <param name="Key">Stable key: assembly name + documentation comment ID, + document key for a file-local type and the types nested in it.</param>
/// <param name="Name">The name without namespace, containing type or type parameters.</param>
/// <param name="Abstract">Declared <c>abstract</c>: only classes and records; an interface is not.</param>
/// <param name="Sealed">Declared <c>sealed</c>: only classes and records; a struct, enum or delegate is not.</param>
/// <param name="Static">Declared <c>static</c>: only classes.</param>
/// <param name="Namespace">The full namespace name; empty for the global namespace.</param>
/// <param name="ContainingType">The enclosing type's key, for a nested type.</param>
/// <param name="Declarations">One per partial part, in Roslyn's order.</param>
/// <param name="Loc">The sum of the declarations' LOC: never the sum of the members' LOC.</param>
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
    IReadOnlyList<TypeDeclarationFact> Declarations,
    int Loc);

/// <param name="Document">The document's key.</param>
/// <param name="Lines">The physical lines the declaration spans, <c>#line</c> directives ignored.</param>
/// <param name="Loc">LOC (<see cref="LinesOfCode.OfType"/>): nested type declarations left out.</param>
/// <param name="Generated">
/// In a generated document, carrying <c>[GeneratedCode]</c> or <c>[CompilerGenerated]</c>, or nested in a generated
/// declaration of the containing type.
/// </param>
internal sealed record TypeDeclarationFact(string Document, LineSpan Lines, int Loc, bool Generated);

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
/// <param name="Loc">LOC (<see cref="LinesOfCode.OfMember"/>) over the same declaration as <paramref name="Lines"/>.</param>
/// <param name="Cyclomatic">Cyclomatic complexity (<see cref="Metrics.Cyclomatic"/>), lambdas and local functions included.</param>
/// <param name="Cognitive">Cognitive complexity (<see cref="Metrics.Cognitive"/>), lambdas and local functions included.</param>
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
    int Loc,
    int Cyclomatic,
    int Cognitive,
    bool Generated);

/// <summary>
/// A type-level Dependency as one document shows it. Assemble sums them per (source, target, Dependency Kind).
/// </summary>
/// <param name="Source">The key of the innermost own type around the places.</param>
/// <param name="Target">The key of the named type's open definition, own or External.</param>
/// <param name="Count">The number of places, which are the Dependency's Occurrences.</param>
/// <param name="GeneratedCount">How many of them are generated, as <see cref="GeneratedCode.CodeIsGenerated"/> decides.</param>
internal sealed record DependencyFact(string Source, string Target, DependencyKind Kind, int Count, int GeneratedCount);

/// <summary>One place in the source where a Dependency appears.</summary>
/// <param name="Source">The Dependency's source: the key of the innermost own type around the place.</param>
/// <param name="Target">The Dependency's target: the key of the named type's open definition, own or External.</param>
/// <param name="Kind">The Dependency's Kind.</param>
/// <param name="SourceMember">
/// The key of the Executable Member whose declaration holds the place, lambdas and local functions included;
/// <c>null</c> in an initializer or outside any Executable Member.
/// </param>
/// <param name="TargetMember">
/// The used member of <paramref name="Target"/>, <c>null</c> when there is none: for an own target the key of an
/// Executable Member or of a <see cref="NonExecutableMemberFact"/>, for an External one the key of the
/// <see cref="ExternalMemberFact"/> of <paramref name="Target"/>.
/// </param>
/// <param name="Document">The document's key.</param>
/// <param name="Line">The 1-based physical line the place starts on, <c>#line</c> directives ignored.</param>
internal sealed record OccurrenceFact(
    string Source,
    string Target,
    DependencyKind Kind,
    string? SourceMember,
    string? TargetMember,
    string Document,
    int Line);

/// <summary>A type a Dependency targets that the solution's source does not declare, as its open definition.</summary>
/// <param name="Key">Stable key: assembly name + documentation comment ID.</param>
/// <param name="Name">The display name: no namespace, but containing types and type parameters, e.g. <c>Dictionary&lt;TKey, TValue&gt;.KeyCollection</c>.</param>
/// <param name="Namespace">The full namespace name; empty for the global namespace.</param>
internal sealed record ExternalTypeFact(string Key, string Name, string Assembly, string Namespace, DeclaredTypeKind Kind);

/// <summary>A member of an External Type that an Occurrence targets, as its definition.</summary>
/// <param name="Key">
/// The documentation comment ID. Unique only together with <paramref name="Type"/>: polyfills declare the same type in
/// several assemblies.
/// </param>
/// <param name="Type">The containing External Type's key.</param>
/// <param name="Name">The display name, with the External Type's name, e.g. <c>String.Join&lt;T&gt;(string, IEnumerable&lt;T&gt;)</c>.</param>
internal sealed record ExternalMemberFact(string Key, string Type, string Name);

/// <summary>
/// A member of an own type that an Occurrence targets but that is no Executable Member: a field, constant, enum value,
/// event, property or indexer whose used accessor is no Executable Member, or a method or constructor without a body.
/// </summary>
/// <param name="Key">Stable key: type key + documentation comment ID.</param>
/// <param name="Type">The containing type's key.</param>
/// <param name="Name">The display name, with the containing type's name, e.g. <c>Color.Red</c>.</param>
internal sealed record NonExecutableMemberFact(string Key, string Type, string Name);

/// <summary>A type name that did not resolve, and how often the code refers to it.</summary>
/// <param name="Name">The name as written, without namespace or type arguments, e.g. <c>JsonConvert</c>.</param>
internal sealed record UnresolvedTypeFact(string Name, int Count);

/// <summary>An exception in Typescape's own walker, caught on one document.</summary>
/// <param name="Document">The document's key.</param>
/// <param name="ExceptionType">The exception's full type name, e.g. <c>System.InvalidCastException</c>.</param>
/// <param name="Frame">The top stack frame as a method with its parameter types, never its file and line.</param>
internal sealed record AnalyzerErrorFact(string Document, string ExceptionType, string Frame);
