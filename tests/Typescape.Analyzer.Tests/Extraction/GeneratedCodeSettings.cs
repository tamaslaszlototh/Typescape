using Microsoft.CodeAnalysis;

namespace Typescape.Analyzer.Tests.Extraction;

/// <summary>The <c>generated_code</c> analyzer-config setting of each document, as <paramref name="isGenerated"/> says.</summary>
internal sealed class GeneratedCodeSettings(Func<SyntaxTree, GeneratedKind> isGenerated) : SyntaxTreeOptionsProvider
{
    public override GeneratedKind IsGenerated(SyntaxTree tree, CancellationToken cancellationToken) => isGenerated(tree);

    public override bool TryGetDiagnosticValue(SyntaxTree tree, string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity)
    {
        severity = default;
        return false;
    }

    public override bool TryGetGlobalDiagnosticValue(string diagnosticId, CancellationToken cancellationToken, out ReportDiagnostic severity)
    {
        severity = default;
        return false;
    }
}
