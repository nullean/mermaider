# 04 — SVG Renderer

**See also:** [10-allocation-strategy.md](10-allocation-strategy.md) for allocation patterns.

## Overview

The SVG renderer takes a `PositionedGraph` and produces a self-contained SVG string. It is pure string concatenation — no DOM, no XML library. beautiful-mermaid's renderer is ~560 lines and maps directly to pooled `StringBuilder` operations.

The renderer uses `ObjectPool<StringBuilder>` to avoid allocating a new builder per render call. The only unavoidable allocation is the final `sb.ToString()` for the result.

## Rendering Order (Back-to-Front)

SVG renders in document order (later elements on top), so we render:

1. `<svg>` root with CSS variables as inline style
2. `<style>` block with font imports + derived CSS variable rules
3. `<defs>` with arrow marker definitions
4. Subgraph backgrounds (group rectangles + header bands)
5. Edges (polylines — behind nodes)
6. Edge labels (with background pills)
7. Nodes (shape + label text)
8. `</svg>`

## SVG Root — Pooled StringBuilder

```csharp
internal static class SvgRenderer
{
	private static readonly ObjectPool<StringBuilder> s_sbPool =
		new DefaultObjectPoolProvider().CreateStringBuilderPool(
			initialCapacity: 4096,
			maximumRetainedCapacity: 64 * 1024);

	internal static string Render(PositionedGraph graph, DiagramColors colors, string font, bool transparent)
	{
		var sb = s_sbPool.Get();
		try
		{
			SvgHelpers.AppendOpenTag(sb, graph.Width, graph.Height, colors, transparent);
			StyleBlock.AppendTo(sb, font, hasMonoFont: false);
			sb.Append("<defs>");
			MarkerDefs.AppendArrowMarkers(sb);
			sb.Append("</defs>");

			foreach (var group in graph.Groups)
				RenderGroup(sb, group, font);

			foreach (var edge in graph.Edges)
				RenderEdge(sb, edge);

			foreach (var edge in graph.Edges)
			{
				if (edge.Label is not null)
					RenderEdgeLabel(sb, edge, font);
			}

			foreach (var node in graph.Nodes)
				RenderNode(sb, node, font);

			sb.Append("</svg>");
			return sb.ToString();
		}
		finally
		{
			sb.Clear();
			s_sbPool.Return(sb);
		}
	}
}
```

**Key pattern:** All helper methods take `StringBuilder sb` and append directly — no intermediate string allocations. Methods are named `AppendXxx` to signal they write into the builder.
```

## Shape Rendering

Each node shape maps to an SVG primitive. Port as a `switch` expression:

```csharp
internal static class ShapeRenderer
{
    internal static void Render(StringBuilder sb, PositionedNode node)
    {
        var (fill, stroke, sw) = ResolveStyle(node);

        switch (node.Shape)
        {
            case NodeShape.Rectangle:
                RenderRect(sb, node, fill, stroke, sw);
                break;
            case NodeShape.Diamond:
                RenderDiamond(sb, node, fill, stroke, sw);
                break;
            case NodeShape.Rounded:
                RenderRoundedRect(sb, node, fill, stroke, sw);
                break;
            // ... all 14 shapes
        }
    }
}
```

### Shape → SVG Element Mapping

| Shape | SVG | Key Attributes |
|---|---|---|
| Rectangle | `<rect>` | `rx="0" ry="0"` |
| Rounded | `<rect>` | `rx="6" ry="6"` |
| Stadium | `<rect>` | `rx="{h/2}" ry="{h/2}"` |
| Diamond | `<polygon>` | 4 midpoint vertices |
| Circle | `<circle>` | `r = min(w,h)/2` |
| DoubleCircle | 2× `<circle>` | outer + inner (5px gap) |
| Subroutine | `<rect>` + 2× `<line>` | vertical lines at 8px inset |
| Hexagon | `<polygon>` | 6 vertices, `inset = h/4` |
| Cylinder | `<rect>` + 2× `<ellipse>` + 2× `<line>` | Top/bottom ellipse caps |
| Asymmetric | `<polygon>` | 5 vertices (flag shape) |
| Trapezoid | `<polygon>` | 4 vertices, top narrower |
| TrapezoidAlt | `<polygon>` | 4 vertices, bottom narrower |
| StateStart | `<circle>` | Filled, no stroke |
| StateEnd | 2× `<circle>` | Ring + filled inner |

## Edge Rendering

Edges are `<polyline>` elements with optional arrow markers:

```csharp
internal static void RenderEdge(StringBuilder sb, PositionedEdge edge)
{
    if (edge.Points.Count < 2) return;

    var points = string.Join(' ', edge.Points.Select(p => $"{p.X},{p.Y}"));
    var dashArray = edge.Style == EdgeStyle.Dotted ? """ stroke-dasharray="4 4" """ : "";
    var strokeWidth = edge.Style == EdgeStyle.Thick ? StrokeWidths.Connector * 2 : StrokeWidths.Connector;

    var markers = new StringBuilder();
    if (edge.HasArrowEnd) markers.Append(""" marker-end="url(#arrowhead)" """);
    if (edge.HasArrowStart) markers.Append(""" marker-start="url(#arrowhead-start)" """);

    sb.Append($"""<polyline class="edge" data-from="{Escape(edge.Source)}" data-to="{Escape(edge.Target)}" """);
    sb.Append($"""points="{points}" fill="none" stroke="var(--_line)" """);
    sb.Append($"""stroke-width="{strokeWidth}"{dashArray}{markers}/>""");
}
```

## Arrow Marker Definitions

Two reusable SVG markers — forward and reverse arrows:

```csharp
internal static class MarkerDefs
{
    internal static string ArrowMarkers()
    {
        const double w = 8, h = 5;
        const string style = """fill="var(--_arrow)" stroke="var(--_arrow)" stroke-width="0.75" stroke-linejoin="round" """;

        return $"""
            <marker id="arrowhead" markerWidth="{w}" markerHeight="{h}" refX="{w - 1}" refY="{h / 2}" orient="auto">
              <polygon points="0 0, {w} {h / 2}, 0 {h}" {style}/>
            </marker>
            <marker id="arrowhead-start" markerWidth="{w}" markerHeight="{h}" refX="1" refY="{h / 2}" orient="auto-start-reverse">
              <polygon points="{w} 0, 0 {h / 2}, {w} {h}" {style}/>
            </marker>
            """;
    }
}
```

## Text Rendering

Multi-line text with inline formatting (`<b>`, `<i>`, `<u>`, `<s>`) rendered as SVG `<text>` / `<tspan>`:

```csharp
internal static class TextRenderer
{
    internal static void RenderMultilineText(
        StringBuilder sb, string text, double cx, double cy,
        double fontSize, string attrs, double baselineShift = 0.35)
    {
        var lines = text.Split('\n');

        if (lines.Length == 1)
        {
            var dy = fontSize * baselineShift;
            sb.Append($"""<text x="{cx}" y="{cy}" {attrs} dy="{dy}">{RenderLineContent(lines[0])}</text>""");
            return;
        }

        var lineHeight = fontSize * LineHeightRatio;
        var firstDy = -((lines.Length - 1) / 2.0) * lineHeight + fontSize * baselineShift;

        sb.Append($"""<text x="{cx}" y="{cy}" {attrs}>""");
        for (var i = 0; i < lines.Length; i++)
        {
            var dy = i == 0 ? firstDy : lineHeight;
            sb.Append($"""<tspan x="{cx}" dy="{dy}">{RenderLineContent(lines[i])}</tspan>""");
        }
        sb.Append("</text>");
    }
}
```

### Inline Formatting Parser

Parse `<b>`, `<i>`, `<u>`, `<s>` tags into styled segments, then render as SVG tspan attributes:

- `<b>` / `<strong>` → `font-weight="bold"`
- `<i>` / `<em>` → `font-style="italic"`
- `<u>` → `text-decoration="underline"`
- `<s>` / `<del>` → `text-decoration="line-through"`

Supports nesting: `<b>bold <i>both</i> bold</b>`.

## SVG Helpers

```csharp
internal static class SvgHelpers
{
    internal static string EscapeAttr(string value) =>
        value.Replace("&", "&amp;").Replace("\"", "&quot;")
             .Replace("<", "&lt;").Replace(">", "&gt;");

    internal static string EscapeXml(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;")
             .Replace(">", "&gt;").Replace("\"", "&quot;")
             .Replace("'", "&#39;");

    internal static string OpenTag(double width, double height, DiagramColors colors, bool transparent)
    {
        // Build CSS variable inline style: --bg, --fg, + optional enrichment vars
        // ...
    }
}
```

## Semantic Data Attributes

Every rendered element carries `data-*` attributes for inspection/testing:

- Nodes: `data-id`, `data-label`, `data-shape`
- Edges: `data-from`, `data-to`, `data-style`, `data-arrow-start`, `data-arrow-end`, `data-label`
- Groups: `data-id`, `data-label`
- Edge labels: `data-from`, `data-to`, `data-label`

## Files to Create

| File | Lines (est.) | Complexity |
|---|---|---|
| `Rendering/SvgRenderer.cs` | ~80 | Low — orchestration |
| `Rendering/ShapeRenderer.cs` | ~300 | Medium — 14 shape variants |
| `Rendering/EdgeRenderer.cs` | ~80 | Low |
| `Rendering/TextRenderer.cs` | ~150 | Medium — inline formatting |
| `Rendering/MarkerDefs.cs` | ~30 | Low |
| `Rendering/SvgHelpers.cs` | ~40 | Low |

## Allocation Strategy

See [10-allocation-strategy.md](10-allocation-strategy.md) for the full playbook.

**Key rules for the renderer:**

1. **`ObjectPool<StringBuilder>`** — rent/return per render call, never `new StringBuilder()`
2. **`sb.Append()` chains** — never `$"..."` interpolation in hot loops
3. **`AppendSpanFormattable`** — for number formatting (`double x, y`) directly into the buffer
4. **Span-based escaping** — `AppendEscapedAttr(sb, span)` with fast-path when no special chars
5. **`ContainsAny` guard** — check if escaping is needed before iterating character-by-character
6. **Static SVG fragments** — arrow markers, style block templates cached or as `ReadOnlySpan<byte>`
7. **Points formatting** — direct `for` loop with `sb.Append(point.X)`, no LINQ or `string.Join`

```csharp
// AVOID — allocates intermediate string per interpolation
sb.Append($"""<rect x="{x}" y="{y}" width="{w}" height="{h}" />""");

// PREFER — zero intermediate allocations
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

```csharp
// Span-based escaping with fast path
internal static void AppendEscapedAttr(StringBuilder sb, ReadOnlySpan<char> value)
{
	// Fast path: no special chars → append directly (common case)
	if (!value.ContainsAny("&\"<>"))
	{
		sb.Append(value);
		return;
	}

	// Slow path
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

### Allocation Budget

Per render call of a 5-node flowchart:
- 1 pooled `StringBuilder` (rented, not allocated)
- 1 `string` (the final `sb.ToString()` — the only unavoidable allocation)
- 0 intermediate strings from SVG building
