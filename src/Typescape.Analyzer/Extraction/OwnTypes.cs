using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Typescape.Snapshot.Records;

namespace Typescape.Analyzer.Extraction;

/// <summary>The project's own types: every named type its source declares, nested ones included.</summary>
internal static class OwnTypes
{
    public static ProjectFacts Extract(Compilation compilation, ProjectContext context, CancellationToken cancellationToken)
    {
        var scope = ProjectScope.Create(compilation, context, cancellationToken);
        var models = new Dictionary<SyntaxTree, SemanticModel>();
        SemanticModel Model(SyntaxTree tree) =>
            models.TryGetValue(tree, out var model) ? model : models[tree] = compilation.GetSemanticModel(tree);

        return ProjectFacts.Combine(Declared(compilation.Assembly.GlobalNamespace).Select(type => Guarded(type, scope, Model)));
    }

    // An exception on one type is pinned to the document of its first declaration, and the other types survive it.
    private static ProjectFacts Guarded(INamedTypeSymbol type, ProjectScope scope, Func<SyntaxTree, SemanticModel> models)
    {
        ProjectFacts Walk()
        {
            scope.CancellationToken.ThrowIfCancellationRequested();
            return Fact(type, scope) is { } fact
                ? ProjectFacts.Empty with { Types = [fact], ExecutableMembers = [.. ExecutableMembers.Of(type, fact.Key, scope, models)] }
                : ProjectFacts.Empty;
        }

        return type.DeclaringSyntaxReferences.FirstOrDefault() is { } first
            ? AnalyzerErrors.Catch(Source.Document(first.SyntaxTree, scope.Context), Walk, scope.CancellationToken)
            : Walk();
    }

    private static TypeFact? Fact(INamedTypeSymbol type, ProjectScope scope)
    {
        // C# 14 extension blocks are named types too, but not ones a reader would call a type.
        if (Symbols.TypeKind(type) is not { } kind)
        {
            return null;
        }

        var isClass = type.TypeKind == TypeKind.Class;
        return new TypeFact(
            Symbols.TypeKey(type, scope.Context),
            type.Name,
            kind,
            Symbols.Accessibility(type),
            Abstract: isClass && type.IsAbstract,
            Sealed: isClass && type.IsSealed,
            Static: isClass && type.IsStatic,
            type.Arity,
            Symbols.Namespace(type),
            type.ContainingType is { } containing ? Symbols.TypeKey(containing, scope.Context) : null,
            [.. type.DeclaringSyntaxReferences.Select(reference => Declaration(type, reference, scope))]);
    }

    private static IEnumerable<INamedTypeSymbol> Declared(INamespaceSymbol container) =>
        container.GetNamespaceMembers().SelectMany(Declared).Concat(container.GetTypeMembers().SelectMany(WithNested));

    private static IEnumerable<INamedTypeSymbol> WithNested(INamedTypeSymbol type) =>
        type.GetTypeMembers().SelectMany(WithNested).Prepend(type);

    private static TypeDeclarationFact Declaration(INamedTypeSymbol type, SyntaxReference reference, ProjectScope scope)
    {
        var span = reference.GetSyntax(scope.CancellationToken) is CompilationUnitSyntax unit ? Source.TopLevelStatements(unit) : reference.Span;
        return new TypeDeclarationFact(
            Source.Document(reference.SyntaxTree, scope.Context),
            Source.Lines(reference.SyntaxTree, span, scope.CancellationToken),
            scope.Generated.DeclarationIsGenerated(type, reference));
    }
}
