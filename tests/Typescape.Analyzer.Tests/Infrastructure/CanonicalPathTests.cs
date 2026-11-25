using Typescape.Analyzer.Infrastructure;

namespace Typescape.Analyzer.Tests.Infrastructure;

public sealed class CanonicalPathTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("typescape-path-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void A_path_through_a_symbolic_link_is_the_path_of_its_target()
    {
        // Creating a symbolic link on Windows needs a privilege.
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows paths are only made full.");
        var real = Directory.CreateDirectory(Path.Combine(_directory, "Real")).FullName;
        File.WriteAllText(Path.Combine(real, "Test.slnx"), "");
        Directory.CreateSymbolicLink(Path.Combine(_directory, "Link"), real);

        var canonical = CanonicalPath.Of(Path.Combine(_directory, "Link", "Test.slnx"));

        Assert.Equal(CanonicalPath.Of(Path.Combine(real, "Test.slnx")), canonical);
        Assert.EndsWith($"{Path.DirectorySeparatorChar}Real{Path.DirectorySeparatorChar}Test.slnx", canonical);
    }

    [Fact]
    public void A_path_in_another_case_has_the_case_on_disk_where_the_file_system_ignores_case()
    {
        var real = Directory.CreateDirectory(Path.Combine(_directory, "Real")).FullName;
        File.WriteAllText(Path.Combine(real, "Test.slnx"), "");
        var otherCase = Path.Combine(_directory, "REAL", "test.SLNX");
        Assert.SkipUnless(!OperatingSystem.IsWindows() && File.Exists(otherCase), "Needs a file system off Windows that ignores case.");

        Assert.Equal(CanonicalPath.Of(Path.Combine(real, "Test.slnx")), CanonicalPath.Of(otherCase));
    }

    [Fact]
    public void A_missing_path_is_only_made_full()
    {
        var missing = Path.Combine(_directory, "Missing", "..", "Missing", "Test.slnx");

        Assert.Equal(Path.GetFullPath(missing), CanonicalPath.Of(missing));
    }
}
