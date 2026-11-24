using Microsoft.CodeAnalysis;

namespace Typescape.Analyzer.Extraction;

/// <summary>What every extractor in one extraction unit needs besides the symbol or document it walks.</summary>
internal sealed record ProjectScope(ProjectContext Context, GeneratedCode Generated, CancellationToken CancellationToken)
{
    public static ProjectScope Create(Compilation compilation, ProjectContext context, CancellationToken cancellationToken) =>
        new(context, new GeneratedCode(compilation, context, cancellationToken), cancellationToken);
}
