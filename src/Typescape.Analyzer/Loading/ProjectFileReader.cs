using System.Xml;
using System.Xml.Linq;

namespace Typescape.Analyzer.Loading;

/// <summary>
/// Reads the <c>ProjectReference</c> items of one project file without MSBuild, because MSBuildWorkspace reports only
/// the transitive closure.
/// </summary>
/// <remarks>
/// Best-effort, in MSBuild's passes: properties and imports in file order, then item definitions, then items, both
/// against the final properties. It follows imports and Directory.Build.props/targets, but no SDK or MSBuild file. A
/// condition it cannot evaluate counts as true.
/// </remarks>
internal sealed class ProjectFileReader
{
    private const string ReferenceOutputAssembly = nameof(ReferenceOutputAssembly);
    private const string OutputItemType = nameof(OutputItemType);

    // Set in files the reader does not read: MSBuild's installation, the SDK, NuGet's generated props and packages'
    // props. A project file may still set them itself.
    private static readonly string[] UnreadProperties =
    [
        "MSBuildBinPath", "MSBuildExtensionsPath", "MSBuildExtensionsPath32", "MSBuildExtensionsPath64",
        "MSBuildFrameworkToolsPath", "MSBuildFrameworkToolsPath32", "MSBuildFrameworkToolsPath64", "MSBuildRuntimeType",
        "MSBuildSDKsPath", "MSBuildToolsPath", "MSBuildToolsPath32", "MSBuildToolsPath64", "MSBuildToolsVersion",
        "MSBuildVersion", "NETCoreSdkVersion", "NuGetPackageRoot", "MSBuildProjectExtensionsPath",
        "BaseIntermediateOutputPath", "IntermediateOutputPath", "OutputPath", "IsTestProject", "IsPackable",
    ];

    private readonly MSBuildExpressions _expressions;
    private readonly string _projectPath;
    private readonly string _projectDirectory;
    private readonly string? _tfm;
    private readonly HashSet<string> _imported = new(MSBuildPaths.Comparer);
    private readonly List<(XElement Group, string File)> _itemDefinitionGroups = [];
    private readonly List<(XElement Element, string File)> _itemElements = [];
    private bool _propsImported;
    private bool _targetsImported;

    private ProjectFileReader(string projectPath, string? tfm, IReadOnlyDictionary<string, string> globalProperties)
    {
        _projectPath = projectPath;
        _projectDirectory = Path.GetDirectoryName(projectPath)!;
        _tfm = tfm;
        _expressions = new MSBuildExpressions(projectPath);

        foreach (System.Collections.DictionaryEntry variable in Environment.GetEnvironmentVariables())
        {
            _expressions.Set((string)variable.Key, (string?)variable.Value ?? "");
        }

        foreach (var name in UnreadProperties)
        {
            _expressions.Set(name, null);
        }

        _expressions.Set("MSBuildProjectFullPath", projectPath);
        _expressions.Set("MSBuildProjectDirectory", _projectDirectory);
        _expressions.Set("MSBuildProjectFile", Path.GetFileName(projectPath));
        _expressions.Set("MSBuildProjectName", Path.GetFileNameWithoutExtension(projectPath));
        _expressions.Set("MSBuildProjectExtension", Path.GetExtension(projectPath));
        _expressions.Set("OS", OperatingSystem.IsWindows() ? "Windows_NT" : "Unix");
        _expressions.Set("Platform", "AnyCPU");

        foreach (var (name, value) in globalProperties)
        {
            _expressions.SetGlobal(name, value);
        }
    }

    /// <summary>The <c>ProjectReference</c> items of <paramref name="projectPath"/>, each file once, in item order.</summary>
    /// <param name="projectPath">The project file's full path.</param>
    /// <param name="tfm">The target framework the project is analyzed for; conditions on <c>TargetFramework</c> see it.</param>
    /// <param name="globalProperties">The global properties Load evaluates the projects with.</param>
    /// <returns>Empty when the file cannot be read as XML.</returns>
    public static IReadOnlyList<ProjectFileReference> ProjectReferences(
        string projectPath,
        string? tfm,
        IReadOnlyDictionary<string, string> globalProperties)
    {
        ArgumentNullException.ThrowIfNull(globalProperties);
        return new ProjectFileReader(Path.GetFullPath(projectPath), tfm, globalProperties).Read();
    }

    private List<ProjectFileReference> Read()
    {
        if (Load(_projectPath) is not { } project)
        {
            return [];
        }

        var sdkStyle = ProjectStyles.IsSdk(project);
        _expressions.Set("UsingMicrosoftNETSdk", sdkStyle ? "true" : "");

        // Each Roslyn project of an SDK-style file is MSBuild's inner build for one target framework; a classic project
        // has no TargetFramework at all.
        if (sdkStyle && _tfm is not null)
        {
            _expressions.SetGlobal("TargetFramework", _tfm);
        }

        if (sdkStyle)
        {
            ImportDirectoryBuildProps();
        }

        Walk(_projectPath, project);
        if (sdkStyle)
        {
            ImportDirectoryBuildTargets();
        }

        InferTargetFramework();
        return References(ItemDefinitions());
    }

    private XElement? Load(string path)
    {
        if (!_imported.Add(path))
        {
            return null;
        }

        try
        {
            return XDocument.Load(path).Root;
        }
        catch (Exception exception) when (exception is XmlException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void Walk(string file, XElement project)
    {
        var previous = _expressions.ThisFile;
        _expressions.ThisFile = file;
        foreach (var element in project.Elements())
        {
            switch (element.Name.LocalName)
            {
                case "PropertyGroup":
                    SetProperties(element);
                    break;
                case "ItemGroup":
                    _itemElements.Add((element, file));
                    break;
                case "ItemDefinitionGroup":
                    _itemDefinitionGroups.Add((element, file));
                    break;
                case "Choose":
                    ChooseProperties(element);
                    _itemElements.Add((element, file));
                    break;
                case "Import":
                    Import(element);
                    break;
                case "ImportGroup" when Holds(element, ThisFileDirectory):
                    foreach (var import in element.Elements().Where(import => import.Name.LocalName == "Import"))
                    {
                        Import(import);
                    }

                    break;
            }
        }

        _expressions.ThisFile = previous;
    }

    private void SetProperties(XElement group)
    {
        if (Holds(group))
        {
            foreach (var property in group.Elements().Where(property => Holds(property)))
            {
                _expressions.Set(property.Name.LocalName, _expressions.Expand(property.Value));
            }
        }
    }

    // The property pass decides a Choose with the properties so far; the item pass decides it again with the final ones.
    private void ChooseProperties(XElement choose)
    {
        foreach (var element in Branch(choose)?.Elements() ?? [])
        {
            if (element.Name.LocalName == "PropertyGroup")
            {
                SetProperties(element);
            }
            else if (element.Name.LocalName == "Choose")
            {
                ChooseProperties(element);
            }
        }
    }

    private XElement? Branch(XElement choose) =>
        choose.Elements().FirstOrDefault(branch => branch.Name.LocalName == "Otherwise" || (branch.Name.LocalName == "When" && Holds(branch)));

    private void Import(XElement import)
    {
        var project = import.Attribute("Project")?.Value.Trim() ?? "";
        if (!Holds(import, ThisFileDirectory))
        {
            return;
        }

        // Where the SDK and MSBuild files, which are not read, import Directory.Build.props and .targets.
        if (import.Attribute("Sdk") is not null)
        {
            if (project.EndsWith("Sdk.props", StringComparison.OrdinalIgnoreCase))
            {
                ImportDirectoryBuildProps();
            }
            else if (project.EndsWith("Sdk.targets", StringComparison.OrdinalIgnoreCase))
            {
                ImportDirectoryBuildTargets();
            }

            return;
        }

        if (project.EndsWith("Microsoft.Common.props", StringComparison.OrdinalIgnoreCase))
        {
            ImportDirectoryBuildProps();
            return;
        }

        if (project.EndsWith("Microsoft.CSharp.targets", StringComparison.OrdinalIgnoreCase)
            || project.EndsWith("Microsoft.Common.targets", StringComparison.OrdinalIgnoreCase))
        {
            ImportDirectoryBuildTargets();
            return;
        }

        if (_expressions.Expand(project) is not { Length: > 0 } expanded)
        {
            return;
        }

        foreach (var path in MSBuildPaths.Items(expanded, ThisFileDirectory))
        {
            if (Load(path) is { } imported)
            {
                Walk(path, imported);
            }
        }
    }

    private void ImportDirectoryBuildProps()
    {
        if (!_propsImported)
        {
            _propsImported = true;
            ImportDirectoryBuild("ImportDirectoryBuildProps", "DirectoryBuildPropsPath", "Directory.Build.props");
        }
    }

    private void ImportDirectoryBuildTargets()
    {
        if (!_targetsImported)
        {
            _targetsImported = true;
            ImportDirectoryBuild("ImportDirectoryBuildTargets", "DirectoryBuildTargetsPath", "Directory.Build.targets");
        }
    }

    private void ImportDirectoryBuild(string switchName, string pathName, string fileName)
    {
        if (string.Equals(_expressions.Value(switchName)?.Trim(), "false", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var path = _expressions.Value(pathName) is { Length: > 0 } set
            ? MSBuildPaths.Full(set, _projectDirectory)
            : MSBuildPaths.FileAbove(_projectDirectory, fileName);
        if (path is not null && Load(path) is { } imported)
        {
            Walk(path, imported);
        }
    }

    // The SDK infers these after the project file, so only the item passes see them.
    private void InferTargetFramework()
    {
        if (_tfm is null)
        {
            return;
        }

        foreach (var (name, value) in new[]
        {
            ("TargetFrameworkIdentifier", TargetFrameworks.FrameworkIdentifier(_tfm)),
            ("TargetFrameworkVersion", TargetFrameworks.FrameworkVersion(_tfm) is { } version ? $"v{version}" : null),
            ("TargetPlatformIdentifier", TargetFrameworks.PlatformIdentifier(_tfm)),
        })
        {
            if (value is not null && _expressions.Value(name) is "")
            {
                _expressions.Set(name, value);
            }
        }
    }

    private Dictionary<string, string> ItemDefinitions()
    {
        var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (group, file) in _itemDefinitionGroups)
        {
            _expressions.ThisFile = file;
            if (!Holds(group))
            {
                continue;
            }

            foreach (var definition in group.Elements().Where(IsProjectReference))
            {
                foreach (var name in new[] { ReferenceOutputAssembly, OutputItemType })
                {
                    if (Metadata(definition, name) is { } value)
                    {
                        defaults[name] = value;
                    }
                }
            }
        }

        return defaults;
    }

    private List<ProjectFileReference> References(Dictionary<string, string> defaults)
    {
        var items = new List<Item>();
        foreach (var (element, file) in _itemElements)
        {
            _expressions.ThisFile = file;
            AddItems(element, items, defaults);
        }

        return
        [
            .. items
                .Where(item => !MSBuildPaths.Comparer.Equals(item.Path, _projectPath))
                .GroupBy(item => item.Path, MSBuildPaths.Comparer)
                .Select(file => new ProjectFileReference(file.Key, file.Min(item => item.Kind))),
        ];
    }

    private void AddItems(XElement element, List<Item> items, Dictionary<string, string> defaults)
    {
        if (element.Name.LocalName == "Choose")
        {
            foreach (var child in Branch(element)?.Elements() ?? [])
            {
                AddItems(child, items, defaults);
            }

            return;
        }

        if (element.Name.LocalName != "ItemGroup" || !Holds(element))
        {
            return;
        }

        foreach (var reference in element.Elements().Where(reference => IsProjectReference(reference) && Holds(reference)))
        {
            if (reference.Attribute("Include") is { } include)
            {
                var excluded = ItemPaths(reference.Attribute("Exclude")).ToHashSet(MSBuildPaths.Comparer);
                items.AddRange(ItemPaths(include)
                    .Where(path => !excluded.Contains(path))
                    .Select(path => new Item(
                        path,
                        Metadata(reference, ReferenceOutputAssembly) ?? defaults.GetValueOrDefault(ReferenceOutputAssembly),
                        Metadata(reference, OutputItemType) ?? defaults.GetValueOrDefault(OutputItemType))));
            }
            else if (reference.Attribute("Update") is { } update)
            {
                var updated = ItemPaths(update).ToHashSet(MSBuildPaths.Comparer);
                var referenceOutputAssembly = Metadata(reference, ReferenceOutputAssembly);
                var outputItemType = Metadata(reference, OutputItemType);
                for (var i = 0; i < items.Count; i++)
                {
                    if (updated.Contains(items[i].Path))
                    {
                        items[i] = items[i] with
                        {
                            ReferenceOutputAssembly = referenceOutputAssembly ?? items[i].ReferenceOutputAssembly,
                            OutputItemType = outputItemType ?? items[i].OutputItemType,
                        };
                    }
                }
            }
            else if (reference.Attribute("Remove") is { } remove)
            {
                var removed = ItemPaths(remove).ToHashSet(MSBuildPaths.Comparer);
                items.RemoveAll(item => removed.Contains(item.Path));
            }
        }
    }

    private static bool IsProjectReference(XElement element) => element.Name.LocalName == "ProjectReference";

    // Null when the item does not set it, or sets it to a value that cannot be told.
    private string? Metadata(XElement item, string name)
    {
        var value = item.Elements().LastOrDefault(child => child.Name.LocalName == name && Holds(child))?.Value
            ?? item.Attribute(name)?.Value;
        return value is null ? null : _expressions.Expand(value)?.Trim();
    }

    // Item paths are relative to the project, even in an imported file.
    private IEnumerable<string> ItemPaths(XAttribute? attribute) =>
        attribute is not null && _expressions.Expand(attribute.Value) is { } value ? MSBuildPaths.Items(value, _projectDirectory) : [];

    private string ThisFileDirectory => Path.GetDirectoryName(_expressions.ThisFile)!;

    // MSBuild resolves Exists() against the importing file in an import's condition, against the project elsewhere.
    private bool Holds(XElement element, string? directory = null) =>
        element.Attribute("Condition") is not { } condition
        || MSBuildCondition.Evaluate(condition.Value, _expressions, directory ?? _projectDirectory) != false;

    private sealed record Item(string Path, string? ReferenceOutputAssembly, string? OutputItemType)
    {
        public ProjectReferenceKind Kind =>
            string.Equals(ReferenceOutputAssembly, "false", StringComparison.OrdinalIgnoreCase) ? ProjectReferenceKind.OutputNotReferenced
            : string.Equals(OutputItemType, "Analyzer", StringComparison.OrdinalIgnoreCase) ? ProjectReferenceKind.Analyzer
            : ProjectReferenceKind.Code;
    }
}

/// <summary>A <c>ProjectReference</c> item of a project file.</summary>
/// <param name="Path">The referenced project file's full path.</param>
/// <param name="Kind">What the reference brings; only <see cref="ProjectReferenceKind.Code"/> makes a direct reference.</param>
internal sealed record ProjectFileReference(string Path, ProjectReferenceKind Kind);

/// <summary>What a <c>ProjectReference</c> brings, strongest first: a file referenced twice counts as the stronger.</summary>
internal enum ProjectReferenceKind
{
    /// <summary>The referenced assembly is compiled against.</summary>
    Code,

    /// <summary>
    /// <c>OutputItemType=Analyzer</c>: the output runs in the compiler, it is not compiled against. MSBuildWorkspace still
    /// lists it, and its references transitively.
    /// </summary>
    Analyzer,

    /// <summary><c>ReferenceOutputAssembly=false</c>: only built first. MSBuildWorkspace drops it and its references.</summary>
    OutputNotReferenced,
}
