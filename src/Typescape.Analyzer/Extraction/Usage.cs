using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Typescape.Analyzer.Extraction;

/// <summary>How a place in the source uses a member: the name it invokes, the definition and the accessor it reaches.</summary>
internal static class Usage
{
    /// <summary>The name an invocation calls: the member name of <c>x.M()</c> and <c>x?.M()</c>, else the expression itself.</summary>
    public static SyntaxNode InvokedName(ExpressionSyntax invoked) => invoked switch
    {
        MemberAccessExpressionSyntax access => access.Name,
        MemberBindingExpressionSyntax binding => binding.Name,
        _ => invoked,
    };

    /// <summary>The expression a name is the member part of (<c>x.P</c>, <c>?.P</c>), or the name itself.</summary>
    public static SyntaxNode Access(SyntaxNode name) =>
        name.Parent is MemberAccessExpressionSyntax access && access.Name == name || name.Parent is MemberBindingExpressionSyntax
            ? name.Parent
            : name;

    /// <summary>The definition a place uses: a reduced extension method as its static method, generics as their definition.</summary>
    public static ISymbol Definition(ISymbol used) =>
        used is IMethodSymbol { ReducedFrom: { } unreduced } ? unreduced.OriginalDefinition : used.OriginalDefinition;

    /// <summary>
    /// The method the place <paramref name="at"/> runs when it uses <paramref name="member"/>: a method itself, the
    /// accessor of a property or indexer it reads or writes, the <c>add</c> or <c>remove</c> accessor of an event.
    /// </summary>
    public static IMethodSymbol? Method(ISymbol member, SyntaxNode at) => member switch
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
        var access = Access(at);
        while (access.Parent is ArgumentSyntax { Parent: TupleExpressionSyntax tuple })
        {
            access = tuple;
        }

        return access.Parent is AssignmentExpressionSyntax assignment && assignment.Left == access ? assignment : null;
    }
}
