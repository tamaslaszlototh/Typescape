using System.Text;

namespace Typescape.Analyzer.MSBuild;

/// <summary>
/// Best-effort MSBuild properties and <c>$(…)</c> expansion for <see cref="ProjectFileReader"/>. <c>null</c> stands for
/// a value it cannot tell: an item or metadata reference, an unsupported property function, or a property set in a file
/// it does not read.
/// </summary>
internal sealed class MSBuildExpressions(string projectPath)
{
    private readonly Dictionary<string, string?> _properties = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _global = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _projectDirectory = Path.GetDirectoryName(projectPath)!;

    /// <summary>The file being evaluated, which the <c>MSBuildThisFile*</c> properties describe.</summary>
    public string ThisFile { get; set; } = projectPath;

    /// <summary>Sets a property unless a global property of that name exists, as MSBuild does.</summary>
    public void Set(string name, string? value)
    {
        if (!_global.Contains(name))
        {
            _properties[name] = value;
        }
    }

    public void SetGlobal(string name, string value)
    {
        _global.Add(name);
        _properties[name] = value;
    }

    /// <summary>The property's value: empty when it is not set, <c>null</c> when it cannot be told.</summary>
    public string? Value(string name) => name.ToUpperInvariant() switch
    {
        "MSBUILDTHISFILE" => Path.GetFileName(ThisFile),
        "MSBUILDTHISFILENAME" => Path.GetFileNameWithoutExtension(ThisFile),
        "MSBUILDTHISFILEEXTENSION" => Path.GetExtension(ThisFile),
        "MSBUILDTHISFILEFULLPATH" => ThisFile,
        "MSBUILDTHISFILEDIRECTORY" => MSBuildPaths.EnsureTrailingSlash(Path.GetDirectoryName(ThisFile)!),
        _ => _properties.TryGetValue(name, out var value) ? value : "",
    };

    /// <summary>Expands <c>$(…)</c>; <c>null</c> when any part of <paramref name="text"/> cannot be told.</summary>
    public string? Expand(string text)
    {
        var result = new StringBuilder();
        for (var i = 0; i < text.Length;)
        {
            if (i + 1 < text.Length && text[i + 1] == '(' && text[i] is '@' or '%')
            {
                return null;
            }

            if (i + 1 < text.Length && text[i] == '$' && text[i + 1] == '(')
            {
                var end = Closing(text, i + 1);
                if (end < 0 || Property(text[(i + 2)..end]) is not { } value)
                {
                    return null;
                }

                result.Append(value);
                i = end + 1;
            }
            else
            {
                result.Append(text[i++]);
            }
        }

        return result.ToString();
    }

    /// <summary>The index of the parenthesis that closes the one at <paramref name="open"/>, or -1.</summary>
    public static int Closing(string text, int open) =>
        Unquoted(text, open).Where(at => text[at.Index] == ')' && at.Depth == 0).Select(at => at.Index).DefaultIfEmpty(-1).First();

    private string? Property(string body)
    {
        body = body.Trim();
        var i = 0;
        string? value;
        if (body.StartsWith('['))
        {
            var typeEnd = body.IndexOf(']', StringComparison.Ordinal);
            if (typeEnd < 0 || !body.AsSpan(typeEnd + 1).StartsWith("::"))
            {
                return null;
            }

            i = typeEnd + 3;
            if (Call(body, ref i) is not var (method, arguments))
            {
                return null;
            }

            value = StaticFunction(body[1..typeEnd].Trim(), method, arguments);
        }
        else
        {
            var start = i;
            while (i < body.Length && (char.IsLetterOrDigit(body[i]) || body[i] is '_' or '-'))
            {
                i++;
            }

            value = i == start ? null : Value(body[start..i]);
        }

        while (value is not null && i < body.Length)
        {
            if (body[i] != '.')
            {
                return null;
            }

            i++;
            if (Call(body, ref i) is not var (method, arguments))
            {
                return null;
            }

            value = StringMethod(value, method, arguments);
        }

        return value;
    }

    private (string Method, string[] Arguments)? Call(string body, ref int i)
    {
        var open = body.IndexOf('(', i);
        var close = open < 0 ? -1 : Closing(body, open);
        if (close < 0)
        {
            return null;
        }

        var method = body[i..open].Trim();
        var arguments = new List<string>();
        foreach (var argument in SplitArguments(body[(open + 1)..close]))
        {
            var text = argument.Trim();
            if (text.Length >= 2 && text[0] is '\'' or '"' or '`' && text[^1] == text[0])
            {
                text = text[1..^1];
            }

            if (Expand(text) is not { } expanded)
            {
                return null;
            }

            arguments.Add(expanded);
        }

        i = close + 1;
        return (method, [.. arguments]);
    }

    private string? StaticFunction(string type, string method, string[] arguments)
    {
        if (string.Equals(type, "MSBuild", StringComparison.OrdinalIgnoreCase))
        {
            return method.ToUpperInvariant() switch
            {
                "GETDIRECTORYNAMEOFFILEABOVE" => MSBuildPaths.Full(At(arguments, 0), _projectDirectory) is { } start
                    ? Path.GetDirectoryName(MSBuildPaths.FileAbove(start, At(arguments, 1))) ?? ""
                    : null,
                "GETPATHOFFILEABOVE" => (arguments.Length > 1 ? MSBuildPaths.Full(At(arguments, 1), _projectDirectory) : Path.GetDirectoryName(ThisFile)) is { } start
                    ? MSBuildPaths.FileAbove(start, At(arguments, 0)) ?? ""
                    : null,
                "NORMALIZEDIRECTORY" => CombinedPath(arguments) is { } directory ? MSBuildPaths.EnsureTrailingSlash(directory) : null,
                "NORMALIZEPATH" => CombinedPath(arguments),
                "ENSURETRAILINGSLASH" => At(arguments, 0).Length == 0 ? "" : MSBuildPaths.EnsureTrailingSlash(At(arguments, 0)),
                "VALUEORDEFAULT" => At(arguments, 0).Length > 0 ? At(arguments, 0) : At(arguments, 1),
                "GETTARGETFRAMEWORKIDENTIFIER" => TargetFrameworks.FrameworkIdentifier(At(arguments, 0)),
                "GETTARGETFRAMEWORKVERSION" => TargetFrameworks.FrameworkVersion(At(arguments, 0)),
                "ISTARGETFRAMEWORKCOMPATIBLE" => TargetFrameworks.IsCompatible(At(arguments, 0), At(arguments, 1)) is { } compatible
                    ? Boolean(compatible)
                    : null,
                _ => null,
            };
        }

        if (string.Equals(type, "System.IO.Path", StringComparison.OrdinalIgnoreCase))
        {
            return method.ToUpperInvariant() switch
            {
                "COMBINE" => Path.Combine([.. arguments.Select(MSBuildPaths.Normalize)]),
                "GETFULLPATH" => MSBuildPaths.Full(At(arguments, 0), _projectDirectory),
                "GETDIRECTORYNAME" => Path.GetDirectoryName(MSBuildPaths.Normalize(At(arguments, 0))) ?? "",
                _ => null,
            };
        }

        return null;
    }

    private static string? StringMethod(string value, string method, string[] arguments) => method.ToUpperInvariant() switch
    {
        "STARTSWITH" => Boolean(value.StartsWith(At(arguments, 0), StringComparison.Ordinal)),
        "ENDSWITH" => Boolean(value.EndsWith(At(arguments, 0), StringComparison.Ordinal)),
        "CONTAINS" => Boolean(value.Contains(At(arguments, 0), StringComparison.Ordinal)),
        "REPLACE" when arguments.Length == 2 && At(arguments, 0).Length > 0 => value.Replace(At(arguments, 0), At(arguments, 1), StringComparison.Ordinal),
        _ => null,
    };

    // MSBuild passes a missing argument as empty.
    private static string At(string[] arguments, int index) => index < arguments.Length ? arguments[index] : "";

    private string? CombinedPath(string[] parts) =>
        MSBuildPaths.Full(Path.Combine([.. parts.Select(MSBuildPaths.Normalize)]), _projectDirectory);

    private static string Boolean(bool value) => value ? "True" : "False";

    private static IEnumerable<string> SplitArguments(string text)
    {
        if (text.Trim().Length == 0)
        {
            yield break;
        }

        var start = 0;
        foreach (var (index, _) in Unquoted(text, 0).Where(at => text[at.Index] == ',' && at.Depth == 0))
        {
            yield return text[start..index];
            start = index + 1;
        }

        yield return text[start..];
    }

    // Each character outside quotes, with the parenthesis depth after it: MSBuild's quotes hide parentheses and commas.
    private static IEnumerable<(int Index, int Depth)> Unquoted(string text, int start)
    {
        var depth = 0;
        char? quote = null;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (quote is not null)
            {
                quote = c == quote ? null : quote;
            }
            else if (c is '\'' or '"' or '`')
            {
                quote = c;
            }
            else
            {
                depth += c == '(' ? 1 : c == ')' ? -1 : 0;
                yield return (i, depth);
            }
        }
    }
}
