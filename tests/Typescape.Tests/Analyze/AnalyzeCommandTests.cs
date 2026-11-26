using System.CommandLine;
using Typescape.Analyze;
using Typescape.Analyzer;
using Typescape.Snapshot;
using Typescape.Snapshot.Records;

namespace Typescape.Tests.Analyze;

public sealed class AnalyzeCommandTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("typescape-analyze-").FullName;
    private readonly List<AnalysisRequest> _requests = [];
    private AnalysisProgress[] _progress = [];
    private bool _interactive;
    private SnapshotRecord _snapshot = Snapshots.Empty();

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task The_Snapshot_goes_to_the_current_directory_and_only_its_path_to_stdout()
    {
        var solution = Touch("src/App.slnx");

        var run = await RunAsync("analyze", "src");

        var path = Path.Combine(_directory, "App.typescape.json.gz");
        Assert.Equal((0, path + Environment.NewLine), (run.ExitCode, run.Stdout));
        Assert.True(File.Exists(path));
        Assert.Equal(solution, Assert.Single(_requests).SolutionPath);
    }

    [Theory]
    [InlineData("-o")]
    [InlineData("--output")]
    public async Task The_output_option_overrides_the_path_relative_to_the_current_directory(string option)
    {
        Touch("App.sln");
        Directory.CreateDirectory(Path.Combine(_directory, "out"));

        var run = await RunAsync("analyze", option, "out/snapshot.gz");

        var path = Path.Combine(_directory, "out", "snapshot.gz");
        Assert.Equal((0, path + Environment.NewLine), (run.ExitCode, run.Stdout));
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(Path.Combine(_directory, "App.typescape.json.gz")));
    }

    [Theory]
    [InlineData]
    [InlineData("App/App.csproj")]
    [InlineData("Missing.sln")]
    public async Task An_unusable_input_exits_2_on_stderr_without_analyzing(params string[] input)
    {
        Touch("App/App.csproj");

        var run = await RunAsync(["analyze", .. input]);

        Assert.Equal((2, ""), (run.ExitCode, run.Stdout));
        Assert.NotEqual("", run.Stderr);
        Assert.Empty(_requests);
    }

    [Fact]
    public async Task Several_solutions_exit_2_and_list_them()
    {
        Touch("App.sln");
        Touch("App.slnx");

        var run = await RunAsync("analyze");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("  App.sln" + Environment.NewLine + "  App.slnx", run.Stderr);
    }

    [Theory]
    [InlineData("-f")]
    [InlineData("--framework")]
    public async Task The_framework_option_names_the_target_framework_to_analyze(string option)
    {
        Touch("App.sln");

        var run = await RunAsync("analyze", option, "net8.0");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("net8.0", Assert.Single(_requests).Framework);
    }

    [Fact]
    public async Task Max_parallelism_defaults_to_the_core_count_and_the_option_overrides_it()
    {
        Touch("App.sln");

        await RunAsync("analyze");
        await RunAsync("analyze", "--max-parallelism", "1");

        Assert.Equal([Environment.ProcessorCount, 1], _requests.Select(request => request.MaxParallelism));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("many")]
    public async Task Max_parallelism_below_1_or_not_a_number_exits_2(string value)
    {
        Touch("App.sln");

        var run = await RunAsync("analyze", "--max-parallelism", value);

        Assert.Equal((2, ""), (run.ExitCode, run.Stdout));
        Assert.Contains("--max-parallelism", run.Stderr);
        Assert.Empty(_requests);
    }

    [Fact]
    public async Task Occurrences_and_restore_are_on_unless_switched_off()
    {
        Touch("App.sln");

        await RunAsync("analyze");
        await RunAsync("analyze", "--no-occurrences");
        await RunAsync("analyze", "--no-restore");

        Assert.Equal(
            [(true, true), (false, true), (true, false)],
            _requests.Select(request => (request.Occurrences, request.Restore)));
    }

    [Theory]
    [InlineData("-p", "Configuration=Release")]
    [InlineData("-p:Configuration=Release")]
    [InlineData("--property", "Configuration=Release")]
    [InlineData("--property:Configuration=Release")]
    public async Task A_property_is_an_MSBuild_global_property(params string[] flag)
    {
        Touch("App.sln");

        await RunAsync(["analyze", .. flag]);

        Assert.Equal(
            new Dictionary<string, string> { ["Configuration"] = "Release" },
            Assert.Single(_requests).Properties);
    }

    [Fact]
    public async Task Properties_repeat_and_a_later_one_wins_with_its_spelling_in_any_case()
    {
        Touch("App.sln");

        await RunAsync("analyze", "-p", "Empty=", "-p", "platform=x86", "-p", "Platform=x64");

        var properties = Assert.Single(_requests).Properties;
        Assert.Equal(["Empty", "Platform"], properties.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(("", "x64"), (properties["Empty"], properties["PLATFORM"]));
    }

    [Fact]
    public async Task A_property_value_is_taken_whole_with_its_equals_signs_and_semicolons()
    {
        Touch("App.sln");

        await RunAsync("analyze", "-p:DefineConstants=A=1;B", "-p", "NoWarn=CS1591;CS0618");

        Assert.Equal(
            new Dictionary<string, string> { ["DefineConstants"] = "A=1;B", ["NoWarn"] = "CS1591;CS0618" },
            Assert.Single(_requests).Properties);
    }

    [Fact]
    public async Task A_property_takes_one_token_and_leaves_the_input_after_it()
    {
        var solution = Touch("src/App.slnx");

        await RunAsync("analyze", "-p", "A=1", "src");

        Assert.Equal(solution, Assert.Single(_requests).SolutionPath);
    }

    [Theory]
    [InlineData("Configuration")]
    [InlineData("=Release")]
    public async Task A_property_without_a_name_or_an_equals_sign_exits_2(string value)
    {
        Touch("App.sln");

        var run = await RunAsync("analyze", "-p", value);

        Assert.Equal((2, ""), (run.ExitCode, run.Stdout));
        Assert.Contains(value, run.Stderr);
        Assert.Empty(_requests);
    }

    [Fact]
    public async Task Test_and_not_test_patterns_repeat_and_default_to_none()
    {
        Touch("App.sln");

        await RunAsync("analyze");
        await RunAsync("analyze", "--test", "*.Specs", "--not-test", "Gallery.Tests", "--test", "Acceptance?");

        Assert.Equal(
            [([], []), (["*.Specs", "Acceptance?"], ["Gallery.Tests"])],
            _requests.Select(request => (request.TestPatterns, request.NotTestPatterns)));
    }

    [Theory]
    [InlineData("None")]
    [InlineData("None", "--fail-on", "none")]
    [InlineData("Degraded", "--fail-on", "degraded")]
    [InlineData("Failed", "--fail-on", "failed")]
    public void Fail_on_takes_none_degraded_or_failed_and_defaults_to_none(string expected, params string[] flag) =>
        Assert.Equal(Enum.Parse<FailOn>(expected), Parse(["analyze", .. flag]).GetValue<FailOn>("--fail-on"));

    [Theory]
    [InlineData("Normal")]
    [InlineData("Quiet", "-v", "quiet")]
    [InlineData("Normal", "--verbosity", "normal")]
    [InlineData("Detailed", "-v", "detailed")]
    public void Verbosity_takes_quiet_normal_or_detailed_and_defaults_to_normal(string expected, params string[] flag) =>
        Assert.Equal(Enum.Parse<Verbosity>(expected), Parse(["analyze", .. flag]).GetValue<Verbosity>("--verbosity"));

    [Theory]
    [InlineData("--fail-on", "warning")]
    [InlineData("--verbosity", "minimal")]
    public async Task A_fail_on_or_verbosity_outside_its_values_exits_2(string option, string value)
    {
        Touch("App.sln");

        var run = await RunAsync("analyze", option, value);

        Assert.Equal((2, ""), (run.ExitCode, run.Stdout));
        Assert.Contains(value, run.Stderr);
        Assert.Empty(_requests);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("analyze", "--help")]
    [InlineData("analyze", "-h")]
    public async Task Help_works_on_every_command_without_analyzing(params string[] args)
    {
        var run = await RunAsync(args);

        Assert.Equal((0, ""), (run.ExitCode, run.Stderr));
        Assert.Contains("Usage:", run.Stdout);
        Assert.Empty(_requests);
    }

    [Fact]
    public async Task Analyze_help_lists_every_documented_flag()
    {
        var run = await RunAsync("analyze", "--help");

        Assert.All(
            [
                "-o, --output", "-f, --framework <tfm>", "--max-parallelism <n>", "--no-occurrences", "--no-restore",
                "-p, --property <Name=Value>", "--test <pattern>", "--not-test <pattern>",
                "--fail-on <none|degraded|failed>", "-v, --verbosity <quiet|normal|detailed>",
            ],
            flag => Assert.Contains(flag, run.Stdout));
    }

    [Theory]
    [InlineData("--bogus")]
    [InlineData("-c", "Release")]
    [InlineData("--configuration", "Release")]
    public async Task An_unknown_flag_exits_2_on_stderr_without_analyzing(params string[] flag)
    {
        Touch("App.sln");

        var run = await RunAsync(["analyze", .. flag]);

        Assert.Equal((2, ""), (run.ExitCode, run.Stdout));
        Assert.Contains(flag[0], run.Stderr);
        Assert.Empty(_requests);
    }

    [Fact]
    public async Task Redirected_stderr_gets_one_plain_line_per_phase_with_its_count_and_duration()
    {
        Touch("App.sln");
        _progress =
        [
            new AnalysisProgress.PhaseStarted(AnalysisPhase.Restore),
            new AnalysisProgress.PhaseFinished(AnalysisPhase.Restore, TimeSpan.FromSeconds(2.14)),
            new AnalysisProgress.PhaseStarted(AnalysisPhase.Load),
            new AnalysisProgress.PhaseProgressed(AnalysisPhase.Load, 0, 41),
            new AnalysisProgress.PhaseProgressed(AnalysisPhase.Load, 12, 41),
            new AnalysisProgress.PhaseProgressed(AnalysisPhase.Load, 41, 41),
            new AnalysisProgress.PhaseFinished(AnalysisPhase.Load, TimeSpan.FromSeconds(13.4)),
            new AnalysisProgress.PhaseStarted(AnalysisPhase.Walk),
            new AnalysisProgress.PhaseProgressed(AnalysisPhase.Walk, 1234, 1234),
            new AnalysisProgress.PhaseFinished(AnalysisPhase.Walk, TimeSpan.FromMilliseconds(80)),
        ];

        var run = await RunAsync("analyze");

        var lines = run.Stderr.Split(Environment.NewLine);
        Assert.Equal(
            [
                "restore        2.1s",
                "load          13.4s  41 projects",
                "walk           80ms  1234 documents",
            ],
            lines[..3]);
        Assert.Matches(@"^write +\d+ms$", lines[3]);
        Assert.DoesNotContain('\r', run.Stderr);
        Assert.DoesNotContain('\e', run.Stderr);
    }

    [Fact]
    public async Task A_terminal_gets_a_live_counter_that_the_phase_line_overwrites()
    {
        Touch("App.sln");
        _interactive = true;
        _progress =
        [
            new AnalysisProgress.PhaseStarted(AnalysisPhase.Compile),
            new AnalysisProgress.PhaseProgressed(AnalysisPhase.Compile, 0, 41),
            new AnalysisProgress.PhaseProgressed(AnalysisPhase.Compile, 41, 41),
            new AnalysisProgress.PhaseFinished(AnalysisPhase.Compile, TimeSpan.FromSeconds(1)),
        ];

        var run = await RunAsync("analyze");

        Assert.StartsWith("\rcompile", run.Stderr);
        Assert.Contains("\rcompile     41/41 projects", run.Stderr);
        Assert.Contains("\rcompile        1.0s  41 projects" + Environment.NewLine, run.Stderr);
        Assert.Matches(@"\rwrite +\d+ms *" + Environment.NewLine, run.Stderr);
    }

    [Fact]
    public async Task Load_Health_is_a_summary_line_then_one_line_per_non_ok_project_with_its_main_reason()
    {
        Touch("App.sln");
        _snapshot = Snapshots.WithProjects(
            ("App", Snapshots.Health()),
            ("Billing", Snapshots.Health(LoadStatus.Degraded, compileErrors: new CompileErrorsRecord(212, [new("CS0518", 212, "Predefined type 'System.Object' is not defined or imported")]))),
            ("Legacy", Snapshots.Health(LoadStatus.Degraded, metadataReferences: 0, objectResolves: false, compileErrors: new CompileErrorsRecord(40, [new("CS0518", 30, "…"), new("CS0246", 10, "…")]))),
            ("Ledger", Snapshots.Health(LoadStatus.Failed, metadataReferences: 0, workspaceDiagnostics: [new(WorkspaceDiagnosticKind.Warning, "Found project reference without a matching metadata reference."), new(WorkspaceDiagnosticKind.Failure, "Msbuild failed when processing the file 'Ledger/Ledger.csproj' with message: The SDK 'Acme.Sdk' could not be found.\nMore detail")])),
            ("Tools", Snapshots.Health(LoadStatus.Degraded, compileErrors: new CompileErrorsRecord(250, [new("CS0246", 200, "…"), new("CS0103", 50, "…")]))),
            ("Walker", Snapshots.Health(LoadStatus.Degraded, analyzerErrors: 2)));

        var run = await RunAsync("analyze", "--no-restore");

        Assert.EndsWith(
            string.Join(
                Environment.NewLine,
                "6 projects: 1 ok · 4 degraded · 1 failed",
                "  Billing  degraded  CS0518 ×212",
                "  Legacy   degraded  0 metadata references",
                "  Ledger   failed    Msbuild failed when processing the file 'Ledger/Ledger.csproj' with message: The SDK 'Acme.Sdk' could not be found.",
                "  Tools    degraded  CS0246 ×200 (250 compile errors)",
                "  Walker   degraded  2 analyzer errors",
                ""),
            run.Stderr);
    }

    [Fact]
    public async Task Quiet_prints_only_the_warnings_and_the_non_ok_projects()
    {
        Touch("App.sln");
        _progress =
        [
            new AnalysisProgress.PhaseStarted(AnalysisPhase.Restore),
            new AnalysisProgress.Warning(AnalysisPhase.Restore, "Restore failed:\n  error NU1301: Unable to load the service index."),
            new AnalysisProgress.PhaseFinished(AnalysisPhase.Restore, TimeSpan.FromSeconds(1)),
        ];
        _snapshot = Snapshots.WithProjects(
            ("App", Snapshots.Health()),
            ("Ledger", Snapshots.Health(LoadStatus.Degraded, metadataReferences: 0)));

        var run = await RunAsync("analyze", "-v", "quiet");

        Assert.Equal(
            string.Join(
                Environment.NewLine,
                "Restore failed:\n  error NU1301: Unable to load the service index.",
                "2 projects: 1 ok · 1 degraded · 0 failed",
                "  Ledger  degraded  0 metadata references",
                ""),
            run.Stderr);
    }

    [Fact]
    public async Task Quiet_prints_nothing_when_every_project_is_ok()
    {
        Touch("App.sln");
        _interactive = true;
        _progress =
        [
            new AnalysisProgress.PhaseStarted(AnalysisPhase.Load),
            new AnalysisProgress.PhaseProgressed(AnalysisPhase.Load, 0, 1),
            new AnalysisProgress.PhaseFinished(AnalysisPhase.Load, TimeSpan.FromSeconds(1)),
        ];
        _snapshot = Snapshots.WithProjects(("App", Snapshots.Health()));

        var run = await RunAsync("analyze", "-v", "quiet");

        Assert.Equal((0, ""), (run.ExitCode, run.Stderr));
    }

    [Fact]
    public async Task Detailed_lists_every_project_with_its_workspace_diagnostics_and_the_solution_diagnostics()
    {
        Touch("App.sln");
        _snapshot = Snapshots.WithProjects(
            [new(WorkspaceDiagnosticKind.Warning, "The solution configuration 'Debug|AnyCPU' maps no project.")],
            ("App", Snapshots.Health(workspaceDiagnostics: [new(WorkspaceDiagnosticKind.Info, "Found project reference without a matching metadata reference.")])),
            ("Ledger", Snapshots.Health(LoadStatus.Failed, metadataReferences: 0, workspaceDiagnostics: [new(WorkspaceDiagnosticKind.Failure, "The SDK 'Acme.Sdk' could not be found.\nSee the build log.")])),
            ("Tools", Snapshots.Health()));

        var run = await RunAsync("analyze", "--verbosity", "detailed");

        Assert.EndsWith(
            string.Join(
                Environment.NewLine,
                "3 projects: 2 ok · 0 degraded · 1 failed",
                "  App     ok",
                "    info     Found project reference without a matching metadata reference.",
                "  Ledger  failed  The SDK 'Acme.Sdk' could not be found.",
                "    failure  The SDK 'Acme.Sdk' could not be found.",
                "             See the build log.",
                "  Tools   ok",
                "  solution",
                "    warning  The solution configuration 'Debug|AnyCPU' maps no project.",
                ""),
            run.Stderr);
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = await Program.InvokeAsync(
            CreateRootCommand(),
            args,
            new InvocationConfiguration { Output = stdout, Error = stderr },
            TestContext.Current.CancellationToken);

        return (exitCode, stdout.ToString(), stderr.ToString());
    }

    private ParseResult Parse(string[] args) => CreateRootCommand().Parse(args);

    private RootCommand CreateRootCommand() =>
        Program.CreateRootCommand(_directory, _interactive, (request, progress, _) =>
        {
            _requests.Add(request);
            foreach (var report in _progress)
            {
                progress.Report(report);
            }

            return Task.FromResult(new AnalysisResult(_snapshot, Summary(_snapshot)));
        });

    private static LoadHealthSummary Summary(SnapshotRecord snapshot)
    {
        var statuses = snapshot.LoadHealth.Projects.Select(project => project.Status).ToList();
        return new LoadHealthSummary(
            statuses.Count(status => status == LoadStatus.Ok),
            statuses.Count(status => status == LoadStatus.Degraded),
            statuses.Count(status => status == LoadStatus.Failed));
    }

    private string Touch(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(_directory, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }
}
