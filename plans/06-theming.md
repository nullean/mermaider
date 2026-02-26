# 06 — Theming System

## Overview

beautiful-mermaid's theming uses CSS custom properties inside the SVG. Two required colors (`--bg`, `--fg`) drive the entire diagram via `color-mix()` derivations. Optional "enrichment" colors override specific derivations for richer themes.

This is all SVG-embedded CSS — no runtime color computation in C#. We just emit the right CSS.

## Color Architecture

### User-Facing Variables (set on `<svg style="...">`)

| Variable | Required | Purpose |
|---|---|---|
| `--bg` | Yes | Background color |
| `--fg` | Yes | Foreground / primary text |
| `--line` | No | Edge/connector color |
| `--accent` | No | Arrow heads, highlights |
| `--muted` | No | Secondary text, labels |
| `--surface` | No | Node fill tint |
| `--border` | No | Node/group stroke |

### Derived Variables (computed in `<style>` block)

Each derived variable uses the enrichment override if set, falling back to `color-mix()`:

```css
--_text:       var(--fg);
--_text-sec:   var(--muted, color-mix(in srgb, var(--fg) 60%, var(--bg)));
--_text-muted: var(--muted, color-mix(in srgb, var(--fg) 40%, var(--bg)));
--_text-faint: color-mix(in srgb, var(--fg) 25%, var(--bg));
--_line:       var(--line, color-mix(in srgb, var(--fg) 50%, var(--bg)));
--_arrow:      var(--accent, color-mix(in srgb, var(--fg) 85%, var(--bg)));
--_node-fill:  var(--surface, color-mix(in srgb, var(--fg) 3%, var(--bg)));
--_node-stroke:var(--border, color-mix(in srgb, var(--fg) 20%, var(--bg)));
--_group-fill: var(--bg);
--_group-hdr:  color-mix(in srgb, var(--fg) 5%, var(--bg));
--_inner-stroke: color-mix(in srgb, var(--fg) 12%, var(--bg));
--_key-badge:  color-mix(in srgb, var(--fg) 10%, var(--bg));
```

## Data Model

```csharp
namespace Mermaid.Theming;

public sealed record DiagramColors
{
    public required string Bg { get; init; }
    public required string Fg { get; init; }
    public string? Line { get; init; }
    public string? Accent { get; init; }
    public string? Muted { get; init; }
    public string? Surface { get; init; }
    public string? Border { get; init; }
}
```

## Color-Mix Weights

```csharp
internal static class ColorMix
{
    internal const int Text = 100;
    internal const int TextSec = 60;
    internal const int TextMuted = 40;
    internal const int TextFaint = 25;
    internal const int Line = 50;
    internal const int Arrow = 85;
    internal const int NodeFill = 3;
    internal const int NodeStroke = 20;
    internal const int GroupHeader = 5;
    internal const int InnerStroke = 12;
    internal const int KeyBadge = 10;
}
```

## Style Block Builder

```csharp
internal static class StyleBlock
{
    internal static string Build(string font, bool hasMonoFont)
    {
        // Font imports (Google Fonts)
        // Derived CSS variable rules
        // Returns complete <style>...</style> block
    }
}
```

The style block is mostly static text — use a raw string literal with interpolation for the weight constants.

## SVG Open Tag

```csharp
internal static string OpenTag(double width, double height, DiagramColors colors, bool transparent)
{
    var vars = new StringBuilder();
    vars.Append($"--bg:{colors.Bg};--fg:{colors.Fg}");
    if (colors.Line is not null) vars.Append($";--line:{colors.Line}");
    if (colors.Accent is not null) vars.Append($";--accent:{colors.Accent}");
    if (colors.Muted is not null) vars.Append($";--muted:{colors.Muted}");
    if (colors.Surface is not null) vars.Append($";--surface:{colors.Surface}");
    if (colors.Border is not null) vars.Append($";--border:{colors.Border}");

    var bgStyle = transparent ? "" : ";background:var(--bg)";

    return $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width} {height}" width="{width}" height="{height}" style="{vars}{bgStyle}">""";
}
```

## Built-In Themes

15 curated themes, stored as a `FrozenDictionary<string, DiagramColors>`:

```csharp
public static class Themes
{
    public static readonly FrozenDictionary<string, DiagramColors> BuiltIn =
        new Dictionary<string, DiagramColors>
        {
            ["zinc-light"] = new() { Bg = "#FFFFFF", Fg = "#27272A" },
            ["zinc-dark"] = new() { Bg = "#18181B", Fg = "#FAFAFA" },
            ["tokyo-night"] = new()
            {
                Bg = "#1a1b26", Fg = "#a9b1d6",
                Line = "#3d59a1", Accent = "#7aa2f7", Muted = "#565f89"
            },
            // ... 12 more themes
        }.ToFrozenDictionary();

    public static DiagramColors Default { get; } = BuiltIn["zinc-light"];
}
```

## Live Theme Switching

Because all colors are CSS custom properties, a rendered SVG supports live theme switching without re-rendering:

```javascript
svg.style.setProperty('--bg', '#282a36');
svg.style.setProperty('--fg', '#f8f8f2');
```

This works because we use `var(--_xxx)` references everywhere in the SVG, and the derived `--_xxx` variables are computed via `color-mix()` in the `<style>` block. No C# code involved — it's pure CSS.

## CSS Variables as Input

Users can pass CSS variable references instead of hex colors:

```csharp
var options = new RenderOptions
{
    Bg = "var(--background)",
    Fg = "var(--foreground)",
    Transparent = true
};
```

This requires `transparent: true` since the SVG background can't resolve `var()` references from parent context otherwise. The SVG inherits from the host page's CSS cascade.

## Files to Create

| File | Lines (est.) | Complexity |
|---|---|---|
| `Theming/DiagramColors.cs` | ~20 | Low — record definition |
| `Theming/Themes.cs` | ~80 | Low — static data |
| `Theming/StyleBlock.cs` | ~60 | Low — string template |
| `Theming/ColorMix.cs` | ~20 | Low — constants |
