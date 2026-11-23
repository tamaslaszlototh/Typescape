using System.Reflection;

namespace Typescape.Analyzer.Tests.Architecture;

public class PublicSurfaceTests
{
    [Fact]
    public void Only_the_entry_point_and_the_types_of_its_signature_are_public()
    {
        var exported = typeof(Analyzer).Assembly.GetExportedTypes();

        Assert.Equal(
            new[]
            {
                typeof(Analyzer), typeof(AnalysisRequest), typeof(AnalysisResult), typeof(LoadHealthSummary),
                typeof(AnalysisPhase), typeof(AnalysisProgress), typeof(AnalysisProgress.PhaseStarted),
                typeof(AnalysisProgress.PhaseProgressed), typeof(AnalysisProgress.PhaseFinished), typeof(AnalysisProgress.Warning),
                typeof(AnalysisProgress.LoadHealthReported),
            }.Select(type => type.FullName).Order(StringComparer.Ordinal),
            exported.Select(type => type.FullName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Analyzer_has_one_public_method_AnalyzeAsync()
    {
        var method = Assert.Single(typeof(Analyzer).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));

        Assert.Equal(nameof(Analyzer.AnalyzeAsync), method.Name);
        Assert.Equal(typeof(Task<AnalysisResult>), method.ReturnType);
        Assert.Equal(
            [typeof(AnalysisRequest), typeof(IProgress<AnalysisProgress>), typeof(CancellationToken)],
            method.GetParameters().Select(parameter => parameter.ParameterType));
    }
}
