using Microsoft.CodeAnalysis;
using Typescape.Snapshot.Records;

namespace Typescape.Analyzer.Extraction;

/// <summary>Symbol facts every extractor maps the same way.</summary>
internal static class Symbols
{
    public static string DocumentationCommentId(ISymbol symbol) =>
        symbol.GetDocumentationCommentId() ?? throw new InvalidOperationException($"{symbol} has no documentation comment ID.");

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
