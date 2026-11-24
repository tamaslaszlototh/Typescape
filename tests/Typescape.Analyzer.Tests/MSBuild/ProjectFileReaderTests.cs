using Typescape.Analyzer.MSBuild;

namespace Typescape.Analyzer.Tests.MSBuild;

public sealed class ProjectFileReaderTests : IDisposable
{
    private static readonly Dictionary<string, string> Debug = new() { ["Configuration"] = "Debug" };

    private readonly string _directory = Directory.CreateTempSubdirectory("typescape-refs-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Each_referenced_file_is_listed_once_in_item_order_without_the_project_itself()
    {
        var app = Sdk("App/App.csproj", """
            <ItemGroup>
              <ProjectReference Include="..\B\B.csproj;../A/A.csproj" />
              <ProjectReference Include="../B/b.csproj" />
              <ProjectReference Include="App.csproj" />
            </ItemGroup>
            """);

        Assert.Equal(["B/B.csproj", "A/A.csproj"], Read(app));
    }

    [Fact]
    public void Directory_Build_props_references_count_with_their_conditions_and_metadata()
    {
        Write("Directory.Build.props", """
            <Project>
              <ItemGroup Condition=" '$(MSBuildProjectName)' != 'Analyzers' AND '$(Configuration)' == 'Debug' ">
                <ProjectReference Include="$(MSBuildThisFileDirectory)src/Analyzers/Analyzers.csproj" OutputItemType="Analyzer" />
              </ItemGroup>
            </Project>
            """);
        var app = Sdk("src/App/App.csproj", """<ItemGroup><ProjectReference Include="../Core/Core.csproj" /></ItemGroup>""");
        var analyzers = Sdk("src/Analyzers/Analyzers.csproj", "");

        Assert.Equal(
            [
                new ProjectFileReference(Full("src/Analyzers/Analyzers.csproj"), ProjectReferenceKind.Analyzer),
                new ProjectFileReference(Full("src/Core/Core.csproj"), ProjectReferenceKind.Code),
            ],
            ProjectFileReader.ProjectReferences(app, "net10.0", Debug));
        Assert.Equal(["src/Core/Core.csproj"], Read(app, properties: new Dictionary<string, string> { ["Configuration"] = "Release" }));
        Assert.Empty(Read(analyzers));
    }

    [Fact]
    public void Metadata_comes_from_the_item_an_update_or_an_item_definition_and_not_referencing_the_output_wins()
    {
        var app = Sdk("App/App.csproj", """
            <ItemDefinitionGroup>
              <ProjectReference><ReferenceOutputAssembly>false</ReferenceOutputAssembly></ProjectReference>
            </ItemDefinitionGroup>
            <ItemGroup>
              <ProjectReference Include="../Tool/Tool.csproj" />
              <ProjectReference Include="../Lib/Lib.csproj" ReferenceOutputAssembly="true" />
              <ProjectReference Include="../Gen/Gen.csproj" ReferenceOutputAssembly="true" />
              <ProjectReference Update="../Gen/Gen.csproj" OutputItemType="Analyzer" />
              <ProjectReference Include="../Both/Both.csproj" OutputItemType="Analyzer"><ReferenceOutputAssembly>False</ReferenceOutputAssembly></ProjectReference>
            </ItemGroup>
            """);

        Assert.Equal(
            [
                ("Tool/Tool.csproj", ProjectReferenceKind.OutputNotReferenced),
                ("Lib/Lib.csproj", ProjectReferenceKind.Code),
                ("Gen/Gen.csproj", ProjectReferenceKind.Analyzer),
                ("Both/Both.csproj", ProjectReferenceKind.OutputNotReferenced),
            ],
            ReadKinds(app));
    }

    [Fact]
    public void A_file_referenced_twice_counts_as_its_stronger_reference()
    {
        var app = Sdk("App/App.csproj", """
            <ItemGroup>
              <ProjectReference Include="../Lib/Lib.csproj" OutputItemType="Analyzer" />
              <ProjectReference Include="../Lib/Lib.csproj" />
            </ItemGroup>
            """);

        Assert.Equal([("Lib/Lib.csproj", ProjectReferenceKind.Code)], ReadKinds(app));
    }

    [Theory]
    [InlineData("net48", "Framework/Framework.csproj")]
    [InlineData("net10.0", "Modern/Modern.csproj")]
    public void Conditions_see_the_target_framework_the_project_is_analyzed_for(string tfm, string expected)
    {
        var app = Sdk("App/App.csproj", """
            <ItemGroup Condition="'$(TargetFrameworkIdentifier)' == '.NETFramework'">
              <ProjectReference Include="../Framework/Framework.csproj" />
            </ItemGroup>
            <ItemGroup Condition="$(TargetFramework.StartsWith('net4')) != 'true' and $([MSBuild]::IsTargetFrameworkCompatible('$(TargetFramework)', 'net8.0'))">
              <ProjectReference Include="../Modern/Modern.csproj" />
            </ItemGroup>
            """, "<TargetFrameworks>net48;net10.0</TargetFrameworks>");

        Assert.Equal([expected], Read(app, tfm));
    }

    [Fact]
    public void Choose_takes_the_first_branch_that_holds()
    {
        var app = Sdk("App/App.csproj", """
            <PropertyGroup><Flavor>Blue</Flavor></PropertyGroup>
            <Choose>
              <When Condition="'$(Flavor)' == 'Red'"><ItemGroup><ProjectReference Include="../Red/Red.csproj" /></ItemGroup></When>
              <When Condition="'$(Flavor)' == 'blue'"><ItemGroup><ProjectReference Include="../Blue/Blue.csproj" /></ItemGroup></When>
              <Otherwise><ItemGroup><ProjectReference Include="../Other/Other.csproj" /></ItemGroup></Otherwise>
            </Choose>
            """);

        Assert.Equal(["Blue/Blue.csproj"], Read(app));
    }

    [Fact]
    public void The_items_of_a_Choose_see_the_properties_set_after_it()
    {
        Write("Directory.Build.targets", """<Project><PropertyGroup><Flavor>Late</Flavor></PropertyGroup></Project>""");
        var app = Sdk("App/App.csproj", """
            <Choose>
              <When Condition="'$(Flavor)' == 'Late'">
                <PropertyGroup><Early>true</Early></PropertyGroup>
                <ItemGroup><ProjectReference Include="../Late/Late.csproj" /></ItemGroup>
              </When>
              <Otherwise><ItemGroup><ProjectReference Include="../Early/Early.csproj" /></ItemGroup></Otherwise>
            </Choose>
            <ItemGroup Condition="'$(Early)' == 'true'"><ProjectReference Include="../Wrong/Wrong.csproj" /></ItemGroup>
            """);

        Assert.Equal(["Late/Late.csproj"], Read(app));
    }

    [Fact]
    public void Imports_are_followed_with_properties_set_before_them_and_conditions_relative_to_the_importing_file()
    {
        Write("build/Shared.props", """
            <Project>
              <Import Project="Extra.props" Condition="Exists('Extra.props')" />
              <ItemGroup><ProjectReference Include="$(SharedRoot)Shared/Shared.csproj" /></ItemGroup>
            </Project>
            """);
        Write("build/Extra.props", """<Project><ItemGroup><ProjectReference Include="../Extra/Extra.csproj" /></ItemGroup></Project>""");
        var app = Sdk("App/App.csproj", """
            <PropertyGroup><BuildDirectory>$(MSBuildProjectDirectory)/../build</BuildDirectory></PropertyGroup>
            <Import Project="$(BuildDirectory)/Shared.props" />
            <PropertyGroup><SharedRoot>$([MSBuild]::NormalizeDirectory('$(MSBuildProjectDirectory)', '..'))</SharedRoot></PropertyGroup>
            """);

        Assert.Equal(["Extra/Extra.csproj", "Shared/Shared.csproj"], Read(app));
    }

    [Fact]
    public void A_classic_project_gets_Directory_Build_files_where_it_imports_MSBuild_s_own()
    {
        Write("Directory.Build.props", """
            <Project>
              <PropertyGroup><ImportDirectoryBuildTargets>false</ImportDirectoryBuildTargets></PropertyGroup>
              <ItemGroup><ProjectReference Include="$(SolutionDir)Props/Props.csproj" /></ItemGroup>
              <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" Condition="'$(MSBuildThisFileDirectory)' != ''" />
            </Project>
            """);
        Write("Directory.Build.targets", """<Project><ItemGroup><ProjectReference Include="../Targets/Targets.csproj" /></ItemGroup></Project>""");
        var legacy = Write("Legacy/Legacy.csproj", """
            <?xml version="1.0" encoding="utf-8"?>
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" Condition="Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')" />
              <PropertyGroup>
                <Configuration Condition=" '$(Configuration)' == '' ">Release</Configuration>
                <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
              </PropertyGroup>
              <ItemGroup Condition=" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' and '$(TargetFramework)' == '' and '$(TargetFrameworkIdentifier)' == '.NETFramework' ">
                <ProjectReference Include="..\Lib\Lib.csproj">
                  <Project>{6F8D6F7D-0000-0000-0000-000000000000}</Project>
                  <Name>Lib</Name>
                </ProjectReference>
              </ItemGroup>
              <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
            </Project>
            """);
        var properties = new Dictionary<string, string>(Debug) { ["SolutionDir"] = _directory + Path.DirectorySeparatorChar };

        Assert.Equal(["Props/Props.csproj", "Lib/Lib.csproj"], Read(legacy, "net48", properties));
    }

    [Fact]
    public void Removed_and_excluded_references_are_left_out_and_wildcards_are_expanded()
    {
        Write("libs/One/One.csproj", "<Project />");
        Write("libs/nested/Two/Two.csproj", "<Project />");
        Write("libs/Old/Old.csproj", "<Project />");
        var app = Sdk("App/App.csproj", """
            <ItemGroup>
              <ProjectReference Include="../libs/**/*.csproj" Exclude="../libs/Old/Old.csproj" />
              <ProjectReference Include="../Gone/Gone.csproj" />
              <ProjectReference Remove="../Gone/Gone.csproj" />
            </ItemGroup>
            """);

        Assert.Equal(["libs/One/One.csproj", "libs/nested/Two/Two.csproj"], Read(app));
    }

    [Fact]
    public void A_condition_that_cannot_be_evaluated_counts_as_true_and_a_global_property_wins()
    {
        var app = Sdk("App/App.csproj", """
            <PropertyGroup><Configuration>Release</Configuration></PropertyGroup>
            <ItemGroup Condition="'@(Compile)' != '' and '$(Configuration)' == 'Debug'">
              <ProjectReference Include="../Lib/Lib.csproj" />
            </ItemGroup>
            <ItemGroup Condition="'$(Configuration)' == 'Release'">
              <ProjectReference Include="../Release/Release.csproj" />
            </ItemGroup>
            """);

        Assert.Equal(["Lib/Lib.csproj"], Read(app));
    }

    [Fact]
    public void A_property_the_SDK_or_a_package_sets_cannot_be_told_unless_the_files_set_it()
    {
        var tests = Sdk("Tests/Tests.csproj", """
            <ItemGroup Condition="'$(IsTestProject)' == 'true'"><ProjectReference Include="../Fixtures/Fixtures.csproj" /></ItemGroup>
            <ItemGroup Condition="'$(IsPackable)' == 'true'"><ProjectReference Include="../Packing/Packing.csproj" /></ItemGroup>
            <ItemGroup Condition="'$(UsingMicrosoftNETSdk)' != 'true'"><ProjectReference Include="../Classic/Classic.csproj" /></ItemGroup>
            """, "<TargetFramework>net10.0</TargetFramework><IsPackable>false</IsPackable>");

        Assert.Equal(["Fixtures/Fixtures.csproj"], Read(tests));
    }

    [Fact]
    public void A_file_that_is_not_XML_has_no_references()
    {
        var broken = Write("Broken/Broken.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>");

        Assert.Empty(Read(broken));
    }

    private string[] Read(string project, string? tfm = "net10.0", IReadOnlyDictionary<string, string>? properties = null) =>
        [.. ProjectFileReader.ProjectReferences(project, tfm, properties ?? Debug).Select(reference => Relative(reference.Path))];

    private (string, ProjectReferenceKind)[] ReadKinds(string project) =>
        [.. ProjectFileReader.ProjectReferences(project, "net10.0", Debug).Select(reference => (Relative(reference.Path), reference.Kind))];

    private string Relative(string path) => Path.GetRelativePath(_directory, path).Replace('\\', '/');

    private string Sdk(string path, string body, string properties = "<TargetFramework>net10.0</TargetFramework>") =>
        Write(path, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>{properties}</PropertyGroup>
              {body}
            </Project>
            """);

    private string Write(string path, string content)
    {
        var full = Full(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    private string Full(string path) => Path.GetFullPath(Path.Combine(_directory, path));
}
