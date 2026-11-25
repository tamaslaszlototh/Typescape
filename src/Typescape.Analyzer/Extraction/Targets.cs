using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Typescape.Snapshot;

namespace Typescape.Analyzer.Extraction;

/// <summary>
/// The types and members one document's Occurrences target, each listed once: External Types, External members and
/// own non-executable members.
/// </summary>
internal sealed class Targets(ProjectScope scope)
{
    private static readonly SymbolDisplayFormat TypeNameFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

    private static readonly SymbolDisplayFormat MemberNameFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeParameters | SymbolDisplayMemberOptions.IncludeExplicitInterface,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeParamsRefOut,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    private readonly Dictionary<string, ExternalTypeFact> externalTypes = [];
    private readonly Dictionary<(string Type, string Key), ExternalMemberFact> externalMembers = [];
    private readonly Dictionary<string, NonExecutableMemberFact> nonExecutableMembers = [];

    public IEnumerable<ExternalTypeFact> ExternalTypes => externalTypes.Values;

    public IEnumerable<ExternalMemberFact> ExternalMembers => externalMembers.Values;

    public IEnumerable<NonExecutableMemberFact> NonExecutableMembers => nonExecutableMembers.Values;

    /// <param name="definition">An open definition.</param>
    public TargetType Type(INamedTypeSymbol definition)
    {
        var target = new TargetType(definition, Symbols.TypeKey(definition, scope.Context), definition.DeclaringSyntaxReferences.IsEmpty);
        if (target.External && !externalTypes.ContainsKey(target.Key))
        {
            externalTypes[target.Key] = new ExternalTypeFact(
                target.Key,
                definition.ToDisplayString(TypeNameFormat),
                definition.ContainingAssembly.Name,
                Symbols.Namespace(definition),
                Symbols.TypeKind(definition) ?? throw new InvalidOperationException($"{definition} is no kind of type a Dependency can target."));
        }

        return target;
    }

    /// <summary>
    /// The key of the member of <paramref name="target"/> that the place <paramref name="at"/> uses as
    /// <paramref name="used"/>; <c>null</c> when <paramref name="used"/> belongs to another type. An own property,
    /// indexer or event is the accessor the place uses when that is an Executable Member, otherwise the member itself.
    /// </summary>
    public string? Member(ISymbol used, TargetType target, SyntaxNode at)
    {
        var definition = used is IMethodSymbol { ReducedFrom: { } unreduced } ? unreduced.OriginalDefinition : used.OriginalDefinition;
        if (!SymbolEqualityComparer.Default.Equals(Symbols.Owner(definition)?.OriginalDefinition, target.Symbol))
        {
            return null;
        }

        // An extension block's members are implemented on the static class around it, with plain IDs.
        var named = definition is IMethodSymbol { AssociatedExtensionImplementation: { } implementation } ? implementation : definition;
        if (named.GetDocumentationCommentId() is not { } id)
        {
            return null;
        }

        var name = $"{target.Symbol.ToDisplayString(TypeNameFormat)}.{named.ToDisplayString(MemberNameFormat)}";
        if (target.External)
        {
            externalMembers.TryAdd((target.Key, id), new ExternalMemberFact(id, target.Key, name));
            return id;
        }

        if (Accessor(definition, at) is { } method && ExecutableMembers.Key(method, target.Key, scope.CancellationToken) is { } executableKey)
        {
            return executableKey;
        }

        var key = StableKeys.Member(target.Key, id);
        nonExecutableMembers.TryAdd(key, new NonExecutableMemberFact(key, target.Key, name));
        return key;
    }

    private static IMethodSymbol? Accessor(ISymbol member, SyntaxNode at) => member switch
    {
        IMethodSymbol method => method,
        IPropertySymbol property => IsWritten(at) ? property.SetMethod ?? property.GetMethod : property.GetMethod ?? property.SetMethod,
        IEventSymbol @event => Assignment(at)?.Kind() switch
        {
            SyntaxKind.AddAssignmentExpression => @event.AddMethod,
            SyntaxKind.SubtractAssignmentExpression => @event.RemoveMethod,
            _ => null,
        },
        _ => null,
    };

    // Only a simple assignment or an attribute's named argument writes: `x.P += 1` and `x.P++` read first, and so does
    // `P = { … }` in an initializer.
    private static bool IsWritten(SyntaxNode at) =>
        at.Parent is NameEqualsSyntax { Parent: AttributeArgumentSyntax }
        || Assignment(at) is { Right: not InitializerExpressionSyntax } assignment && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression);

    // The assignment whose left side is the access at the place, a deconstruction's tuple included.
    private static AssignmentExpressionSyntax? Assignment(SyntaxNode at)
    {
        var access = at.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == at || at.Parent is MemberBindingExpressionSyntax ? at.Parent : at;
        while (access.Parent is ArgumentSyntax { Parent: TupleExpressionSyntax tuple })
        {
            access = tuple;
        }

        return access.Parent is AssignmentExpressionSyntax assignment && assignment.Left == access ? assignment : null;
    }
}

/// <summary>A type a Dependency targets.</summary>
/// <param name="Symbol">Its open definition.</param>
/// <param name="External">Whether the source declares none of it.</param>
internal readonly record struct TargetType(INamedTypeSymbol Symbol, string Key, bool External);
