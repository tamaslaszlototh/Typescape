using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Typescape.Snapshot;
using Typescape.Snapshot.Records;

namespace Typescape.Analyzer.Extraction;

/// <summary>
/// The Executable Members of one own type: every member its source gives an executable body. Lambdas, anonymous
/// methods and local functions are part of their member's body, not members of their own.
/// </summary>
internal static class ExecutableMembers
{
    private const string EntryPointSignature = "<top-level statements>";

    private static readonly SymbolDisplayFormat SignatureFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeParameters | SymbolDisplayMemberOptions.IncludeExplicitInterface,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeParamsRefOut,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    /// <param name="models">The semantic model of a document, one per document.</param>
    public static IEnumerable<ExecutableMemberFact> Of(INamedTypeSymbol type, string typeKey, ProjectScope scope, Func<SyntaxTree, SemanticModel> models)
    {
        // A C# 14 extension block is no type of its own, so its members belong to the static class around it.
        var extensionMembers = type.GetTypeMembers().Where(nested => nested.IsExtension).SelectMany(block => block.GetMembers());
        foreach (var method in type.GetMembers().Concat(extensionMembers).OfType<IMethodSymbol>())
        {
            scope.CancellationToken.ThrowIfCancellationRequested();
            if (Fact(type, method, typeKey, scope, models) is { } fact)
            {
                yield return fact;
            }
        }
    }

    /// <summary>
    /// The key of <paramref name="method"/> of an own type when it is an Executable Member, whichever of its partial
    /// parts it is; <c>null</c> otherwise.
    /// </summary>
    /// <param name="typeKey">The key of the own type the member belongs to: for an extension-block member, the static class.</param>
    public static string? Key(IMethodSymbol method, string typeKey, CancellationToken cancellationToken) =>
        Body(method, cancellationToken) is { } found ? BodyKey(found.WithBody, typeKey) : null;

    private static ExecutableMemberFact? Fact(
        INamedTypeSymbol type,
        IMethodSymbol method,
        string typeKey,
        ProjectScope scope,
        Func<SyntaxTree, SemanticModel> models)
    {
        if (Body(method, scope.CancellationToken) is not { } found)
        {
            return null;
        }

        var (withBody, kind, body) = found;

        var entryPoint = body as CompilationUnitSyntax;
        var metrics = MemberMetrics.Of(withBody, body, models(body.SyntaxTree), scope.CancellationToken);
        return new ExecutableMemberFact(
            BodyKey(withBody, typeKey),
            typeKey,
            entryPoint is null ? Signature(withBody) : EntryPointSignature,
            entryPoint is null ? kind : ExecutableMemberKind.EntryPoint,
            Symbols.Accessibility(withBody),
            Source.Document(body.SyntaxTree, scope.Context),
            Source.Lines(body.SyntaxTree, entryPoint is null ? Span(body) : Source.TopLevelStatements(entryPoint), scope.CancellationToken),
            metrics.Cyclomatic,
            metrics.Cognitive,
            scope.Generated.MemberIsGenerated(type, withBody, body));
    }

    // A partial member's definition part has no body; the implementation part, when there is one, has it.
    private static (IMethodSymbol WithBody, ExecutableMemberKind Kind, SyntaxNode Body)? Body(IMethodSymbol method, CancellationToken cancellationToken)
    {
        var withBody = method.PartialImplementationPart ?? method;
        return !withBody.IsImplicitlyDeclared
            && Kind(withBody) is { } kind
            && withBody.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax(cancellationToken)).FirstOrDefault(HasBody) is { } body
            ? (withBody, kind, body)
            : null;
    }

    // The extension block's own IDs hash its receiver; the implementation method on the static class has a plain one.
    private static string BodyKey(IMethodSymbol withBody, string typeKey) =>
        StableKeys.Member(typeKey, Symbols.DocumentationCommentId(withBody.AssociatedExtensionImplementation ?? withBody));

    private static ExecutableMemberKind? Kind(IMethodSymbol method) =>
        // An explicit implementation is the kind of the interface member it implements: an operator stays an operator.
        (method.MethodKind == MethodKind.ExplicitInterfaceImplementation && method.ExplicitInterfaceImplementations is [var implemented, ..]
            ? implemented.MethodKind
            : method.MethodKind) switch
        {
            MethodKind.Ordinary or MethodKind.ExplicitInterfaceImplementation => ExecutableMemberKind.Method,
            MethodKind.Constructor or MethodKind.StaticConstructor => ExecutableMemberKind.Constructor,
            MethodKind.Destructor => ExecutableMemberKind.Destructor,
            MethodKind.UserDefinedOperator => ExecutableMemberKind.Operator,
            MethodKind.Conversion => ExecutableMemberKind.Conversion,
            MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove or MethodKind.EventRaise =>
                ExecutableMemberKind.Accessor,
            _ => null,
        };

    // Abstract, extern, interface and partial-definition members, auto-accessors and primary constructors have none.
    private static bool HasBody(SyntaxNode syntax) => syntax switch
    {
        BaseMethodDeclarationSyntax member => member.Body is not null || member.ExpressionBody is not null,
        AccessorDeclarationSyntax accessor => accessor.Body is not null || accessor.ExpressionBody is not null,
        ArrowExpressionClauseSyntax => true,
        CompilationUnitSyntax => true,
        _ => false,
    };

    // An expression-bodied property or indexer is its getter, so the getter spans the whole declaration.
    private static TextSpan Span(SyntaxNode body) =>
        body is ArrowExpressionClauseSyntax { Parent: BasePropertyDeclarationSyntax property } ? property.Span : body.Span;

    private static string Signature(IMethodSymbol method) =>
        method.MethodKind == MethodKind.StaticConstructor ? $"static {method.ToDisplayString(SignatureFormat)}"
        : method.ContainingType is { IsExtension: true, ExtensionParameter: { } receiver }
            ? $"extension({receiver.Type.ToDisplayString(SignatureFormat)}).{method.ToDisplayString(SignatureFormat)}"
        : method.ToDisplayString(SignatureFormat);
}
