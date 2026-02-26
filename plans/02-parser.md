# 02 — Mermaid Text Parsing

**See also:** [10-allocation-strategy.md](10-allocation-strategy.md) for detailed allocation patterns.

## Overview

The parser converts Mermaid source text into a typed `MermaidGraph` model. beautiful-mermaid uses line-by-line regex parsing (no grammar generator), which maps well to C# `[GeneratedRegex]` + `ReadOnlySpan<char>` patterns.

All regex patterns use `[GeneratedRegex]` — these compile to `Span<T>`-optimized search routines at build time, avoiding `Regex` and `Match` object allocations on the hot path.

## Data Model

```csharp
namespace Mermaid.Models;

// Shared direction enum
public enum Direction { TD, TB, LR, BT, RL }

// Node shapes
public enum NodeShape
{
    Rectangle, Rounded, Diamond, Stadium, Circle,
    Subroutine, DoubleCircle, Hexagon,
    Cylinder, Asymmetric, Trapezoid, TrapezoidAlt,
    StateStart, StateEnd
}

// Edge visual style
public enum EdgeStyle { Solid, Dotted, Thick }

// Parsed graph — output of the parser
public sealed record MermaidGraph
{
    public required Direction Direction { get; init; }
    public required IReadOnlyDictionary<string, MermaidNode> Nodes { get; init; }
    public required IReadOnlyList<MermaidEdge> Edges { get; init; }
    public required IReadOnlyList<MermaidSubgraph> Subgraphs { get; init; }
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ClassDefs { get; init; }
    public IReadOnlyDictionary<string, string> ClassAssignments { get; init; }
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> NodeStyles { get; init; }
}

public readonly record struct MermaidNode(string Id, string Label, NodeShape Shape);

public sealed record MermaidEdge(
    string Source,
    string Target,
    string? Label,
    EdgeStyle Style,
    bool HasArrowStart,
    bool HasArrowEnd
);

public sealed record MermaidSubgraph
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required IReadOnlyList<string> NodeIds { get; init; }
    public required IReadOnlyList<MermaidSubgraph> Children { get; init; }
    public Direction? Direction { get; init; }
}
```

## Diagram Detection (Zero-Alloc)

```csharp
internal static class DiagramDetector
{
	// Pure span comparison — no allocations
	internal static DiagramType Detect(ReadOnlySpan<char> firstLine)
	{
		var trimmed = firstLine.Trim();
		if (trimmed.StartsWith("sequenceDiagram", StringComparison.OrdinalIgnoreCase))
			return DiagramType.Sequence;
		if (trimmed.StartsWith("classDiagram", StringComparison.OrdinalIgnoreCase))
			return DiagramType.Class;
		if (trimmed.StartsWith("erDiagram", StringComparison.OrdinalIgnoreCase))
			return DiagramType.Er;
		return DiagramType.Flowchart; // includes stateDiagram-v2
	}
}
```

## Flowchart Parser — Port Strategy

The TS flowchart parser is ~575 lines. Key patterns to port.

### GeneratedRegex Patterns — Span-Optimized with 2s Timeout

Every regex pattern gets its own `[GeneratedRegex]` method. These compile at build time to optimized `ReadOnlySpan<char>` search routines. All patterns include a **2-second timeout** to guard against ReDoS — Mermaid text is user-supplied input and a pathological string must never hang the process.

The third argument to `[GeneratedRegex]` is `matchTimeoutMilliseconds`. We use a shared constant for consistency.

```csharp
internal static partial class FlowchartPatterns
{
	// 2s timeout on all patterns — guards against pathological input
	private const int TimeoutMs = 2000;

	// Header
	[GeneratedRegex(@"^(?:graph|flowchart)\s+(TD|TB|LR|BT|RL)\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	internal static partial Regex Header();

	// Arrow operators
	[GeneratedRegex(@"^(<)?(-->|-.->|==>|---|-\.-|===)(?:\|([^|]*)\|)?", RegexOptions.None, TimeoutMs)]
	internal static partial Regex Arrow();

	// Node shapes — ordered most-specific to least-specific
	[GeneratedRegex(@"^([\w-]+)\(\(\((.+?)\)\)\)", RegexOptions.None, TimeoutMs)]
	internal static partial Regex DoubleCircleNode();

	[GeneratedRegex(@"^([\w-]+)\(\[(.+?)\]\)", RegexOptions.None, TimeoutMs)]
	internal static partial Regex StadiumNode();

	[GeneratedRegex(@"^([\w-]+)\(\((.+?)\)\)", RegexOptions.None, TimeoutMs)]
	internal static partial Regex CircleNode();

	[GeneratedRegex(@"^([\w-]+)\[\[(.+?)\]\]", RegexOptions.None, TimeoutMs)]
	internal static partial Regex SubroutineNode();

	[GeneratedRegex(@"^([\w-]+)\[\((.+?)\)\]", RegexOptions.None, TimeoutMs)]
	internal static partial Regex CylinderNode();

	[GeneratedRegex(@"^([\w-]+)\[/(.+?)\\\]", RegexOptions.None, TimeoutMs)]
	internal static partial Regex TrapezoidNode();

	[GeneratedRegex(@"^([\w-]+)\[\\(.+?)/\]", RegexOptions.None, TimeoutMs)]
	internal static partial Regex TrapezoidAltNode();

	[GeneratedRegex(@"^([\w-]+)>(.+?)\]", RegexOptions.None, TimeoutMs)]
	internal static partial Regex AsymmetricNode();

	[GeneratedRegex(@"^([\w-]+)\{\{(.+?)\}\}", RegexOptions.None, TimeoutMs)]
	internal static partial Regex HexagonNode();

	[GeneratedRegex(@"^([\w-]+)\[(.+?)\]", RegexOptions.None, TimeoutMs)]
	internal static partial Regex RectangleNode();

	[GeneratedRegex(@"^([\w-]+)\((.+?)\)", RegexOptions.None, TimeoutMs)]
	internal static partial Regex RoundedNode();

	[GeneratedRegex(@"^([\w-]+)\{(.+?)\}", RegexOptions.None, TimeoutMs)]
	internal static partial Regex DiamondNode();

	// Bare node reference
	[GeneratedRegex(@"^([\w-]+)", RegexOptions.None, TimeoutMs)]
	internal static partial Regex BareNode();

	// Class shorthand
	[GeneratedRegex(@"^:::([\w][\w-]*)", RegexOptions.None, TimeoutMs)]
	internal static partial Regex ClassShorthand();

	// Statements
	[GeneratedRegex(@"^classDef\s+(\w+)\s+(.+)$", RegexOptions.None, TimeoutMs)]
	internal static partial Regex ClassDef();

	[GeneratedRegex(@"^class\s+([\w,-]+)\s+(\w+)$", RegexOptions.None, TimeoutMs)]
	internal static partial Regex ClassAssign();

	[GeneratedRegex(@"^style\s+([\w,-]+)\s+(.+)$", RegexOptions.None, TimeoutMs)]
	internal static partial Regex StyleStatement();

	[GeneratedRegex(@"^direction\s+(TD|TB|LR|BT|RL)\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	internal static partial Regex DirectionOverride();

	[GeneratedRegex(@"^subgraph\s+(.+)$", RegexOptions.None, TimeoutMs)]
	internal static partial Regex SubgraphStart();

	[GeneratedRegex(@"^([\w-]+)\s*\[(.+)\]$", RegexOptions.None, TimeoutMs)]
	internal static partial Regex SubgraphBracketLabel();
}
```

### Timeout Exception Handling

`RegexMatchTimeoutException` is caught at the parser entry point and wrapped in a descriptive exception:

```csharp
internal static MermaidGraph Parse(string text)
{
	try
	{
		return ParseCore(text.AsSpan());
	}
	catch (RegexMatchTimeoutException ex)
	{
		throw new MermaidParseException(
			$"Parsing timed out after {ex.MatchTimeout.TotalSeconds}s — input may contain pathological patterns.",
			ex);
	}
}
```
```

**Important ordering**: Multi-char delimiters (`(((`, `([`, `((`, `[[`, `[(`) must be tried before single-char (`[`, `(`, `{`). The `NodePatterns` array preserves this order.

### Line Iteration — Zero-Alloc via Span

```csharp
// NEVER: text.Split('\n') — allocates string[]
// ALWAYS: span-based line enumeration
foreach (var line in text.AsSpan().EnumerateLines())
{
	var trimmed = line.Trim();
	if (trimmed.IsEmpty || trimmed.StartsWith("%%"))
		continue;
	ProcessLine(trimmed);
}
```

### Node Shape Matching — Ordered Span-Based Dispatch

```csharp
// Ordered array of (pattern, shape) — tried in sequence
// Each GeneratedRegex.IsMatch operates on ReadOnlySpan<char>
private static readonly (Func<Regex> Pattern, NodeShape Shape)[] s_nodePatterns =
[
	(FlowchartPatterns.DoubleCircleNode, NodeShape.DoubleCircle),
	(FlowchartPatterns.StadiumNode, NodeShape.Stadium),
	(FlowchartPatterns.CircleNode, NodeShape.Circle),
	(FlowchartPatterns.SubroutineNode, NodeShape.Subroutine),
	(FlowchartPatterns.CylinderNode, NodeShape.Cylinder),
	(FlowchartPatterns.TrapezoidNode, NodeShape.Trapezoid),
	(FlowchartPatterns.TrapezoidAltNode, NodeShape.TrapezoidAlt),
	(FlowchartPatterns.AsymmetricNode, NodeShape.Asymmetric),
	(FlowchartPatterns.HexagonNode, NodeShape.Hexagon),
	(FlowchartPatterns.RectangleNode, NodeShape.Rectangle),
	(FlowchartPatterns.RoundedNode, NodeShape.Rounded),
	(FlowchartPatterns.DiamondNode, NodeShape.Diamond),
];
```

### Arrow/Edge Parsing — Consume Loop on Span

The chained edge parsing (`A --> B --> C`) uses a consume loop advancing through a `ReadOnlySpan<char>`:

```csharp
var remaining = line;  // ReadOnlySpan<char>
var prevGroupIds = ConsumeNodeGroup(ref remaining, ...);

while (!remaining.IsEmpty)
{
	if (!TryConsumeArrow(ref remaining, out var style, out var label, out var arrowStart, out var arrowEnd))
		break;

	var nextGroupIds = ConsumeNodeGroup(ref remaining, ...);
	// Emit Cartesian product of edges
}
```

### Subgraph Stack — ArrayPool

```csharp
// Rent from ArrayPool instead of allocating Stack<T>
var subgraphStack = ArrayPool<SubgraphBuilder>.Shared.Rent(16);
var stackDepth = 0;
try { /* ... */ }
finally { ArrayPool<SubgraphBuilder>.Shared.Return(subgraphStack, clearArray: true); }
```

### String Allocation Strategy

Strings are only allocated when they need to persist in the output `MermaidGraph`:

- **Node IDs** — `ToString()` once from the span, then reuse via dictionary lookup
- **Labels** — `ToString()` once, stored in `MermaidNode.Label`
- **Edge labels** — `ToString()` only when present (nullable)
- **Line iteration** — stays on `ReadOnlySpan<char>`, never allocates per-line strings
- **Regex matching** — `IsMatch(span)` is zero-alloc; only `Match()` when we need capture groups

### Pooled Collections for Builder State

```csharp
private static readonly ObjectPool<Dictionary<string, MermaidNode>> s_nodePool =
	ObjectPool.Create<Dictionary<string, MermaidNode>>();

private static readonly ObjectPool<List<MermaidEdge>> s_edgePool =
	ObjectPool.Create<List<MermaidEdge>>();
```

## State Diagram Parser

Same structure as flowchart but with different syntax:
- `state "Description" as s1`
- `s1 --> s2 : label`
- `[*]` pseudostates (unique IDs: `_start1`, `_end1`, etc.)
- `state CompositeState { ... }` nesting

Shares the `MermaidGraph` output type with flowchart parser (state diagrams map to the same graph model with `shape: Rounded` for states and `shape: StateStart/StateEnd` for pseudostates).

## Parser Interface

```csharp
namespace Mermaid.Parsing;

internal interface IDiagramParser
{
    MermaidGraph Parse(ReadOnlySpan<char> text);
}

// Concrete implementations
internal sealed class FlowchartParser : IDiagramParser { ... }
internal sealed class StateParser : IDiagramParser { ... }
```

## Performance & Allocation Strategy

See [10-allocation-strategy.md](10-allocation-strategy.md) for the comprehensive allocation playbook.

**Key rules for the parser:**

1. **`[GeneratedRegex]` for every pattern** — compiles to `Span<T>`-optimized code, `IsMatch(span)` is zero-alloc
2. **`text.AsSpan().EnumerateLines()`** — never `text.Split('\n')`
3. **Pooled builder collections** — `ObjectPool<Dictionary<>>`, `ObjectPool<List<>>` for mutable state during parsing
4. **Freeze on output** — `ToFrozenDictionary()` for the final `MermaidGraph.Nodes`
5. **Intern node IDs** — allocate each ID string once, reuse via dictionary lookup for edges
6. **`ArrayPool` for subgraph stack** — avoid `Stack<T>` allocation
7. **Span slicing over substring** — extract captures by slicing the line span, only `ToString()` when persisting

## Files to Create

| File | Lines (est.) | Complexity |
|---|---|---|
| `Models/MermaidGraph.cs` | ~80 | Low — record definitions |
| `Parsing/DiagramDetector.cs` | ~20 | Low |
| `Parsing/FlowchartParser.cs` | ~400 | Medium — regex + consume loop |
| `Parsing/StateParser.cs` | ~200 | Medium |
| `Text/MultilineUtils.cs` | ~60 | Low — `<br>` normalization |

## Porting Checklist (Flowchart Parser)

- [ ] Header parsing (`graph TD` / `flowchart LR`)
- [ ] `classDef` statements
- [ ] `class` assignments
- [ ] `style` statements
- [ ] `direction` override inside subgraphs
- [ ] `subgraph` start/end with nesting
- [ ] Node shape detection (all 14 shapes)
- [ ] Arrow operator parsing (all variants + bidirectional)
- [ ] Edge label parsing (`-->|label|`)
- [ ] Chained edges (`A --> B --> C`)
- [ ] `&` parallel links (`A & B --> C & D`)
- [ ] `:::className` shorthand
- [ ] State diagram detection and routing to StateParser
