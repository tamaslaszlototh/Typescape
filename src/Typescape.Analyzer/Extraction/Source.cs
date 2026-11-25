using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Typescape.Analyzer.Infrastructure;
using Typescape.Snapshot.Records;

namespace Typescape.Analyzer.Extraction;

/// <summary>Where declarations sit: document keys and line spans, as every extractor records them.</summary>
internal static class Source
{
    public static string Document(SyntaxTree tree, ProjectContext context) =>
        SolutionPaths.Key(tree.FilePath, context.SolutionDirectory);

    /// <summary>The physical lines <paramref name="span"/> covers, 1-based, <c>#line</c> directives ignored.</summary>
    public static LineSpan Lines(SyntaxTree tree, TextSpan span, CancellationToken cancellationToken)
    {
        var lines = tree.GetLineSpan(span, cancellationToken);
        return new LineSpan(lines.StartLinePosition.Line + 1, lines.EndLinePosition.Line + 1);
    }

    /// <summary>
    /// The entry point's code: the compilation unit that holds the top-level statements also holds usings and types.
    /// </summary>
    public static IEnumerable<GlobalStatementSyntax> GlobalStatements(CompilationUnitSyntax unit) =>
        unit.Members.OfType<GlobalStatementSyntax>();

    /// <summary>From the first top-level statement to the end of the last.</summary>
    public static TextSpan TopLevelStatements(CompilationUnitSyntax unit)
    {
        var statements = GlobalStatements(unit).ToArray();
        return TextSpan.FromBounds(statements[0].SpanStart, statements[^1].Span.End);
    }
}
