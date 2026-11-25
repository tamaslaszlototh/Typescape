using Microsoft.CodeAnalysis;

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
        var scope = ProjectScope.Create(compilation, context, cancellationToken);
        var key = Source.Document(document, context);
        var documented = AnalyzerErrors.Catch(
            key,
            () => ProjectFacts.Empty with { Documents = [new DocumentFact(key, scope.Generated.Reason(document))] },
            cancellationToken);

        // The document outlives a failed walk: the declarations in it still name it. A failed Dependencies walk alone
        // keeps its generated reason.
        return ProjectFacts.Combine(
        [
            documented.Documents.Count > 0 ? documented : documented with { Documents = [new DocumentFact(key, GeneratedReason: null)] },
            AnalyzerErrors.Catch(
                key,
                () => Dependencies.Of(compilation.GetSemanticModel(document), key, scope),
                cancellationToken),
        ]);
    }
}
