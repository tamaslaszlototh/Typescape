using Typescape.Analyzer.MSBuild;

namespace Typescape.Analyzer.Tests.MSBuild;

public class MSBuildPathsTests
{
    [Theory]
    [InlineData(@"Msbuild failed when processing the file 'C:\repo\App\App.csproj'", 41)]
    [InlineData("Msbuild failed when processing the file 'C:/repo/App/App.csproj'", 41)]
    [InlineData(@"Msbuild failed when processing the file 'c:\REPO\app\app.csproj'", 41)]
    [InlineData(@"Msbuild failed when processing the file 'C:\repo\Lib\Lib.csproj'", -1)]
    public void A_message_names_a_path_with_either_separator_in_any_case(string message, int index)
    {
        Assert.Equal(index, MSBuildPaths.IndexIn(message, @"C:\repo\App\App.csproj"));
    }

    [Theory]
    [InlineData(@"Found project reference without a matching metadata reference: C:\repo\Lib\Lib.csproj", true)]
    [InlineData("Found project reference without a matching metadata reference: C:/repo/Lib/Lib.csproj", true)]
    [InlineData(@"C:\repo\Lib\Lib.csproj is referenced", false)]
    public void A_message_ends_with_a_path_with_either_separator(string message, bool endsWith)
    {
        Assert.Equal(endsWith, MSBuildPaths.EndsWith(message, @"C:\repo\Lib\Lib.csproj"));
    }
}
