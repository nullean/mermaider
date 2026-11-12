---
name: mermaider-layout-msagl
description: >-
  Use when a Mermaider user needs flowchart, state, class or ER output laid out by Microsoft MSAGL to match SVGs
  produced by earlier Mermaider versions. Mermaider.Layout.Msagl is an IGraphLayoutProvider that swaps MSAGL in for
  the built-in engine.
---

# Mermaider.Layout.Msagl

`using Mermaider; using Mermaider.Layout.Msagl;` It only exists for compatibility. For new code keep the built-in
engine: it is about ten times faster, does compound subgraph layout and routes edges orthogonally.

## The calls you need

```csharp
// one render
var svg = MermaidRenderer.RenderSvg(source, new RenderOptions { LayoutProvider = new MsaglLayoutProvider() });

// every render in the process
var builtIn = MermaidRenderer.LayoutProvider;   // keep it: there is no public way to get the default back
MermaidRenderer.SetLayoutProvider(new MsaglLayoutProvider());
// ...
MermaidRenderer.SetLayoutProvider(builtIn);
```

`RenderOptions` lives in `Mermaider.Models`.

## Gotchas

- Prefer the per-call `RenderOptions.LayoutProvider`. `SetLayoutProvider` is global and affects every caller,
  tests running in parallel included.
- It applies to flowchart, state, class and ER diagrams only. Requirement diagrams and `RenderAscii` always use the
  built-in engine.
- MSAGL has no compound layout: subgraph boxes are drawn around their members afterwards and can overlap. Class
  namespaces and notes, lollipop interfaces and state notes are ignored.
- The provider is stateless; one instance can be shared.

Docs: https://mermaider.nullean.net
