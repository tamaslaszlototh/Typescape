using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Typescape.Analyzer.Extraction;

/// <summary>
/// LOC (spec §5.3): the distinct physical lines the syntax tokens cover, a multi-line token covering every one of its
/// lines. Blank lines, comments, preprocessor directives and inactive <c>#if</c> text hold no token, so they never count.
/// Lines are the file's own, <c>#line</c> directives ignored.
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

    // The entry point's code is its top-level statements, not the usings and types around them.
    private static IEnumerable<SyntaxToken> Tokens(SyntaxNode declaration, Func<SyntaxNode, bool> descend) =>
        declaration is CompilationUnitSyntax unit
            ? unit.Members.OfType<GlobalStatementSyntax>().SelectMany(statement => statement.DescendantTokens(descend))
            : declaration.DescendantTokens(descend);

    // A C# 14 extension block is no type of its own: its members belong to the static class around it.
    private static bool IsNestedType(SyntaxNode node) =>
        node is BaseTypeDeclarationSyntax and not ExtensionBlockDeclarationSyntax or DelegateDeclarationSyntax;

    // Tokens come in text order, so their lines never go back: each token adds the lines past the last one counted.
    private static int Count(IEnumerable<SyntaxToken> tokens, SyntaxTree tree, CancellationToken cancellationToken)
    {
        var lines = tree.GetText(cancellationToken).Lines;
        var count = 0;
        var last = -1;
        foreach (var token in tokens)
        {
            // A missing token of broken code has no text, and so no line.
            if (token.Span.IsEmpty)
            {
                continue;
            }

            var first = Math.Max(lines.IndexOf(token.SpanStart), last + 1);
            var end = lines.IndexOf(token.Span.End - 1);
            if (end >= first)
            {
                count += end - first + 1;
                last = end;
            }
        }

        return count;
    }
}
