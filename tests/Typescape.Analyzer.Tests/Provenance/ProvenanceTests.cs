using Typescape.Analyzer.Pipeline;
using Typescape.Analyzer.Tests.Fakes;
using Typescape.Snapshot.Records;
using static Typescape.Analyzer.AnalysisProgress;

namespace Typescape.Analyzer.Tests.Provenance;

public class ProvenanceTests
{
    [Fact]
    public async Task Provenance_records_the_tool_the_machine_the_source_revision_and_the_request()
    {
        var sources = new FakeProvenanceSources();
        var request = Request() with
        {
            Framework = "net8.0",
            Properties = new Dictionary<string, string> { ["Platform"] = "x64" },
        };

        var provenance = (await RunAsync(new FakeStages(1), sources, request)).Snapshot.Provenance;

        Assert.Equal(sources.Tool, provenance.Tool);
        Assert.Equal("10.0.404", provenance.SdkVersion);
        Assert.Equal("Fake OS 1.0 Arm64", provenance.Os);
        Assert.Equal(sources.Git, provenance.Git);
        Assert.Equal("net8.0", provenance.Framework);
        Assert.Equal(new Dictionary<string, string> { ["Platform"] = "x64" }, provenance.Properties);
    }

    [Fact]
    public async Task Without_a_git_repository_there_is_no_git_state()
    {
        var provenance = (await RunAsync(new FakeStages(1), new FakeProvenanceSources { Git = null })).Snapshot.Provenance;

        Assert.Null(provenance.Git);
    }

    [Fact]
    public async Task The_timestamp_is_the_start_in_UTC_to_the_whole_second()
    {
        var sources = new FakeProvenanceSources { UtcNow = new DateTimeOffset(2026, 11, 23, 14, 30, 15, 789, TimeSpan.FromHours(2)) };

        var provenance = (await RunAsync(new FakeStages(1), sources)).Snapshot.Provenance;

        Assert.Equal(new DateTimeOffset(2026, 11, 23, 12, 30, 15, TimeSpan.Zero), provenance.Timestamp);
        Assert.Equal(TimeSpan.Zero, provenance.Timestamp.Offset);
    }

    [Theory]
    [InlineData("/repo/Jellyfin.sln", "Jellyfin.sln", SolutionFormat.Sln)]
    [InlineData("/repo/src/Aspire.slnx", "Aspire.slnx", SolutionFormat.Slnx)]
    [InlineData("/repo/LEGACY.SLN", "LEGACY.SLN", SolutionFormat.Sln)]
    [InlineData("Relative.slnx", "Relative.slnx", SolutionFormat.Slnx)]
    public async Task The_solution_is_recorded_by_file_name_and_format(string path, string fileName, SolutionFormat format)
    {
        var provenance = (await RunAsync(new FakeStages(1), new FakeProvenanceSources(), Request(path))).Snapshot.Provenance;

        Assert.Equal(new SolutionProvenanceRecord(fileName, format), provenance.Solution);
    }

    [Theory]
    [InlineData("/repo/App/App.csproj")]
    [InlineData("/repo")]
    public async Task A_path_that_is_not_a_solution_file_is_rejected_before_any_phase(string path)
    {
        var stages = new FakeStages(1);

        await Assert.ThrowsAsync<ArgumentException>(() => RunAsync(stages, new FakeProvenanceSources(), Request(path)));

        Assert.Empty(stages.Log);
    }

    [Fact]
    public async Task The_sdk_and_git_state_are_read_in_the_solution_directory()
    {
        var sources = new FakeProvenanceSources();

        await RunAsync(new FakeStages(1), sources, Request("Sub/Relative.slnx"));

        var expected = Path.GetFullPath("Sub");
        Assert.Equal([expected, expected], sources.Directories);
    }

    [Fact]
    public async Task The_git_state_is_read_before_restore()
    {
        var stages = new FakeStages(1);

        await RunAsync(stages, new FakeProvenanceSources { Log = stages.Log });

        Assert.Equal(["git", "restore start"], stages.Log.Where(entry => entry is "git" or "restore start"));
    }

    [Fact]
    public async Task The_configuration_defaults_to_Debug()
    {
        var provenance = (await RunAsync(new FakeStages(1), new FakeProvenanceSources())).Snapshot.Provenance;

        Assert.Equal("Debug", provenance.Configuration);
        Assert.Empty(provenance.Properties);
    }

    [Theory]
    [InlineData("Configuration")]
    [InlineData("configuration")]
    public async Task The_configuration_comes_from_the_property_whatever_its_case(string name)
    {
        var request = Request() with { Properties = new Dictionary<string, string> { [name] = "Release" } };

        var provenance = (await RunAsync(new FakeStages(1), new FakeProvenanceSources(), request)).Snapshot.Provenance;

        Assert.Equal("Release", provenance.Configuration);
        Assert.Equal(new Dictionary<string, string> { [name] = "Release" }, provenance.Properties);
    }

    [Fact]
    public async Task Every_property_is_recorded_in_ordinal_name_order()
    {
        var request = Request() with
        {
            Properties = new Dictionary<string, string> { ["b"] = "2", ["Configuration"] = "Release", ["A"] = "1", ["_x"] = "3" },
        };

        var provenance = (await RunAsync(new FakeStages(1), new FakeProvenanceSources(), request)).Snapshot.Provenance;

        Assert.Equal(["A", "Configuration", "_x", "b"], provenance.Properties.Keys);
    }

    [Fact]
    public async Task Properties_that_differ_only_in_case_are_rejected_before_any_phase()
    {
        var stages = new FakeStages(1);
        var request = Request() with
        {
            Properties = new Dictionary<string, string> { ["Configuration"] = "Release", ["configuration"] = "Debug" },
        };

        var error = await Assert.ThrowsAsync<ArgumentException>(() => RunAsync(stages, new FakeProvenanceSources(), request));

        Assert.Contains("Configuration, configuration", error.Message);
        Assert.Empty(stages.Log);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Phase_seconds_are_the_reported_phase_durations_rounded_to_milliseconds(bool restore)
    {
        var progress = new RecordingProgress();

        var result = await RunAsync(new FakeStages(2, 3), new FakeProvenanceSources(), Request() with { Restore = restore }, progress);

        var reported = progress.Events
            .OfType<PhaseFinished>()
            .ToDictionary(finished => finished.Phase, finished => Math.Round(finished.Duration.TotalSeconds, 3));
        var expected = new PhaseSecondsRecord(
            restore ? reported[AnalysisPhase.Restore] : null,
            reported[AnalysisPhase.Load],
            reported[AnalysisPhase.Compile],
            reported[AnalysisPhase.Diagnostics],
            reported[AnalysisPhase.Walk],
            reported[AnalysisPhase.Assemble]);
        Assert.Equal(expected, result.Snapshot.Provenance.PhaseSeconds);
        Assert.True(expected.Load > 0);
    }

    [Fact]
    public async Task Assemble_receives_the_Provenance_so_far_and_the_Snapshot_gets_the_final_one()
    {
        ProvenanceRecord? received = null;
        var sources = new FakeProvenanceSources();
        var stages = new FakeStages(1) { OnAssemble = input => received = input.Provenance };

        var provenance = (await RunAsync(stages, sources)).Snapshot.Provenance;

        Assert.NotNull(received);
        Assert.Equal(0, received.PhaseSeconds.Assemble);
        Assert.Equal(received.PhaseSeconds with { Assemble = provenance.PhaseSeconds.Assemble }, provenance.PhaseSeconds);
        Assert.True(provenance.PhaseSeconds.Assemble > 0);

        Assert.Equal(1000 * sources.PeakReads, provenance.PeakMemoryBytes);
        Assert.True(provenance.PeakMemoryBytes > received.PeakMemoryBytes);
    }

    [Fact]
    public async Task The_pipeline_s_Provenance_replaces_whatever_Assemble_put_on_the_Snapshot()
    {
        var stages = new FakeStages(1);

        var provenance = (await RunAsync(stages, new FakeProvenanceSources())).Snapshot.Provenance;

        Assert.Equal("0.0.0", stages.Snapshot.Provenance.Tool.Version);
        Assert.Equal("9.9.9", provenance.Tool.Version);
    }

    private static AnalysisRequest Request(string solutionPath = "/repo/Fake.slnx") =>
        new() { SolutionPath = solutionPath, MaxParallelism = 2 };

    private static Task<AnalysisResult> RunAsync(
        FakeStages stages,
        FakeProvenanceSources sources,
        AnalysisRequest? request = null,
        IProgress<AnalysisProgress>? progress = null) =>
        AnalysisPipeline.RunAsync(request ?? Request(), stages, sources, progress, TestContext.Current.CancellationToken);
}
