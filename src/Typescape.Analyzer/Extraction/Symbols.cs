using Microsoft.CodeAnalysis;
using Typescape.Snapshot;
using Typescape.Snapshot.Records;

namespace Typescape.Analyzer.Extraction;

/// <summary>Symbol facts every extractor maps the same way.</summary>
internal static class Symbols
{
    public static string DocumentationCommentId(ISymbol symbol) =>
        symbol.GetDocumentationCommentId() ?? throw new InvalidOperationException($"{symbol} has no documentation comment ID.");

    /// <summary>The stable key of a type declared in the solution, as <see cref="StableKeys.Type"/> builds it.</summary>
    public static string TypeKey(INamedTypeSymbol type, ProjectContext context) =>
        StableKeys.Type(type.ContainingAssembly.Name, DocumentationCommentId(type), FileLocalDocument(type, context));

    // All parts of a file-local type sit in its one document, so the first part names it.
    private static string? FileLocalDocument(INamedTypeSymbol type, ProjectContext context)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.IsFileLocal)
            {
                return Source.Document(current.DeclaringSyntaxReferences[0].SyntaxTree, context);
            }
        }

        return null;
    }

    public static DeclaredAccessibility Accessibility(ISymbol symbol) => symbol.DeclaredAccessibility switch
    {
        Microsoft.CodeAnalysis.Accessibility.Public => DeclaredAccessibility.Public,
        Microsoft.CodeAnalysis.Accessibility.Internal => DeclaredAccessibility.Internal,
        Microsoft.CodeAnalysis.Accessibility.Protected => DeclaredAccessibility.Protected,
        Microsoft.CodeAnalysis.Accessibility.ProtectedOrInternal => DeclaredAccessibility.ProtectedInternal,
        Microsoft.CodeAnalysis.Accessibility.ProtectedAndInternal => DeclaredAccessibility.PrivateProtected,
        Microsoft.CodeAnalysis.Accessibility.Private => DeclaredAccessibility.Private,
        var other => throw new InvalidOperationException($"{symbol} has no declared accessibility ({other})."),
    };
}
