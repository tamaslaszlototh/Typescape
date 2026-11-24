using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Typescape.Snapshot.Records;

namespace Typescape.Analyzer.Loading;

/// <summary>Tells an SDK-style project file from a classic one.</summary>
internal static partial class ProjectStyles
{
    /// <summary>SDK-style when the file names an SDK: <c>&lt;Project Sdk&gt;</c>, an <c>&lt;Sdk&gt;</c> element or <c>&lt;Import Sdk&gt;</c>.</summary>
    /// <returns><c>null</c> when the file cannot be read.</returns>
    public static ProjectStyle? Of(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        try
        {
            return XDocument.Parse(text).Root is { } root && IsSdk(root) ? ProjectStyle.Sdk : ProjectStyle.Legacy;
        }
        catch (XmlException)
        {
            // MSBuildWorkspace still opens a malformed file, so it gets a style too.
            return SdkMarkup().IsMatch(text) ? ProjectStyle.Sdk : ProjectStyle.Legacy;
        }
    }

    /// <param name="project">The project file's root element.</param>
    public static bool IsSdk(XElement project) =>
        project.Attribute("Sdk") is not null
        || project.Elements().Any(element =>
            element.Name.LocalName == "Sdk" || (element.Name.LocalName == "Import" && element.Attribute("Sdk") is not null));

    [GeneratedRegex(@"<(?:Project|Import)\s[^>]*\bSdk\s*=|<Sdk[\s/>]")]
    private static partial Regex SdkMarkup();
}
