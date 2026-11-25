namespace Typescape.Analyzer.Tests.Fixtures;

[Trait("Category", "Golden")]
public sealed class DeterminismTests
{
    // At least 4, so the parallel run interleaves even on a 2-core CI runner.
    private static readonly int Parallel = Math.Max(4, Environment.ProcessorCount);

    [Theory]
    [InlineData("Modern")]
    public async Task The_Snapshot_is_byte_identical_under_max_parallelism_1_and_N(string fixture)
    {
        var serial = FixtureSolution.Write(await FixtureSolution.AnalyzeAsync(fixture, request => request with { MaxParallelism = 1 }));
        var parallel = FixtureSolution.Write(await FixtureSolution.AnalyzeAsync(fixture, request => request with { MaxParallelism = Parallel }));

        // The text first, for a readable first difference.
        Assert.Equal(FixtureSolution.Render(serial), FixtureSolution.Render(parallel));
        Assert.Equal(serial, parallel);
    }
}
