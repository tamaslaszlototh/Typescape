using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Typescape.Snapshot.Records;

namespace Typescape.Analyzer.Provenance;

internal sealed class SystemProvenanceSources : IProvenanceSources
{
    // No first-run banner or telemetry from dotnet, no index lock from git.
    private static readonly Dictionary<string, string> QuietEnvironment = new()
    {
        ["DOTNET_NOLOGO"] = "1",
        ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
        ["GIT_OPTIONAL_LOCKS"] = "0",
    };

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public ToolProvenanceRecord Tool { get; } = ParseTool(
        typeof(SystemProvenanceSources).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? throw new InvalidOperationException("The Analyzer assembly has no informational version."));

    public string Os { get; } = $"{RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}";

    public async Task<string> GetSdkVersionAsync(string solutionDirectory, CancellationToken cancellationToken)
    {
        // A global.json in the solution directory picks the SDK, as it does for the BuildHost.
        ProcessResult result;
        try
        {
            result = await ProcessRunner.RunAsync("dotnet", ["--version"], solutionDirectory, QuietEnvironment, cancellationToken);
        }
        catch (Win32Exception e)
        {
            throw new InvalidOperationException("No .NET SDK found: the dotnet executable could not be started.", e);
        }

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"No usable .NET SDK found: 'dotnet --version' exited with code {result.ExitCode}. {result.Error.Trim()}");
        }

        return result.Output.Trim();
    }

    public async Task<GitProvenanceRecord?> GetGitAsync(string solutionDirectory, CancellationToken cancellationToken)
    {
        ProcessResult result;
        try
        {
            // Untracked files count as dirty: SDK-style projects compile them.
            result = await ProcessRunner.RunAsync(
                "git",
                ["status", "--porcelain=v2", "--branch", "--untracked-files=normal"],
                solutionDirectory,
                QuietEnvironment,
                cancellationToken);
        }
        catch (Win32Exception)
        {
            return null;
        }

        return result.ExitCode == 0 ? ParseGitStatus(result.Output) : null;
    }

    public long GetPeakMemoryBytes()
    {
        using var process = Process.GetCurrentProcess();
        var peak = process.PeakWorkingSet64;

        // .NET reports 0 on macOS.
        if (peak == 0 && OperatingSystem.IsMacOS() && GetResourceUsage(0 /* RUSAGE_SELF */, out var usage) == 0)
        {
            peak = usage.MaxResidentSetSize;
        }

        return peak;
    }

    internal static ToolProvenanceRecord ParseTool(string informationalVersion)
    {
        var plus = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        if (plus < 0)
        {
            return new ToolProvenanceRecord(informationalVersion, null);
        }

        var metadata = informationalVersion[(plus + 1)..];
        var commit = metadata.Length is 40 or 64 && metadata.All(char.IsAsciiHexDigitLower) ? metadata : null;
        return new ToolProvenanceRecord(informationalVersion[..plus], commit);
    }

    /// <returns><c>null</c> when the repository has no commit yet.</returns>
    internal static GitProvenanceRecord? ParseGitStatus(string output)
    {
        string? commit = null;
        string? branch = null;
        var dirty = false;
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("# branch.oid ", StringComparison.Ordinal))
            {
                commit = line["# branch.oid ".Length..];
            }
            else if (line.StartsWith("# branch.head ", StringComparison.Ordinal))
            {
                branch = line["# branch.head ".Length..];
            }
            else if (!line.StartsWith('#'))
            {
                dirty = true;
            }
        }

        if (commit is null or "(initial)")
        {
            return null;
        }

        return new GitProvenanceRecord(commit, branch is "(detached)" ? null : branch, dirty);
    }

    [DllImport("libc", EntryPoint = "getrusage")]
    private static extern int GetResourceUsage(int who, out ResourceUsage usage);

    [StructLayout(LayoutKind.Sequential)]
    private struct ResourceUsage
    {
        public long UserTimeSeconds;
        public long UserTimeMicroseconds;
        public long SystemTimeSeconds;
        public long SystemTimeMicroseconds;
        public long MaxResidentSetSize;
        public long SharedMemorySize;
        public long UnsharedDataSize;
        public long UnsharedStackSize;
        public long MinorFaults;
        public long MajorFaults;
        public long Swaps;
        public long BlockInputs;
        public long BlockOutputs;
        public long MessagesSent;
        public long MessagesReceived;
        public long Signals;
        public long VoluntaryContextSwitches;
        public long InvoluntaryContextSwitches;
    }
}
