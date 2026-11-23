using System.Reflection;

namespace Typescape.Analyzer.Tests;

public class PublicSurfaceTests
{
    [Fact]
    public void Only_the_entry_point_and_the_types_of_its_signature_are_public()
    {
        var exported = typeof(Analyzer).Assembly.GetExportedTypes().Select(type => type.Name).Order(StringComparer.Ordinal);

        Assert.Equal(
            [nameof(AnalysisProgress), nameof(AnalysisRequest), nameof(AnalysisResult), nameof(Analyzer), nameof(LoadHealthSummary)],
            exported);
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
