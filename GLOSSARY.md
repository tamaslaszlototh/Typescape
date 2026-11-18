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

## Views

**Dependency Graph**:
The view of what depends on what, navigable from project level down through namespace to type level.
_Avoid_: Call graph, reference map

**Type Hierarchy**:
The view of inheritance and interface implementation among all of the solution's own types (classes, interfaces, structs, records).
_Avoid_: Class hierarchy, inheritance tree

**Complexity Heatmap**:
The treemap view of the solution where area shows code size and color shows a chosen complexity metric.
_Avoid_: Hotspot map, complexity chart