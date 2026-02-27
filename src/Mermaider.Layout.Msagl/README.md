# Mermaider.Layout.Msagl

Optional [Microsoft MSAGL](https://github.com/microsoft/automatic-graph-layout) layout provider for
[Mermaider](https://github.com/nullean/mermaider).

Mermaider ships with a fast, zero-dependency Sugiyama layout engine by default. This package provides
an alternative backed by Microsoft's Automatic Graph Layout library, which may produce higher-fidelity
edge routing on very complex graphs at the cost of significantly higher allocations (~70x) and latency (~120x).

## Usage

```bash
dotnet add package Mermaider.Layout.Msagl
```

```csharp
using Mermaider;
using Mermaider.Layout.Msagl;

// Register globally:
MermaidRenderer.SetLayoutProvider(new MsaglLayoutProvider());

// Or per-call:
var svg = MermaidRenderer.RenderSvg(input, new RenderOptions
{
    LayoutProvider = new MsaglLayoutProvider(),
});
```

## When to use this

- You need the specific edge-routing fidelity of MSAGL's rectilinear router
- You're migrating from an earlier version that used MSAGL and want identical output

For most use cases, the built-in layout engine is recommended.
