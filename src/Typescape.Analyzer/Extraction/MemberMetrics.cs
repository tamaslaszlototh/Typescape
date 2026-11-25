using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Typescape.Analyzer.Extraction;

/// <summary>The metrics of one Executable Member's body, lambdas and local functions included.</summary>
/// <param name="Cognitive">
/// Cognitive complexity: a clean-room reading of the SonarSource whitepaper v1.7 (Appendix B) with the C# mapping of
/// spec §5.1.
/// </param>
internal readonly record struct Metrics(int Cognitive);

/// <summary>
/// The single pass over an Executable Member's body that measures its <see cref="Metrics"/>. It reads syntax only,
/// except to bind the few places that may run the member itself.
/// </summary>
internal sealed class MemberMetrics : CSharpSyntaxWalker
{
    private readonly IMethodSymbol member;
    private readonly SemanticModel model;
    private readonly CancellationToken cancellationToken;
    private int cognitive;
    private int nesting;
    private bool recursive;

    private MemberMetrics(IMethodSymbol member, SemanticModel model, CancellationToken cancellationToken)
    {
        this.member = member;
        this.model = model;
        this.cancellationToken = cancellationToken;
    }

    /// <param name="member">The member whose body <paramref name="body"/> holds.</param>
    /// <param name="body">The member's syntax that holds its body, as <see cref="ExecutableMembers"/> finds it.</param>
    /// <param name="model">The semantic model of <paramref name="body"/>'s document.</param>
    public static Metrics Of(IMethodSymbol member, SyntaxNode body, SemanticModel model, CancellationToken cancellationToken)
    {
        var walker = new MemberMetrics(member, model, cancellationToken);

        // The entry point's code is its top-level statements, not the usings and types around them.
        IEnumerable<SyntaxNode> code = body is CompilationUnitSyntax unit ? unit.Members.OfType<GlobalStatementSyntax>() : [body];
        foreach (var node in code)
        {
            walker.Visit(node);
        }

        return new Metrics(walker.cognitive + (walker.recursive ? 1 : 0));
    }

    public override void Visit(SyntaxNode? node)
    {
        recursive = recursive || node is not null && MayRunMember(node) && RunsMember(node);
        base.Visit(node);
    }

    // Attribute arguments are constants, not code a reader follows.
    public override void VisitAttributeList(AttributeListSyntax node)
    {
    }

    public override void VisitIfStatement(IfStatementSyntax node)
    {
        cognitive += 1 + nesting;
        ScoreIfChain(node);
    }

    public override void VisitConditionalExpression(ConditionalExpressionSyntax node) =>
        ScoreStructure(node, node.WhenTrue, node.WhenFalse);

    public override void VisitSwitchStatement(SwitchStatementSyntax node) => ScoreStructure(node, node.Sections);

    public override void VisitSwitchExpression(SwitchExpressionSyntax node) => ScoreStructure(node, node.Arms);

    public override void VisitForStatement(ForStatementSyntax node) => ScoreStructure(node, node.Statement);

    public override void VisitForEachStatement(ForEachStatementSyntax node) => ScoreStructure(node, node.Statement);

    public override void VisitForEachVariableStatement(ForEachVariableStatementSyntax node) => ScoreStructure(node, node.Statement);

    public override void VisitWhileStatement(WhileStatementSyntax node) => ScoreStructure(node, node.Statement);

    public override void VisitDoStatement(DoStatementSyntax node) => ScoreStructure(node, node.Statement);

    public override void VisitCatchClause(CatchClauseSyntax node) => ScoreStructure(node, node.Block);

    public override void VisitGotoStatement(GotoStatementSyntax node)
    {
        cognitive++;
        base.VisitGotoStatement(node);
    }

    public override void VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        if (Operands(node) is null)
        {
            base.VisitBinaryExpression(node);
            return;
        }

        ScoreSequences(node);
    }

    public override void VisitBinaryPattern(BinaryPatternSyntax node) => ScoreSequences(node);

    public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) =>
        Deeper(() => base.VisitSimpleLambdaExpression(node));

    public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) =>
        Deeper(() => base.VisitParenthesizedLambdaExpression(node));

    public override void VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node) =>
        Deeper(() => base.VisitAnonymousMethodExpression(node));

    public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node) =>
        Deeper(() => base.VisitLocalFunctionStatement(node));

    // The inner `if` of an `else if` is part of the chain, so it gets no nesting increment of its own.
    private void ScoreIfChain(IfStatementSyntax node)
    {
        Visit(node.Condition);
        Deeper(() => Visit(node.Statement));
        switch (node.Else?.Statement)
        {
            case IfStatementSyntax elseIf:
                cognitive++;
                ScoreIfChain(elseIf);
                break;
            case { } @else:
                cognitive++;
                Deeper(() => Visit(@else));
                break;
        }
    }

    /// <summary>+1 plus the nesting level; <paramref name="nested"/> one level deeper, the rest of the node in place.</summary>
    private void ScoreStructure(SyntaxNode node, params IEnumerable<SyntaxNode> nested)
    {
        cognitive += 1 + nesting;
        var deeper = nested.ToHashSet();
        foreach (var child in node.ChildNodes())
        {
            if (deeper.Contains(child))
            {
                Deeper(() => Visit(child));
            }
            else
            {
                Visit(child);
            }
        }
    }

    private void Deeper(Action visit)
    {
        nesting++;
        visit();
        nesting--;
    }

    // +1 per run of like operators in textual order; a parenthesis or `!` ends the chain, so what it holds is a new one.
    private void ScoreSequences(SyntaxNode chain)
    {
        var operands = new List<SyntaxNode>();
        int? previousOperator = null;

        void Flatten(SyntaxNode node)
        {
            if (Operands(node) is not var (left, right))
            {
                operands.Add(node);
                return;
            }

            Flatten(left);
            if (node.RawKind != previousOperator)
            {
                cognitive++;
                previousOperator = node.RawKind;
            }

            Flatten(right);
        }

        Flatten(chain);
        foreach (var operand in operands)
        {
            Visit(operand);
        }
    }

    private static (SyntaxNode Left, SyntaxNode Right)? Operands(SyntaxNode node) => node switch
    {
        BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalAndExpression) || binary.IsKind(SyntaxKind.LogicalOrExpression) =>
            (binary.Left, binary.Right),
        BinaryPatternSyntax pattern => (pattern.Left, pattern.Right),
        _ => null,
    };

    // Binding is costly, so only syntax that could run the member gets bound.
    private bool MayRunMember(SyntaxNode node) => member.MethodKind switch
    {
        MethodKind.Ordinary => node is InvocationExpressionSyntax invocation && Usage.InvokedName(invocation.Expression) is SimpleNameSyntax name
            && name.Identifier.ValueText == member.Name,
        MethodKind.Constructor => node is BaseObjectCreationExpressionSyntax,
        MethodKind.PropertyGet or MethodKind.PropertySet when member.AssociatedSymbol is IPropertySymbol { IsIndexer: true } =>
            node is ElementAccessExpressionSyntax or ElementBindingExpressionSyntax,
        MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove =>
            node is SimpleNameSyntax name && name.Identifier.ValueText == member.AssociatedSymbol?.Name && !InNameof(name),
        MethodKind.UserDefinedOperator =>
            node is BinaryExpressionSyntax or PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax or AssignmentExpressionSyntax,
        MethodKind.Conversion => node is ExpressionSyntax,
        _ => false,
    };

    // A use runs the accessor D13 picks for it; a conversion operator may also run where the code converts implicitly.
    private bool RunsMember(SyntaxNode node) =>
        model.GetSymbolInfo(node, cancellationToken).Symbol is { } used && Usage.Method(Usage.Definition(used), node) is { } method && IsMember(method)
        || member.MethodKind == MethodKind.Conversion && model.GetConversion(node, cancellationToken).MethodSymbol is { } conversion && IsMember(conversion);

    private bool IsMember(IMethodSymbol method)
    {
        var definition = (IMethodSymbol)Usage.Definition(method);
        definition = definition.PartialImplementationPart ?? definition;
        return SymbolEqualityComparer.Default.Equals(definition, member)
            || SymbolEqualityComparer.Default.Equals(definition, member.AssociatedExtensionImplementation);
    }

    private static bool InNameof(SyntaxNode node) =>
        node.Ancestors().OfType<InvocationExpressionSyntax>().Any(invocation => invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" });
}
