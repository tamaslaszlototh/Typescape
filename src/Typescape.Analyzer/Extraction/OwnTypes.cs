using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Typescape.Snapshot;
using Typescape.Snapshot.Records;

namespace Typescape.Analyzer.Extraction;

/// <summary>The project's own types: every named type its source declares, nested ones included.</summary>
internal static class OwnTypes
{
    public static ProjectFacts Extract(Compilation compilation, ProjectContext context, CancellationToken cancellationToken)
    {
        var scope = ProjectScope.Create(compilation, context, cancellationToken);
        return ProjectFacts.Combine(Declared(compilation.Assembly.GlobalNamespace).Select(type => Guarded(type, scope)));
    }

    // An exception on one type is pinned to the document of its first declaration, and the other types survive it.
    private static ProjectFacts Guarded(INamedTypeSymbol type, ProjectScope scope)
    {
        ProjectFacts Walk()
        {
            scope.CancellationToken.ThrowIfCancellationRequested();
            return Fact(type, scope) is { } fact
                ? ProjectFacts.Empty with { Types = [fact], ExecutableMembers = [.. ExecutableMembers.Of(type, fact.Key, scope)] }
                : ProjectFacts.Empty;
        }

        return type.DeclaringSyntaxReferences.FirstOrDefault() is { } first
            ? AnalyzerErrors.Catch(Source.Document(first.SyntaxTree, scope.Context), Walk, scope.CancellationToken)
            : Walk();
    }

    private static TypeFact? Fact(INamedTypeSymbol type, ProjectScope scope)
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
            Symbols.Accessibility(type),
            Abstract: isClass && type.IsAbstract,
            Sealed: isClass && type.IsSealed,
            Static: isClass && type.IsStatic,
            type.Arity,
            NamespaceName(type.ContainingNamespace),
            type.ContainingType is { } containing ? Key(containing) : null,
            [.. type.DeclaringSyntaxReferences.Select(reference => Declaration(type, reference, scope))]);
    }

    private static IEnumerable<INamedTypeSymbol> Declared(INamespaceSymbol container) =>
        container.GetNamespaceMembers().SelectMany(Declared).Concat(container.GetTypeMembers().SelectMany(WithNested));

    private static IEnumerable<INamedTypeSymbol> WithNested(INamedTypeSymbol type) =>
        type.GetTypeMembers().SelectMany(WithNested).Prepend(type);

    private static string Key(INamedTypeSymbol type) =>
        StableKeys.Type(type.ContainingAssembly.Name, Symbols.DocumentationCommentId(type));

    private static DeclaredTypeKind? Kind(INamedTypeSymbol type) => type.TypeKind switch
    {
        TypeKind.Class => type.IsRecord ? DeclaredTypeKind.Record : DeclaredTypeKind.Class,
        TypeKind.Struct => type.IsRecord ? DeclaredTypeKind.RecordStruct : DeclaredTypeKind.Struct,
        TypeKind.Interface => DeclaredTypeKind.Interface,
        TypeKind.Enum => DeclaredTypeKind.Enum,
        TypeKind.Delegate => DeclaredTypeKind.Delegate,
        _ => null,
    };

    // Built from the names rather than ToDisplayString, which escapes keywords such as @class.
    private static string NamespaceName(INamespaceSymbol ns) =>
        ns.IsGlobalNamespace ? ""
        : ns.ContainingNamespace.IsGlobalNamespace ? ns.Name
        : $"{NamespaceName(ns.ContainingNamespace)}.{ns.Name}";

    private static TypeDeclarationFact Declaration(INamedTypeSymbol type, SyntaxReference reference, ProjectScope scope)
    {
        var span = reference.GetSyntax(scope.CancellationToken) is CompilationUnitSyntax unit ? Source.TopLevelStatements(unit) : reference.Span;
        return new TypeDeclarationFact(
            Source.Document(reference.SyntaxTree, scope.Context),
            Source.Lines(reference.SyntaxTree, span, scope.CancellationToken),
            scope.Generated.DeclarationIsGenerated(type, reference));
    }
}
