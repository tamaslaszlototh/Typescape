using Typescape.Analyzer.MSBuild;
using Typescape.Snapshot.Records;

namespace Typescape.Analyzer.Tests.MSBuild;

public sealed class ProjectStylesTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("typescape-style-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup /></Project>""", ProjectStyle.Sdk)]
    [InlineData("""<Project><Sdk Name="Microsoft.NET.Sdk" /></Project>""", ProjectStyle.Sdk)]
    [InlineData("""<Project><Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk" /></Project>""", ProjectStyle.Sdk)]
    [InlineData("""<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003"><Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" /></Project>""", ProjectStyle.Legacy)]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>""", ProjectStyle.Sdk)]
    [InlineData("""<Project ToolsVersion="15.0"><PropertyGroup>""", ProjectStyle.Legacy)]
    public void The_style_comes_from_the_project_file(string text, ProjectStyle style)
    {
        var path = Path.Combine(_directory, "App.csproj");
        File.WriteAllText(path, text);

        Assert.Equal(style, ProjectStyles.Of(path));
    }

    [Fact]
    public void A_missing_project_file_has_no_style()
    {
        Assert.Null(ProjectStyles.Of(Path.Combine(_directory, "Missing.csproj")));
    }
}
