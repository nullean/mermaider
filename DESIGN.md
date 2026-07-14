# Mermaider Design System

**Goal:** Every diagram type is uniform and easily restylable from a minimal input set. A user
adjusts two values (`--bg` and `--fg`) and every chart type responds coherently.

## The styling contract

Renderers **never hardcode a color**. The user sets at most 7 inputs on the `<svg style="…">`:

```
--bg   --fg   --line   --accent   --muted   --surface   --border
```

Only `--bg` and `--fg` are required. `StyleBlock.AppendStyleBlock` derives every `--_*` token
via `color-mix(in srgb, var(--fg) N%, var(--bg))` with the user's optional vars as overrides.

### Derived token table

| Token | Blend ratio | Intended use |
|---|---|---|
| `--_text` | fg 100% | Primary labels / titles |
| `--_text-sec` | fg 55% | Secondary text |
| `--_text-muted` | fg 35% | Metadata, captions |
| `--_text-faint` | fg 20% | Hints, very subtle |
| `--_line` | fg 32% | Rules, axes, connector paths |
| `--_arrow` | fg 70% (or `--accent`) | Arrowheads |
| `--_node-fill` | fg 10% (or `--surface`) | Box / node backgrounds |
| `--_node-stroke` | fg 22% (or `--border`) | Box / node borders |
| `--_group-fill` | fg 3% | Container backgrounds |
| `--_group-hdr` | fg 4% | Container header backgrounds |
| `--_group-stroke` | fg 10% | Container borders |
| `--_inner-stroke` | fg 10% | Divider lines inside boxes |
| `--_key-badge` | fg 8% | Small pill / badge fills |
| `--_accent-fill` | accent 8% | Subtle accent surfaces |
| `--_accent-stroke` | accent 20% | Accent borders |
| `--_accent-text` | accent 65% | Accent-colored text |

**Rule:** renderers reference only `--_*` tokens and `--fs-*`. A literal hex is a bug —
**except** the sanctioned categorical data palettes for diagram types that encode data via color:
pie, timeline, gantt, journey, C4 (system fills), sankey, xychart. For all other chrome, use
theme vars so dark-mode and custom themes work automatically.

## Font scale

Four tiers via CSS custom properties. Use `RenderConstants.FsVar.*` constants, never a literal
`font-size` in SVG output.

| Constant | CSS var | Default px | Weight | Role |
|---|---|---|---|---|
| `FsVar.L` | `--fs-l` | 18 | 700 | Diagram titles |
| `FsVar.M` | `--fs-m` | 16 | 700 | Box / column headers |
| `FsVar.S` | `--fs-s` | 14 | 500 (node label) / 400 (edge, body) | Node labels, body text |
| `FsVar.Xs` | `--fs-xs` | 12 | 600 (badge) / 400 (meta) | Metadata, badges, annotations |

Weight reference: `RenderConstants.FontWeights.*` (title 700, header 600, node label 500, edge/
body 400, badge 600).

### Measurement px

`TextMetrics.MeasureTextWidth(text, sizePx, weight)` needs a real number for layout. That px
must equal the rendered tier's default resolution — `--fs-s` renders at 14px → measure at 14.
Mismatch causes mis-sized boxes.

## Geometry constants

From `RenderConstants`:

| Property | Value | Where |
|---|---|---|
| Corner radius — rectangle nodes | `Radii.Rectangle` = **6** | All box-based shapes |
| Corner radius — groups / containers | `Radii.Group` = **8** | Subgraphs, columns |
| Corner radius — edge labels / pills | `Radii.Rounded` = **10** | Edge label pills, badges |
| Stroke — outer box | `StrokeWidths.OuterBox` = **1.75** | Node / column borders |
| Stroke — inner divider | `StrokeWidths.InnerBox` = **2** | Dividers inside boxes |
| Stroke — connector path | `StrokeWidths.Connector` = **2.25** | Edges / arrows |
| Node padding horizontal | `NodePadding.Horizontal` = **28** | Left/right inside box |
| Node padding vertical | `NodePadding.Vertical` = **16** | Top/bottom inside box |
| Text baseline shift | `TextBaselineShift` = **"0.35em"** | `dy=` on all centered text |

**Text centering rule:** always `y = midY` + `dy="0.35em"` — never rely on `dominant-baseline`
alone (SVG rendering across browsers is unreliable without `dy`).

## Drop-shadow classes (uniformity)

The visual weight that makes boxes feel physical is applied by **CSS class**, not inline style.
Two rules in `StyleBlock.cs`:

```css
.node, .actor, .entity, .class-node, .architecture-service, .kanban-card {
  filter: drop-shadow(0 1px 3px rgba(0,0,0,.07));
}
.subgraph, .kanban-column {
  filter: drop-shadow(0 1px 2px rgba(0,0,0,.04));
}
```

A new diagram "feels like the others" **only when its boxes carry one of these classes**. Wrap
box groups in `<g class="node">` (or an equivalent class registered in the rule above). Emitting
raw `<rect>` without a wrapper class will look flat and disconnected.

## "Adding a diagram type" checklist

Before shipping a new renderer, verify all of the following:

- [ ] All colors use `--_*` tokens or `--fs-*` (grep for `fill="#` / `color:` in the renderer)
- [ ] Fixed palette only if the diagram type is in the sanctioned list above
- [ ] Font sizes use `RenderConstants.FsVar.*` in SVG output; measurement px matches tier
- [ ] Strokes use `RenderConstants.StrokeWidths.*` (not literal values like `1` or `2`)
- [ ] Corner radii use `RenderConstants.Radii.*`
- [ ] Centered text uses `y = midY` + `dy="0.35em"` (`RenderConstants.TextBaselineShift`)
- [ ] Boxes wrapped in `<g class="node">` or `<g class="subgraph">` for drop-shadow
- [ ] `[GeneratedRegex(..., matchTimeoutMilliseconds: 2000)]` on every regex (ReDoS protection)
- [ ] No `DateTime.Today` / wall-clock in parse or layout paths

## Source of truth

| Concept | File |
|---|---|
| Token derivation + `--_*` variables | `src/Mermaider/Theming/StyleBlock.cs` |
| Font sizes, weights, stroke widths, radii | `src/Mermaider/Rendering/RenderConstants.cs` |
| Font scale tiers (default px values) | `src/Mermaider/Rendering/FontScale.cs` |
| Adding a diagram type (playbook) | `docs/agent-add-diagram-type.md` |
