---
name: mermaider
description: >-
  Use when .NET code needs to turn Mermaid diagram text (flowchart, sequence, class, ER, state, gantt, pie and about
  twenty more) into SVG or into a text drawing for a terminal, without a browser or Node. Mermaider renders it in-process,
  sanitised and with resource limits on by default.
---

# Mermaider

`using Mermaider; using Mermaider.Models;` Rendering is one static call; everything else is an init-only
`RenderOptions` record. Do not use it to edit or round-trip Mermaid source: it only reads it.

## The calls you need

```csharp
var svg = MermaidRenderer.RenderSvg("graph TD\n  A --> B");               // sanitised, self-contained <svg>

var styled = MermaidRenderer.RenderSvg(source, new RenderOptions
{
    Style = DiagramStyle.Blueprint,                                       // Quiet (default), Blueprint, Tonal
    Font = "Inter",
    NodeSpacing = 48,
    AllowedDiagrams = DiagramTypes.Flowchart | DiagramTypes.Sequence,     // anything else throws MermaidParseException
});

await MermaidRenderer.RenderSvgAsync(source, response.Body, options, ct); // UTF-8 to a Stream or PipeWriter

var text = MermaidRenderer.RenderAscii(source, new AsciiOptions { Width = 80 }); // flowchart, state, xychart only
```

A built-in theme is a `DiagramColors`, not a `RenderOptions`: copy the colours you want across.

```csharp
var dark = Themes.BuiltIn["tokyo-night"];   // using Mermaider.Theming; keys: Themes.BuiltIn.Keys
var options = new RenderOptions
{
    Bg = dark.Bg, Fg = dark.Fg, Accent = dark.Accent, Muted = dark.Muted,
    DataPalette = dark.DataPalette, Default = dark.Default, Transparent = false,
};
```

Untrusted input (user-authored diagrams):

```csharp
try
{
    svg = MermaidRenderer.RenderSvg(source, new RenderOptions
    {
        Strict = new StrictStylingOptions { AllowedClasses = [new DiagramClass { Name = "ok", Fill = "#dcfce7" }] },
        Limits = ResourceLimits.Default with { RenderDeadline = TimeSpan.FromSeconds(2) },
    });
}
catch (MermaidResourceLimitException e) { /* e.LimitName, e.ObservedValue, e.LimitValue */ }
catch (MermaidParseException e) { /* bad syntax or a disallowed diagram type */ }
```

## Gotchas

- Catch `MermaidResourceLimitException` before `MermaidParseException`: it derives from it.
- `Transparent` defaults to `true`: `Bg` still drives the derived colours but is painted only with `Transparent = false`.
- Output is always sanitised. `SanitizeMode.Block` throws `MermaidSvgException` instead of stripping, and in strip
  mode malformed output comes back as `MermaidRenderer.FallbackSvg`.
- `RenderAscii` throws `NotSupportedException` for anything but flowchart, state and xychart, and
  `MermaidRenderer.Parse` returns a model only for flowchart and state.
- `MermaidRenderer.SetLayoutProvider` is process-wide; for one call use `RenderOptions.LayoutProvider`.
- `RenderDeadline` (5 s by default) is cooperative, not a hard timer. `ResourceLimits.Unlimited` is for trusted input only.

Docs: https://mermaider.nullean.net
