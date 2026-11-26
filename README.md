# Typescape

[![ci](https://github.com/tamaslaszlototh/Typescape/actions/workflows/ci.yml/badge.svg)](https://github.com/tamaslaszlototh/Typescape/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Typescape)](https://www.nuget.org/packages/Typescape)

See the structure of a C# solution in your browser: what depends on what, how its types inherit, and where its complex code is.

<!-- TODO before 0.1.0: screenshot of the three views (absolute raw.githubusercontent.com URL, so it also renders on nuget.org) -->

## Requirements

- The **.NET SDK 10** or later. The runtime alone is not enough: Typescape loads projects with the SDK's MSBuild.
- **macOS or Windows.** Linux is not supported.

## Install

```
dotnet tool install -g Typescape
```

## Quick start

```
typescape path/to/MySolution.sln
```

Typescape restores and analyzes the solution, writes `MySolution.typescape.json.gz` to the current directory, and opens it in your default browser. Run `typescape` with no argument in a directory that holds exactly one `.sln` or `.slnx`. To reopen a Snapshot later without analyzing again:

```
typescape view MySolution.typescape.json.gz
```

## The three views

All three views share one Selection (a project, namespace, type or member) and the same global filters: Dependency kinds, test projects, generated code and external assemblies.

**Dependency Graph.** What depends on what. The Overview starts with the whole solution at project level; expand projects and namespaces in place down to types. Focus centres on one project, namespace or type, with what uses it on one side and what it uses on the other. Every Dependency has a kind (inherits, implements, member-type, signature, new, call, attribute, generic-arg, member-access, type-usage) and expands to the places in the code where it occurs: the using member, the used member, file and line.

**Type Hierarchy.** Inheritance and interface implementation among the solution's own classes, interfaces, structs and records. By default it shows the Selection's lineage: its ancestors and all of its descendants. Types defined outside the solution appear as collapsed stubs, and many leaf types with the same bases are packed into one block, so wide hierarchies stay readable.

**Complexity Heatmap.** A treemap of the solution, from projects down to methods. Area is lines of code; colour is the share of code in members above a cognitive complexity threshold (15 by default). You can switch to cyclomatic complexity, other aggregations (worst member, density, total), another threshold and colourblind-safe palettes.

## Command line

| Command | Does |
|---|---|
| `typescape [<path>]` | Analyzes a solution (or the one in a directory), then opens it; a `.gz` file is only opened. |
| `typescape analyze [<path>]` | Analyzes and writes the Snapshot, without opening it. |
| `typescape view <snapshot>` | Opens a Snapshot in the browser. |
| `typescape --version` | Prints the tool version, the Snapshot schema version and the rules version. |

| Option | Meaning |
|---|---|
| `-o, --output <path>` | Where to write the Snapshot. |
| `-f, --framework <tfm>` | Which target framework to analyze in multi-targeting projects (default: the highest). |
| `-p, --property <Name=Value>` | An MSBuild property, e.g. `-p:Configuration=Release`. Repeatable. |
| `--no-restore` | Skip the package restore. |
| `--fail-on none\|degraded\|failed` | Exit with code 3 when any project's Load Health is this or worse. |
| `--no-browser` | Print the Viewer's URL instead of opening the browser. |

`typescape --help` and `typescape <command> --help` list every option.

## What a Snapshot is

A Snapshot is one gzip-compressed JSON file of facts about the code at one point in time: projects, namespaces, types and members with their names, lines of code and complexity, the Dependencies between them with file and line, how well each project loaded, and how the Snapshot was made (tool and SDK version, OS, git commit). **It contains no source code**, no absolute paths and no machine name, so it can be shared without the repository. The same code always gives the same Snapshot, apart from the part that records how it was made.

Typescape sends no telemetry and checks for no updates. Its only network traffic is NuGet, to your own configured package sources.

## Legacy projects and Load Health

Typescape analyzes SDK-style and classic (non-SDK-style) projects alike, with or without Visual Studio installed, on macOS too. It restores `packages.config` packages and fetches the .NET Framework reference assemblies it needs from your NuGet sources, so a classic project loads the same way on every machine.

A project can load incompletely without an error: missing packages, unresolved references, compile errors. Typescape checks every project and gives it a Load Health verdict: **ok**, **degraded** (it loaded, but the analysis may be incomplete) or **failed** (it could not be loaded). The terminal lists every project that is not ok with its main reason, and the Viewer shows the full evidence per project. Use `--fail-on` to fail a CI run on it.

## Known limits

- Solutions up to about 1 million lines (about 150 projects) on a 16 GB machine.
- The input is a solution (`.sln` or `.slnx`); a single `.csproj` is not supported.
- Multi-targeting projects are analyzed for one target framework per run.

## Licence

MIT, see [LICENSE](https://github.com/tamaslaszlototh/Typescape/blob/main/LICENSE). The licences of the bundled third-party components are in [THIRD-PARTY-NOTICES.txt](https://github.com/tamaslaszlototh/Typescape/blob/main/THIRD-PARTY-NOTICES.txt).
