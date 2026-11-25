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

    /// <summary>
    /// The type <paramref name="member"/> belongs to: an extension block is no type, so its members belong to the static
    /// class around it.
    /// </summary>
    public static INamedTypeSymbol? Owner(ISymbol member) =>
        member.ContainingType is { IsExtension: true } block ? block.ContainingType : member.ContainingType;

    /// <summary>The kind of a type a reader would call a type; <c>null</c> for others, such as a C# 14 extension block.</summary>
    public static DeclaredTypeKind? TypeKind(INamedTypeSymbol type) => type.TypeKind switch
    {
        Microsoft.CodeAnalysis.TypeKind.Class => type.IsRecord ? DeclaredTypeKind.Record : DeclaredTypeKind.Class,
        Microsoft.CodeAnalysis.TypeKind.Struct => type.IsRecord ? DeclaredTypeKind.RecordStruct : DeclaredTypeKind.Struct,
        Microsoft.CodeAnalysis.TypeKind.Interface => DeclaredTypeKind.Interface,
        Microsoft.CodeAnalysis.TypeKind.Enum => DeclaredTypeKind.Enum,
        Microsoft.CodeAnalysis.TypeKind.Delegate => DeclaredTypeKind.Delegate,
        _ => null,
    };

    /// <summary>The full name of the type's namespace; empty for the global namespace.</summary>
    public static string Namespace(INamedTypeSymbol type) => NamespaceName(type.ContainingNamespace);

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

    // Built from the names rather than ToDisplayString, which escapes keywords such as @class.
    private static string NamespaceName(INamespaceSymbol ns) =>
        ns.IsGlobalNamespace ? ""
        : ns.ContainingNamespace.IsGlobalNamespace ? ns.Name
        : $"{NamespaceName(ns.ContainingNamespace)}.{ns.Name}";
}
