using System.Globalization;
using Microsoft.CodeAnalysis;
using Typescape.Analyzer.Infrastructure;
using Typescape.Snapshot.Records;

namespace Typescape.Analyzer.Pipeline;

/// <summary>The compile-error statistics of one project, for Load Health.</summary>
internal static class CompileErrors
{
    /// <param name="diagnostics">The compilation's diagnostics; only the errors count.</param>
    /// <param name="solutionDirectory">The absolute solution directory, which sample messages are made relative to.</param>
    public static CompileErrorsRecord Summarize(IEnumerable<Diagnostic> diagnostics, string solutionDirectory)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        // The order of the samples: a diagnostic without a source location first, then by document path and position.
        var errors = diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => (diagnostic.Id, Span: diagnostic.Location.GetLineSpan(), Message: diagnostic.GetMessage(CultureInfo.InvariantCulture)))
            .OrderBy(error => error.Span.Path ?? "", StringComparer.Ordinal)
            .ThenBy(error => error.Span.StartLinePosition)
            .ThenBy(error => error.Message, StringComparer.Ordinal)
            .ToArray();

        var top = errors
            .GroupBy(error => error.Id, StringComparer.Ordinal)
            .Select(code => new CompileErrorCodeRecord(code.Key, code.Count(), SolutionPaths.Relativize(code.First().Message, solutionDirectory)))
            .OrderByDescending(code => code.Count)
            .ThenBy(code => code.Code, StringComparer.Ordinal)
            .Take(CompileErrorsRecord.TopCount)
            .ToArray();

        return new CompileErrorsRecord(errors.Length, top);
    }
}
