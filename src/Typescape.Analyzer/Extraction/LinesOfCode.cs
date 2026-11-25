using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Typescape.Analyzer.Extraction;

/// <summary>
/// LOC (spec §5.3): the distinct physical lines the syntax tokens cover, a multi-line token covering each of its lines.
/// </summary>
internal static class LinesOfCode
{
    /// <summary>The LOC of an Executable Member's declaration: its attributes, signature and body.</summary>
    /// <param name="declaration">The member's whole declaration, or the compilation unit for the entry point.</param>
    public static int OfMember(SyntaxNode declaration, CancellationToken cancellationToken) =>
        Count(Tokens(declaration, _ => true), declaration.SyntaxTree, cancellationToken);

    /// <summary>
    /// The LOC of one declaration of a type, the declarations of its nested types left out: those are types of their own.
    /// </summary>
    /// <param name="declaration">The type's declaration, or the compilation unit for the synthesized <c>Program</c> type.</param>
    public static int OfType(SyntaxNode declaration, CancellationToken cancellationToken) =>
        Count(Tokens(declaration, node => node == declaration || !IsNestedType(node)), declaration.SyntaxTree, cancellationToken);

    private static IEnumerable<SyntaxToken> Tokens(SyntaxNode declaration, Func<SyntaxNode, bool> descend) =>
        declaration is CompilationUnitSyntax unit
            ? Source.GlobalStatements(unit).SelectMany(statement => statement.DescendantTokens(descend))
            : declaration.DescendantTokens(descend);

    // A C# 14 extension block is no type of its own: its members belong to the static class around it.
    private static bool IsNestedType(SyntaxNode node) =>
        node is BaseTypeDeclarationSyntax and not ExtensionBlockDeclarationSyntax or DelegateDeclarationSyntax;

    // Tokens come in text order, so their lines never go back: each token adds the lines past the last one counted.
    private static int Count(IEnumerable<SyntaxToken> tokens, SyntaxTree tree, CancellationToken cancellationToken)
    {
        var lines = tree.GetText(cancellationToken).Lines;
        var count = 0;
        var lastCounted = -1;
        foreach (var token in tokens)
        {
            // A missing token of broken code has no text, and so no line.
            if (token.Span.IsEmpty)
            {
                continue;
            }

            var firstNew = Math.Max(lines.IndexOf(token.SpanStart), lastCounted + 1);
            var lastLine = lines.IndexOf(token.Span.End - 1);
            if (lastLine >= firstNew)
            {
                count += lastLine - firstNew + 1;
                lastCounted = lastLine;
            }
        }

        return count;
    }
}
