using Microsoft.CodeAnalysis;
using Typescape.Analyzer.Infrastructure;

namespace Typescape.Analyzer.Extraction;

/// <summary>The extraction seam: turns one project's compilation into its <see cref="ProjectFacts"/>.</summary>
internal static class ProjectExtractor
{
    /// <summary>Everything extraction finds in the project: its declarations and each of its documents.</summary>
    public static ProjectFacts Extract(Compilation compilation, ProjectContext context, CancellationToken cancellationToken) =>
        ProjectFacts.Combine(
        [
            ExtractDeclarations(compilation, context, cancellationToken),
            .. compilation.SyntaxTrees.Select(document => ExtractDocument(compilation, context, document, cancellationToken)),
        ]);

    /// <summary>
    /// The part of <see cref="Extract"/> that runs once per project. Walk runs it and <see cref="ExtractDocument"/> as
    /// separate units, so the documents of a big project spread over all workers.
    /// </summary>
    public static ProjectFacts ExtractDeclarations(Compilation compilation, ProjectContext context, CancellationToken cancellationToken) =>
        OwnTypes.Extract(compilation, context, cancellationToken);

    /// <summary>The part of <see cref="Extract"/> that runs once per document of the compilation.</summary>
    public static ProjectFacts ExtractDocument(
        Compilation compilation,
        ProjectContext context,
        SyntaxTree document,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ProjectFacts([new DocumentFact(SolutionPaths.Key(document.FilePath, context.SolutionDirectory))], [], []);
    }
}
