# 10 — Allocation Strategy & Performance

Allocations are a first-class concern. Every component should be designed to minimize heap allocations using modern .NET primitives.

## Core Principles

1. **`ReadOnlySpan<char>` is the default** for any string processing that doesn't need to persist
2. **`[GeneratedRegex]` everywhere with 2s timeout** — these compile to `Span<T>`-optimized search routines, avoiding `Match` object allocations on the hot path; every pattern includes `matchTimeoutMilliseconds: 2000` to guard against ReDoS from user-supplied input
3. **Pool and reuse** — `StringBuilder`, arrays, and collections via `ObjectPool<T>` and `ArrayPool<T>`
4. **`stackalloc`** for small fixed-size buffers
5. **Avoid LINQ in hot paths** — use `foreach` loops with spans instead
6. **Freeze immutable collections at startup** — `FrozenDictionary`, `FrozenSet` for O(1) lookup tables

## GeneratedRegex — The Foundation

`[GeneratedRegex]` in .NET 8+ compiles regex patterns to source-generated methods that operate directly on `ReadOnlySpan<char>`. This avoids:

- `Regex` object allocation
- `Match` / `Group` / `Capture` allocations when using `IsMatch` or `EnumerateMatches`
- String substring allocations when combined with span slicing

### Pattern: Span-Based Regex Matching with Timeout

All `[GeneratedRegex]` patterns include a 2-second timeout to guard against ReDoS (Regular Expression Denial of Service). Mermaid diagrams are user-supplied input — a crafted pathological string should never hang the process.

```csharp
internal static partial class FlowchartPatterns
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^(?:graph|flowchart)\s+(TD|TB|LR|BT|RL)\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	internal static partial Regex Header();

	[GeneratedRegex(@"^(<)?(-->|-.->|==>|---|-\.-|===)(?:\|([^|]*)\|)?", RegexOptions.None, TimeoutMs)]
	internal static partial Regex Arrow();

	[GeneratedRegex(@"^([\w-]+)\(\(\((.+?)\)\)\)", RegexOptions.None, TimeoutMs)]
	internal static partial Regex DoubleCircleNode();

	[GeneratedRegex(@"^([\w-]+)\(\[(.+?)\]\)", RegexOptions.None, TimeoutMs)]
	internal static partial Regex StadiumNode();

	// ... all patterns use the same TimeoutMs constant
}
```

The timeout throws `RegexMatchTimeoutException` — the parser should catch this and throw a descriptive `MermaidParseException`.
```

### Pattern: EnumerateMatches to Avoid Match Allocations

For cases where we need match data but want zero allocations:

```csharp
// AVOID — allocates Match object
var match = FlowchartPatterns.Arrow().Match(line);
if (match.Success) { /* use match.Groups */ }

// PREFER — zero-allocation enumeration on span
foreach (var valueMatch in FlowchartPatterns.Arrow().EnumerateMatches(line.AsSpan()))
{
	// valueMatch is a ValueMatch (struct) — no heap allocation
	var captured = line.AsSpan().Slice(valueMatch.Index, valueMatch.Length);
	// ...
}
```

### Pattern: IsMatch + Manual Span Slicing

When the regex is just a guard and we extract data manually:

```csharp
// Use IsMatch (zero-alloc) then slice the span directly
var span = line.AsSpan();
if (FlowchartPatterns.Header().IsMatch(span))
{
	// Extract direction by finding the whitespace boundary
	var dirStart = span.LastIndexOf(' ') + 1;
	var direction = ParseDirection(span[dirStart..].Trim());
}
```

## Component-by-Component Strategy

### Parser (`Mermaid.Parsing`)

The parser is the most allocation-sensitive component — it processes every line of input.

**Line iteration — zero-alloc:**
```csharp
// AVOID — allocates string[] array + per-line strings
var lines = text.Split('\n');

// PREFER — zero-allocation line enumeration
foreach (var line in text.AsSpan().EnumerateLines())
{
	var trimmed = line.Trim();
	if (trimmed.IsEmpty || trimmed.StartsWith("%%"))
		continue;
	ProcessLine(trimmed);
}
```

**Node registration — pooled dictionaries:**
```csharp
// Use ObjectPool for the mutable builder dictionaries
private static readonly ObjectPool<Dictionary<string, MermaidNode>> s_nodePool =
	ObjectPool.Create<Dictionary<string, MermaidNode>>();

internal static MermaidGraph Parse(ReadOnlySpan<char> text)
{
	var nodes = s_nodePool.Get();
	try
	{
		// ... parse into nodes ...
		// Freeze into immutable dictionary on output
		return new MermaidGraph { Nodes = nodes.ToFrozenDictionary(), ... };
	}
	finally
	{
		nodes.Clear();
		s_nodePool.Return(nodes);
	}
}
```

**Subgraph stack — rent from ArrayPool:**
```csharp
// For small stacks, use stackalloc or ArrayPool instead of Stack<T>
var subgraphStack = ArrayPool<SubgraphBuilder>.Shared.Rent(16); // max nesting depth
var stackDepth = 0;
try
{
	// ... push/pop via stackDepth index ...
}
finally
{
	ArrayPool<SubgraphBuilder>.Shared.Return(subgraphStack, clearArray: true);
}
```

**String interning for node IDs:**
Node IDs appear repeatedly in edges. Intern them to avoid duplicate allocations:

```csharp
// Node IDs referenced by edges should be the same string instance
var id = nodeIdSpan.ToString(); // allocate once
nodes[id] = new MermaidNode(id, label, shape);
// Edge references reuse the same id string from the nodes dictionary
edges.Add(new MermaidEdge(Source: nodes[sourceSpan.ToString()].Id, ...));
```

**Edge list — pooled and pre-sized:**
```csharp
private static readonly ObjectPool<List<MermaidEdge>> s_edgePool =
	ObjectPool.Create<List<MermaidEdge>>();
```

### Text Metrics (`Mermaid.Text`)

**Span-based character iteration:**
```csharp
internal static double MeasureTextWidth(ReadOnlySpan<char> text, double fontSize, int fontWeight)
{
	var baseRatio = fontWeight >= 600 ? 0.60 : fontWeight >= 500 ? 0.57 : 0.54;
	var totalWidth = 0.0;

	// Direct span iteration — no enumerator allocation
	for (var i = 0; i < text.Length; i++)
		totalWidth += CharWidths.GetCharWidth(text[i]);

	return totalWidth * fontSize * baseRatio + fontSize * 0.15;
}
```

**Strip formatting tags without allocation:**
```csharp
[GeneratedRegex(@"</?(?:b|strong|i|em|u|s|del)\s*>", RegexOptions.IgnoreCase)]
private static partial Regex FormattingTags();

// When we just need the width, count tag characters and subtract
// rather than allocating a new stripped string
internal static double MeasurePlainWidth(ReadOnlySpan<char> text, double fontSize, int fontWeight)
{
	var totalWidth = 0.0;
	var inTag = false;

	for (var i = 0; i < text.Length; i++)
	{
		if (text[i] == '<') { inTag = true; continue; }
		if (text[i] == '>') { inTag = false; continue; }
		if (!inTag)
			totalWidth += CharWidths.GetCharWidth(text[i]);
	}

	var baseRatio = fontWeight >= 600 ? 0.60 : fontWeight >= 500 ? 0.57 : 0.54;
	return totalWidth * fontSize * baseRatio + fontSize * 0.15;
}
```

**Multiline measurement — avoid Split allocation:**
```csharp
internal static MultilineMetrics MeasureMultiline(ReadOnlySpan<char> text, double fontSize, int fontWeight)
{
	var lineHeight = fontSize * LineHeightRatio;
	var maxWidth = 0.0;
	var lineCount = 0;

	// Enumerate lines on span — no string[] allocation
	foreach (var line in text.EnumerateLines())
	{
		lineCount++;
		var w = MeasurePlainWidth(line, fontSize, fontWeight);
		if (w > maxWidth) maxWidth = w;
	}

	// Only allocate string[] when we actually need the lines for rendering
	return new MultilineMetrics(maxWidth, lineCount * lineHeight, lineCount, lineHeight);
}
```

### SVG Renderer (`Mermaid.Rendering`)

**StringBuilder pooling:**
```csharp
private static readonly ObjectPool<StringBuilder> s_sbPool =
	new DefaultObjectPoolProvider().CreateStringBuilderPool(
		initialCapacity: 4096,
		maximumRetainedCapacity: 64 * 1024);

internal static string Render(PositionedGraph graph, DiagramColors colors, string font, bool transparent)
{
	var sb = s_sbPool.Get();
	try
	{
		RenderInto(sb, graph, colors, font, transparent);
		return sb.ToString();
	}
	finally
	{
		sb.Clear();
		s_sbPool.Return(sb);
	}
}
```

**Avoid string interpolation in hot loops — use Append chains:**
```csharp
// AVOID — each $"..." allocates a temporary string
sb.Append($"""<rect x="{x}" y="{y}" width="{w}" height="{h}" />""");

// PREFER — direct appends, no intermediate string
sb.Append("<rect x=\"");
sb.Append(x);
sb.Append("\" y=\"");
sb.Append(y);
sb.Append("\" width=\"");
sb.Append(w);
sb.Append("\" height=\"");
sb.Append(h);
sb.Append("\" />");
```

**Or use `string.Create` with `ISpanFormattable`:**
```csharp
// For number formatting without allocation
sb.Append("<rect x=\"");
sb.AppendSpanFormattable(x); // uses ISpanFormattable, formats into sb's buffer
```

**Points array for polylines — stackalloc when small:**
```csharp
internal static void AppendPoints(StringBuilder sb, IReadOnlyList<Point> points)
{
	for (var i = 0; i < points.Count; i++)
	{
		if (i > 0) sb.Append(' ');
		sb.Append(points[i].X);
		sb.Append(',');
		sb.Append(points[i].Y);
	}
}
```

**Escape functions — span-based fast path:**
```csharp
internal static void AppendEscapedAttr(StringBuilder sb, ReadOnlySpan<char> value)
{
	// Fast path: no special chars → append directly
	if (!value.ContainsAny("&\"<>"))
	{
		sb.Append(value);
		return;
	}

	// Slow path: escape character by character
	foreach (var c in value)
	{
		switch (c)
		{
			case '&': sb.Append("&amp;"); break;
			case '"': sb.Append("&quot;"); break;
			case '<': sb.Append("&lt;"); break;
			case '>': sb.Append("&gt;"); break;
			default: sb.Append(c); break;
		}
	}
}
```

### Layout Engine (`Mermaid.Layout`)

**Point collections — use ArrayPool for temporary buffers:**
```csharp
internal static Point[] OrthogonalizeEdgePoints(ReadOnlySpan<Point> points)
{
	// Rent a buffer for the result (max 3x input due to added bend points)
	var buffer = ArrayPool<Point>.Shared.Rent(points.Length * 3);
	try
	{
		var count = 0;
		buffer[count++] = points[0];

		for (var i = 1; i < points.Length; i++)
		{
			// ... add orthogonal bend points ...
			buffer[count++] = points[i];
		}

		// Return exact-sized array (this is the one allocation we accept)
		return buffer.AsSpan(0, count).ToArray();
	}
	finally
	{
		ArrayPool<Point>.Shared.Return(buffer);
	}
}
```

**Edge bundling — avoid LINQ, use direct iteration:**
```csharp
// AVOID
var fanOutGroups = edges.GroupBy(e => e.Source).Where(g => g.Count() >= 2);

// PREFER — single pass with pooled dictionary
var fanOut = s_edgeGroupPool.Get();
try
{
	foreach (var edge in edges)
	{
		if (edge.Source == edge.Target) continue;
		if (!fanOut.TryGetValue(edge.Source, out var list))
		{
			list = s_edgeListPool.Get();
			fanOut[edge.Source] = list;
		}
		list.Add(edge);
	}
	// ... process groups ...
}
finally
{
	foreach (var list in fanOut.Values)
	{
		list.Clear();
		s_edgeListPool.Return(list);
	}
	fanOut.Clear();
	s_edgeGroupPool.Return(fanOut);
}
```

### Theming (`Mermaid.Theming`)

**Frozen collections for lookup tables:**
```csharp
public static readonly FrozenDictionary<string, DiagramColors> BuiltIn =
	new Dictionary<string, DiagramColors>
	{
		["zinc-light"] = new() { Bg = "#FFFFFF", Fg = "#27272A" },
		// ...
	}.ToFrozenDictionary();
```

**Style block — cached since it depends only on font name:**
```csharp
private static readonly ConcurrentDictionary<(string Font, bool Mono), string> s_styleCache = new();

internal static string Build(string font, bool hasMonoFont) =>
	s_styleCache.GetOrAdd((font, hasMonoFont), static key => BuildCore(key.Font, key.Mono));
```

## Data Model — Struct vs Class Decisions

| Type | Kind | Rationale |
|---|---|---|
| `Point` | `readonly record struct` | 16 bytes, passed by value, no GC pressure |
| `MermaidNode` | `readonly record struct` | Small, frequently created during parsing |
| `MermaidEdge` | `sealed record` | Has nullable `Label` field, more fields |
| `PositionedNode` | `sealed record` | Many fields, nullable `InlineStyle` |
| `PositionedEdge` | `sealed record` | Contains `IReadOnlyList<Point>` |
| `MultilineMetrics` | `readonly record struct` | Small measurement result, returned by value |
| `DiagramColors` | `sealed record` | User-facing, nullable optional fields |

## Allocation Budget per Render Call

Target for a simple 5-node flowchart:

| Component | Expected Allocations |
|---|---|
| Parser | ~5 string allocations (node IDs), 1 frozen dict, 1 edge list |
| Text metrics | 0 (span-based) |
| Layout | MSAGL internal (unavoidable), 1 PositionedGraph |
| SVG render | 1 pooled StringBuilder, 1 final string |
| **Total controllable** | **~10-15 objects** |

## Benchmarking Strategy

Track allocations explicitly with `[MemoryDiagnoser]`:

```csharp
[MemoryDiagnoser]
[ShortRunJob]
public class AllocationBenchmarks
{
	private const string SimpleFlow = "graph TD\n  A --> B --> C";

	[Benchmark]
	public string RenderSimple() => MermaidRenderer.RenderSvg(SimpleFlow);

	[Benchmark]
	public MermaidGraph ParseSimple() => MermaidRenderer.Parse(SimpleFlow);
}
```

Monitor `Gen0`, `Gen1`, `Allocated` columns. Goal: parsing a simple diagram should allocate < 1 KB.

## Anti-Patterns to Avoid

| Anti-Pattern | Replacement |
|---|---|
| `text.Split('\n')` | `text.AsSpan().EnumerateLines()` |
| `regex.Match(str).Groups[1].Value` | `GeneratedRegex` + `EnumerateMatches` + span slicing |
| `string.Format(...)` in loops | `sb.Append()` chains |
| `$"..."` interpolation in hot paths | `sb.Append()` chains |
| `new List<T>()` in hot paths | `ObjectPool<List<T>>` |
| `new StringBuilder()` per render | `ObjectPool<StringBuilder>` |
| `points.Select(p => ...).ToArray()` | `for` loop with rented array |
| `edges.GroupBy(...)` | Manual grouping with pooled dictionary |
| `text.Contains("x")` | `text.AsSpan().Contains('x')` |
| `new Dictionary<K,V>()` for static data | `FrozenDictionary<K,V>` |
| `[GeneratedRegex(...)]` without timeout | Always pass `matchTimeoutMilliseconds: 2000` |
| `new Regex(...)` at runtime | Always `[GeneratedRegex]` — source-generated + span-optimized |

## Security: Regex Timeout

Mermaid text is user-supplied input. Crafted pathological strings can cause catastrophic backtracking in regex engines. **Every `[GeneratedRegex]` must include a 2-second timeout.**

```csharp
// The third parameter is matchTimeoutMilliseconds
[GeneratedRegex(@"pattern", RegexOptions.None, 2000)]
internal static partial Regex MyPattern();
```

Use a shared constant for the timeout value:

```csharp
private const int TimeoutMs = 2000;
```

At the parser entry point, catch `RegexMatchTimeoutException` and wrap it:

```csharp
catch (RegexMatchTimeoutException ex)
{
	throw new MermaidParseException(
		$"Parsing timed out after {ex.MatchTimeout.TotalSeconds}s — input may contain pathological patterns.",
		ex);
}
```

This ensures:
- No single regex match can take longer than 2 seconds
- The caller gets a clear, descriptive exception instead of a hang
- The timeout is generous enough that no legitimate diagram would ever hit it (typical parse: < 10ms)
