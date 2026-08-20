# Theming

Mermaider uses a normalized CSS custom-property token system. Every diagram type reads from the same set of `--` variables embedded in the SVG's `<style>` block, so changing colors once affects all diagram types uniformly.

## Color tokens

| Token | Default | Role |
|---|---|---|
| `--bg` | `#FFFFFF` | Canvas background |
| `--fg` | `#27272A` | Primary text and strokes |
| `--accent` | `#3b82f6` | Arrow heads, active edges, highlights |
| `--muted` | derived | Secondary text, edge labels |
| `--surface` | derived | Node fill tint |
| `--border` | derived | Node and group strokes |
| `--line` | derived | Edge paths |

`derived` tokens are computed via `color-mix(in srgb, var(--fg) X%, var(--bg))` — they automatically adapt when `--fg` and `--bg` are overridden. You rarely need to set them explicitly.

## Setting colors

Pass any hex color or CSS value via `RenderOptions`:

```csharp
var options = new RenderOptions
{
    Bg      = "#0D1117",   // dark background
    Fg      = "#E6EDF3",   // light text
    Accent  = "#58A6FF",   // blue edges
};

string svg = MermaidRenderer.RenderSvg(diagram, options);
```

## Transparent background

`Transparent` defaults to `true` — the SVG has no background rectangle, so it inherits whatever is behind it. Set `Transparent = false` to fill the canvas with `Bg`:

```csharp
var options = new RenderOptions { Transparent = false, Bg = "#FFFFFF" };
```

## Fonts

```csharp
var options = new RenderOptions
{
    Font     = "Inter",               // proportional text
    MonoFont = "JetBrains Mono",      // code-style text (ER types, Class signatures)
    FontSize = "0.9rem",              // base size token --fs-m
};
```

`MonoFont` falls back to the system monospace stack when `null`. Font families must already be loaded in the host page — Mermaider does not embed web font `@import` rules in SVG output.

## Font scale

The type scale is derived from `FontSize` via fixed ratios you can override:

| Option | Ratio | Token |
|---|---|---|
| `FontSize` | 1× | `--fs-m` |
| `FontSizeSmall` | 0.875 | `--fs-s` |
| `FontSizeExtraSmall` | 0.75 | `--fs-xs` |
| `FontSizeLarge` | 1.125 | `--fs-l` |

## Categorical data palette

Pie, sankey, timeline, radar, gitgraph, mindmap, venn, journey, packet, xychart, and treemap use a categorical color palette for series data. Override it with any array of CSS color strings:

```csharp
var options = new RenderOptions
{
    DataPalette = ["#7FE0FF", "#D66FFF", "#B6D9FF", "#E19BFF", "#4A8CFF"]
};
```

When `null`, the built-in palette is used.

## Layout spacing

```csharp
var options = new RenderOptions
{
    Padding      = 40,   // canvas padding in px
    NodeSpacing  = 28,   // horizontal gap between siblings
    LayerSpacing = 48,   // vertical gap between layers
    RoundedEdges = true, // 6px corner radius on edge paths
};
```
