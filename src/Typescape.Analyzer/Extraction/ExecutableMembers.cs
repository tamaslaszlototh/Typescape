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

    public static IEnumerable<ExecutableMemberFact> Of(
        INamedTypeSymbol type,
        string typeKey,
        ProjectContext context,
        CancellationToken cancellationToken)
    {
        // A C# 14 extension block is no type of its own, so its members belong to the static class around it.
        var extensionMembers = type.GetTypeMembers().Where(nested => nested.IsExtension).SelectMany(block => block.GetMembers());
        foreach (var method in type.GetMembers().Concat(extensionMembers).OfType<IMethodSymbol>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Fact(method, typeKey, context, cancellationToken) is { } fact)
            {
                yield return fact;
            }
        }
    }

    private static ExecutableMemberFact? Fact(IMethodSymbol method, string typeKey, ProjectContext context, CancellationToken cancellationToken)
    {
        // A partial member's definition part has no body; the implementation part, when there is one, has it.
        var withBody = method.PartialImplementationPart ?? method;
        if (withBody.IsImplicitlyDeclared
            || Kind(withBody) is not { } kind
            || withBody.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax(cancellationToken)).FirstOrDefault(HasBody) is not { } body)
        {
            return null;
        }

        // The extension block's own IDs hash its receiver; the implementation method on the static class has a plain one.
        var keySource = withBody.AssociatedExtensionImplementation ?? withBody;
        var entryPoint = body as CompilationUnitSyntax;
        return new ExecutableMemberFact(
            StableKeys.Member(typeKey, Symbols.DocumentationCommentId(keySource)),
            typeKey,
            entryPoint is null ? Signature(withBody) : EntryPointSignature,
            entryPoint is null ? kind : ExecutableMemberKind.EntryPoint,
            Symbols.Accessibility(withBody),
            Source.Document(body.SyntaxTree, context),
            Source.Lines(body.SyntaxTree, entryPoint is null ? Span(body) : Source.TopLevelStatements(entryPoint), cancellationToken));
    }

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
