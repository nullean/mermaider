# Mermaider.Layout.Msagl

Optional [Microsoft MSAGL](https://github.com/microsoft/automatic-graph-layout) layout provider for
[Mermaider](https://github.com/nullean/mermaider).

Mermaider ships with a fast, zero-dependency layered layout engine, and that engine is the recommended choice. This
package swaps in Microsoft's Automatic Graph Layout library for flowchart, state, class and ER diagrams, for
compatibility with output from earlier Mermaider versions. On a simple flowchart it is about 10&times; slower and
allocates about 7&times; more.

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

Use it only when you need output that matches an earlier Mermaider version that rendered with MSAGL. Compared with the
built-in engine, MSAGL has these limitations:

- **No compound layout.** Subgraph boxes are drawn around their members afterwards and can overlap each other or
  unrelated nodes.
- **No orthogonal router features.** There are no label columns, no ports spread along node sides or on diamond and
  ellipse outlines, and edges written against a subgraph end on a member node rather than on the subgraph border.
- **Missing class and state features.** Class namespaces, class notes, lollipop interface targets and state notes are
  ignored.
- **Not used for every diagram.** Requirement diagrams and the text output always use the built-in engine.

Node and box sizes come from the same code the built-in engine uses, so class, ER and flowchart text fits its boxes
under either engine.
