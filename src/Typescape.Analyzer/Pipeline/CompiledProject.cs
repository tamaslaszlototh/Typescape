using Microsoft.CodeAnalysis;
using Typescape.Analyzer.Extraction;
using Typescape.Snapshot.Records;

namespace Typescape.Analyzer.Pipeline;

/// <summary>
/// The output of Compile for one project. The pipeline holds every one of them until Assemble has finished: no
/// compilation is released during the run, so Diagnostics and Walk reuse what Compile bound.
/// </summary>
/// <param name="Project">The project.</param>
/// <param name="Compilation">Its compilation, with source generators run.</param>
/// <param name="Context">What extraction needs to know about the project.</param>
internal sealed record CompiledProject(LoadedProject Project, Compilation Compilation, ProjectContext Context);

/// <summary>The output of Diagnostics for one project: its compile evidence for Load Health.</summary>
/// <param name="CompileErrors">The compilation's error statistics, messages relative to the solution directory.</param>
/// <param name="ObjectResolves">Whether <c>System.Object</c> resolves in the compilation.</param>
internal sealed record ProjectDiagnostics(CompileErrorsRecord CompileErrors, bool ObjectResolves);
