using Typescape.Analyzer.Infrastructure;

namespace Typescape.Analyzer.Tests.Infrastructure;

public class SolutionPathsTests
{
    [Theory]
    [InlineData("/repo", "Msbuild failed when processing the file '/repo/App/App.csproj'", "Msbuild failed when processing the file 'App/App.csproj'")]
    [InlineData("/repo/", "/repo/A.csproj references /REPO/B/B.csproj", "A.csproj references B/B.csproj")]
    [InlineData(@"C:\repo", @"C:\repo\App\App.csproj and C:/repo/Lib/Lib.csproj", "App/App.csproj and Lib/Lib.csproj")]
    [InlineData(@"C:\repo", @"Project file not found: 'C:\repo\My App\App.csproj'. See C:\tools\x.log", "Project file not found: 'My App/App.csproj'. See C:\\tools\\x.log")]
    [InlineData(@"C:\repo", @"""C:\repo\Lib\Lib.csproj"": failed", @"""Lib/Lib.csproj"": failed")]
    [InlineData("/repo", "/repository/App.csproj and /other/repo/App.csproj stay", "/repository/App.csproj and /other/repo/App.csproj stay")]
    public void Paths_under_the_solution_directory_become_relative(string directory, string text, string expected)
    {
        Assert.Equal(expected, SolutionPaths.Relativize(text, directory));
    }

    [Theory]
    [InlineData("/repo", "/repo/App/Orders/Order.cs", "App/Orders/Order.cs")]
    [InlineData("/repo/", "/repo/App/App.csproj", "App/App.csproj")]
    [InlineData("/repo", "/Shared/Link.cs", "../Shared/Link.cs")]
    public void A_key_is_the_path_relative_to_the_solution_directory_with_forward_slashes(string directory, string path, string expected)
    {
        Assert.Equal(expected, SolutionPaths.Key(Path.GetFullPath(path), Path.GetFullPath(directory)));
    }

    [Theory]
    [InlineData("/home/me/.nuget/packages/xunit.v3.core/4.0.3/_content/Reporters.cs", "{nuget}/xunit.v3.core/4.0.3/_content/Reporters.cs")]
    [InlineData("/fallback/xunit.v3.core/4.0.3/_content/Reporters.cs", "{nuget}/xunit.v3.core/4.0.3/_content/Reporters.cs")]
    [InlineData("/repo/packages/Lib.1.0.0/content/A.cs", "packages/Lib.1.0.0/content/A.cs")]
    [InlineData("/Shared/Link.cs", "../Shared/Link.cs")]
    public void A_file_outside_the_solution_directory_in_a_package_folder_is_keyed_by_its_path_there(string path, string expected)
    {
        string[] folders = [Path.GetFullPath("/home/me/.nuget/packages"), Path.GetFullPath("/fallback"), Path.GetFullPath("/repo/packages")];
        Assert.Equal(expected, SolutionPaths.Key(Path.GetFullPath(path), Path.GetFullPath("/repo"), folders));
    }

    [Fact]
    public void A_relative_path_is_taken_as_relative_to_the_solution_directory_not_the_working_directory()
    {
        Assert.Equal("App/obj/Gen/Hint.g.cs", SolutionPaths.Key("App/obj/Gen/Hint.g.cs", Path.GetFullPath("/repo")));
    }
}
