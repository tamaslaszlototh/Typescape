using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Typescape.Analyzer.Infrastructure;
using Typescape.Snapshot;
using Typescape.Snapshot.Records;

namespace Typescape.Analyzer.Extraction;

/// <summary>The project's own types: every named type its source declares, nested ones included.</summary>
internal static class OwnTypes
{
    public static ProjectFacts Extract(Compilation compilation, ProjectContext context, CancellationToken cancellationToken) =>
        ProjectFacts.Combine(Declared(compilation.Assembly.GlobalNamespace).Select(type => Guarded(type, context, cancellationToken)));

    // An exception on one type is pinned to the document of its first declaration, and the other types survive it.
    private static ProjectFacts Guarded(INamedTypeSymbol type, ProjectContext context, CancellationToken cancellationToken)
    {
        ProjectFacts Walk()
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Fact(type, context, cancellationToken) is { } fact ? new([], [fact], []) : new([], [], []);
        }

        return type.DeclaringSyntaxReferences.FirstOrDefault() is { } first
            ? AnalyzerErrors.Catch(SolutionPaths.Key(first.SyntaxTree.FilePath, context.SolutionDirectory), Walk, cancellationToken)
            : Walk();
    }

    private static TypeFact? Fact(INamedTypeSymbol type, ProjectContext context, CancellationToken cancellationToken)
    {
        // C# 14 extension blocks are named types too, but not ones a reader would call a type.
        if (Kind(type) is not { } kind)
        {
            return null;
        }

        var isClass = type.TypeKind == TypeKind.Class;
        return new TypeFact(
            Key(type),
            type.Name,
            kind,
            Accessibility(type),
            Abstract: isClass && type.IsAbstract,
            Sealed: isClass && type.IsSealed,
            Static: isClass && type.IsStatic,
            type.Arity,
            NamespaceName(type.ContainingNamespace),
            type.ContainingType is { } containing ? Key(containing) : null,
            [.. type.DeclaringSyntaxReferences.Select(reference => Declaration(reference, context, cancellationToken))]);
    }

    private static IEnumerable<INamedTypeSymbol> Declared(INamespaceSymbol container) =>
        container.GetNamespaceMembers().SelectMany(Declared).Concat(container.GetTypeMembers().SelectMany(WithNested));

    private static IEnumerable<INamedTypeSymbol> WithNested(INamedTypeSymbol type) =>
        type.GetTypeMembers().SelectMany(WithNested).Prepend(type);

    private static string Key(INamedTypeSymbol type) =>
        StableKeys.Type(
            type.ContainingAssembly.Name,
            type.GetDocumentationCommentId() ?? throw new InvalidOperationException($"{type} has no documentation comment ID."));

    private static DeclaredTypeKind? Kind(INamedTypeSymbol type) => type.TypeKind switch
    {
        TypeKind.Class => type.IsRecord ? DeclaredTypeKind.Record : DeclaredTypeKind.Class,
        TypeKind.Struct => type.IsRecord ? DeclaredTypeKind.RecordStruct : DeclaredTypeKind.Struct,
        TypeKind.Interface => DeclaredTypeKind.Interface,
        TypeKind.Enum => DeclaredTypeKind.Enum,
        TypeKind.Delegate => DeclaredTypeKind.Delegate,
        _ => null,
    };

    private static DeclaredAccessibility Accessibility(INamedTypeSymbol type) => type.DeclaredAccessibility switch
    {
        Microsoft.CodeAnalysis.Accessibility.Public => DeclaredAccessibility.Public,
        Microsoft.CodeAnalysis.Accessibility.Internal => DeclaredAccessibility.Internal,
        Microsoft.CodeAnalysis.Accessibility.Protected => DeclaredAccessibility.Protected,
        Microsoft.CodeAnalysis.Accessibility.ProtectedOrInternal => DeclaredAccessibility.ProtectedInternal,
        Microsoft.CodeAnalysis.Accessibility.ProtectedAndInternal => DeclaredAccessibility.PrivateProtected,
        Microsoft.CodeAnalysis.Accessibility.Private => DeclaredAccessibility.Private,
        var other => throw new InvalidOperationException($"{type} has no declared accessibility ({other})."),
    };

    // Built from the names rather than ToDisplayString, which escapes keywords such as @class.
    private static string NamespaceName(INamespaceSymbol ns) =>
        ns.IsGlobalNamespace ? ""
        : ns.ContainingNamespace.IsGlobalNamespace ? ns.Name
        : $"{NamespaceName(ns.ContainingNamespace)}.{ns.Name}";

    private static TypeDeclarationFact Declaration(SyntaxReference reference, ProjectContext context, CancellationToken cancellationToken)
    {
        // Top-level statements declare the synthesized Program through their whole compilation unit, which would
        // also cover the usings and every type declared after them.
        var span = reference.GetSyntax(cancellationToken) is CompilationUnitSyntax unit ? TopLevelStatements(unit) : reference.Span;
        var lines = reference.SyntaxTree.GetLineSpan(span, cancellationToken);
        return new TypeDeclarationFact(
            SolutionPaths.Key(reference.SyntaxTree.FilePath, context.SolutionDirectory),
            new LineSpan(lines.StartLinePosition.Line + 1, lines.EndLinePosition.Line + 1));
    }

    private static TextSpan TopLevelStatements(CompilationUnitSyntax unit)
    {
        var statements = unit.Members.OfType<GlobalStatementSyntax>().ToArray();
        return TextSpan.FromBounds(statements[0].SpanStart, statements[^1].Span.End);
    }
}
