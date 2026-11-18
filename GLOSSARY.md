# Typescape

Typescape analyzes C# solutions with Roslyn and shows their structure as three views: the dependency graph, the type hierarchy, and the complexity heatmap.

## Analysis

**Analyzer**:
The part of Typescape that loads a solution with its full semantic model and produces a Snapshot.
_Avoid_: Scanner, parser

**Snapshot**:
The complete, self-contained result of analyzing one solution at one point in time; the Viewer reads only this.
_Avoid_: Report, dump, cache

**Viewer**:
The part of Typescape that presents a Snapshot as the three views, without needing Roslyn or the original source.
_Avoid_: UI, frontend, dashboard

**External Type**:
A type referenced by the analyzed solution but defined outside it (framework or package); shown only as a collapsed stub.
_Avoid_: Library type, foreign type

**Executable Member**:
A member of a type that has an executable body (method, constructor, destructor, operator, conversion, or a single property/indexer/event accessor) and is the unit metrics are measured on; lambdas and local functions belong to their containing Executable Member.
_Avoid_: Method, function, routine, member body

**Dependency**:
A directed, typed link from one of the solution's own types to another type, own or External, that it uses; the Dependency Graph shows Dependencies aggregated by project or namespace.
_Avoid_: Reference, edge, link

**Dependency Kind**:
The way one type uses another: inherits, implements, member-type, signature, new, call, attribute, generic-arg, member-access, or type-usage.
_Avoid_: Edge type, relation type

**Occurrence**:
One place in the source (file and line, with the using Executable Member and the used member where there is one) where a Dependency appears.
_Avoid_: Usage, reference site, hit

**Load Health**:
The Analyzer's own verdict on how completely one project was loaded (ok, degraded, or failed), with the evidence behind it.
_Avoid_: Load status, project errors

## Views

**Dependency Graph**:
The view of what depends on what, navigable from project level down through namespace to type level.
_Avoid_: Call graph, reference map

**Overview**:
The Dependency Graph's starting mode: the whole solution at project level, where the user expands projects and namespaces in place.
_Avoid_: Solution view, map view

**Focus**:
The Dependency Graph's mode centred on one project, namespace or type, showing what uses it on one side and what it uses on the other.
_Avoid_: Ego graph, neighbourhood view

**Type Hierarchy**:
The view of inheritance and interface implementation among all of the solution's own types (classes, interfaces, structs, records).
_Avoid_: Class hierarchy, inheritance tree

**Complexity Heatmap**:
The treemap view of the solution where area shows code size and color shows a chosen complexity metric.
_Avoid_: Hotspot map, complexity chart

**Selection**:
The one project, namespace, type, or Executable Member the user has picked, shared by all three views; each view reveals it, changing what it shows only when the Selection is not already visible.
_Avoid_: Focus (that is a Dependency Graph mode), current item, active node