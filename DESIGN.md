# Mermaider Design System

**Goal:** all 24 diagram types are one family, restylable from a minimal input set. A host sets two colours
(`--bg`, `--fg`) and picks a **style preset**; every diagram type, light and dark, responds coherently with no
per-type work.

Everything below is one derivation: colour inputs → neutral tokens and **colour families** → **shared components**
that switch on the preset's **`StyleSpec`**. Renderers never pick a colour, ratio, size or radius themselves.

## 1 · Inputs

### Colour inputs (written on `<svg style="…">`)

```
--bg   --fg   --line   --accent   --muted   --surface   --border
```

Only `--bg` and `--fg` are required. Plus, from `RenderOptions` / `DiagramColors`: the data palette (12 colours,
brightened on dark themes), and the optional roles `Default`, `Success`, `Failure`, `Warning`, `Info`. The zinc themes set `Default` (the colour of an ordinary box) to a cool slate (`#64748b` / `#94a3b8`) so the blue accent (`#3b82f6` / `#60a5fa`) is the one saturated "look here" colour.

`--accent` is the **"look here"** colour: terminals, decisions, the note rail, activation
bars, the in-scope C4 system, the mindmap root, story lines (timeline axis, journey curve), the hottest treemap tile,
the Invest quadrant, the gantt active ring, the title mark, PK badges. Nowhere else.

### Design inputs (`RenderOptions`, CLI, Gallery editor)

| Input | Values | Default | Effect |
|---|---|---|---|
| `Style` | `DiagramStyle.Quiet` / `Blueprint` / `Tonal` | `Quiet` | Picks the `StyleSpec` (knob table, §6). Paint only; layout is identical in all three. |
| `Gradient` | `bool` | `true` | On: node top → bot gradient, container wash (a lighter shade of the container's own colour into its tint), bar / ribbon / pie gradients. Off: every fill is the family `Flat` / `Tint` stage. |
| `Tint` | `double` 0.5 – 1.5 | `1` | Multiplies every bg-side family ratio (top, bot, flat, band, soft, tint, edge). Lower keeps dark themes from looking muddy. Clamped; non-finite → 1. |
| `Elevation` | `int` 0 / 1 / 2 | `1` | 0 no shadows · 1 box + container shadows · 2 adds an ambient shadow. Clamped. Blueprint never casts shadows. |

All four are normalised once by `DesignInputs.From(RenderOptions)` (`Theming/StyleSpec.cs`) into
`NormalizedRenderStyles.Design`, so every renderer sees the same clamped values.

The **shadow colour** is never user-supplied and never a literal: `StyleBlock.ElevationLines` mixes it from the theme.
On light pages it is `--fg` at low strength; on dark pages (where `--fg` is light) it is `--bg` deepened, so shadows
carry the theme's own hue.

CLI: `--style <quiet|blueprint|tonal>`, `--no-gradient`, `--tint <0.5-1.5>`, `--elevation <0|1|2>` (invalid values are
rejected with an error). Gallery playground: STYLE, GRADIENT, TINT and ELEVATION controls
(`?style=&gradient=&tint=&elevation=`).

## 2 · Neutral tokens

`StyleBlock` emits every `--_*` token once per SVG as `color-mix(in srgb, var(--fg) N%, var(--bg))`, with the optional
colour inputs as overrides. The ratios were raised so every text pair holds ≥ 4.5:1 and every line ≥ 3:1 in the
built-in themes.

| Token | Ratio (fg → bg) | Before | Use |
|---|---|---|---|
| `--_text` | 100% | — | labels, titles |
| `--_text-sec` | 72% (or `--muted`) | 55% | secondary text, edge / message labels, bar labels |
| `--_text-muted` | 64% (or `--muted`) | 35% | types, ticks, captions, meta |
| `--_text-faint` | 30% | 20% | decoration only, **never text** |
| `--_line` | `--line` (always set: the caller's `Line`, else the default box border `color-mix(<Default> 74%, --fg)`; fg 50% only as a last fallback) | 32% | connectors, axes, the neutral family base |
| `--_line-soft` | 14% | new | grid, dividers, lifelines, pill border |
| `--_line-strong` | 68% | new | terminals, emphasis rules |
| `--_group-fill` | 4% | 3% | neutral panel (quadrant cell, gantt plot), zebra rows |
| `--_key-badge` | 9% | 8% | neutral badge / count pill, Tonal label chip |
| `--_inner-stroke` | 12% | 10% | dividers inside boxes |
| `--_node-fill` / `--_node-stroke` | 10% / 22% (or `--surface` / `--border`) | — | legacy, kept for un-migrated chrome |
| `--_arrow`, `--_group-hdr`, `--_group-stroke`, `--_accent-fill / -stroke / -text` | unchanged | — | legacy, kept for un-migrated chrome |

New code paints neutrals through the **neutral family** (`ds.Neutral`) rather than the legacy tokens.

## 3 · Colour families

Every coloured thing resolves through a **family** (`Theming/ColorFamily.cs`): a base colour plus the same derived
stages. A shape never asks "which colour?", only "which family?".

| Family | Key | Base | Get it with |
|---|---|---|---|
| cluster | `p0…pN` | auto palette (data palette minus role hues, `Default` first) | `palette.Family(id)` / `.GroupFamily(id)`, `ds.Cluster(i)` |
| chart series | `d0…dN` | full data palette | `ds.Series(i)` |
| neutral | `n` | `var(--_line)` | `ds.Neutral` |
| accent | `a` | `var(--accent, var(--fg))` | `ds.Accent` (`ColorFamily.AccentBase` for solid accent marks) |
| roles | `s f w i` | `Success` / `Failure` / `Warning` / `Info` | `ds.Role(ColorRole.X)`, `ds.RoleByName("failure")` |

### The eleven stages

All stages are `color-mix(in srgb, …)` against `--bg` or `--fg`, so they re-theme live. bg-side ratios are multiplied by
`Tint`.

| Stage | Derivation | Used for |
|---|---|---|
| `Top` | base 15% → bg | node gradient start |
| `Bot` | base 7% → bg | node gradient end |
| `Flat` | base 11% → bg | node fill when `Gradient` is off |
| `Band` | base 22% → bg | header band, badge fill, tag pill |
| `Soft` | base 26% → bg | Tonal fill, treemap leaf, gantt done |
| `Tint(depth)` | base 6% (+3% per nesting level) → bg | container body |
| wash | a lighter shade of the family (35% of the tint ratio) → bg, diagonal into `Tint(depth)` | container body gradient (`ds.ContainerFill`) |
| `Edge` | base 42% → bg | container border, soft rules |
| `Stroke` | base 74% → **fg** | node outline, bar outline, markers (≥ 3:1 in both modes) |
| `Ink` | base 44% → **fg** | container titles, stereotypes, badge text (≥ 4.5:1 on `Band`) |
| area | base at 20% opacity (`fill-opacity`) | radar, venn, sankey regions (overlap-safe) |

`family.Mix(percent)` gives an arbitrary bg-side tint for value ramps (treemap heat).

### Which family a box gets

Flowchart / state (reference: `SvgRenderer.NodeFamily`), and the same order everywhere else:

1. non-strict author styling (`style`, `classDef`, inline colours) wins as a paint override;
2. a role class (`:::success`, `:::failure`, `:::warning`, `:::info`) → role family;
3. a shape with a meaning (`VisualLanguage.ShapeFamily`): decision / `<<choice>>` → accent, stadium (terminal) and
   cylinder (data store) → neutral;
4. the cluster family: each connected cluster of nodes / entities / classes gets one auto-palette family
   (`ClusterPalette.Build(...).WithTint(ds.TintStrength)`); containers alternate through the palette in document
   order and never take the family of a member;
5. otherwise neutral.

Class modifiers (`<<abstract>>`, `<<interface>>`, …) hash (FNV-1a) to a fixed auto-palette slot so every class with
the same modifier shares one family. Charts use `ds.Series(i)` (role hues allowed); entity-like diagrams use
clusters (role hues skipped, so an automatically coloured box never looks like a success or a failure).

## 4 · Type roles

Four size tiers (`--fs-xs` 12, `--fs-s` 14, `--fs-m` 16, `--fs-l` 18 by default) and nine named roles
(`TypeRole` in `Rendering/DesignSystem.cs`). Every piece of text is one role: draw it with
`ds.AppendText(sb, text, x, cy, TypeRole.X, …)` or `ds.TextAttributes(TypeRole.X, …)`, measure it at
`DesignSystem.Px(TypeRole.X)`.

| Role | Tier | Weight | Colour | Used for |
|---|---|---|---|---|
| `Title` | l | 700 | `--_text` | diagram title (left aligned, accent mark) |
| `Heading` | m | preset heading (600; 700 Tonal) | `--_text` | entity name, venn set, C4 name, tree level 0 |
| `Subheading` | s | 600 | family `Ink` | container title, tree level 1, treemap tile name |
| `Label` | s | preset label (500; 600 Tonal) | `--_text` | node label, actor, legend, axis name, card |
| `Body` | s | 400 | `--_text` | members, task names, descriptions |
| `Caption` | xs | 500 | `--_text-sec` (edge labels: `--_text`) | edge and message labels, pills |
| `Tag` | xs | 600 | family `Ink` | badges, keyword tabs, counts, chips, shares |
| `Meta` | xs | 400 | `--_text-muted` | types, ticks, subtitles, units |
| `Eyebrow` | xs | 600 caps, +.08em | `--_text-muted` | quadrant labels, gantt sections, Blueprint tabs |

Blueprint sets the xs-tier roles (Caption, Tag, Meta, Eyebrow) in the mono font (`class="mono"`). Node labels are
`--fs-s` (they were `--fs-m`).

**Measurement px:** layout must measure at the role's default px (`DesignSystem.Px`); a mismatch mis-sizes boxes.
**Text centring:** `y = midY` + `dy="0.35em"` (`RenderConstants.TextBaselineShift`); `AppendText` does this for you.

## 5 · Geometry

Identical in every preset, so **layout never depends on the style**.

| Constant | Value | Where |
|---|---|---|
| node padding h × v | 20 × 12 (`RenderConstants.NodePadding`) | a single-line node is 40px high |
| stroke box / divider / connector | 1.25 / 1 / 1.5 (`RenderConstants.StrokeWidths`) | Quiet values; outline and edge width come from the preset |
| entity row / cell pad | 28 / 16 (`DesignSystem.RowHeight`, `CellPad`) | class, ER and requirement share the column grid |
| container inset / strip | 12 / 28 (`DesignSystem.ContainerInset`, `StripHeight`) | every container type |
| pill height | 20 (`DesignSystem.PillHeight`), width = text + 16 | edge labels |
| canvas margin / title | 40; title near the top padding, content from 88 when titled | `ds.AppendTitle` |
| spacing scale | 4 8 12 16 20 24 32 48 | every gap |
| legend swatch | 10 (`DesignSystem.SwatchSize`) | charts |

Radii, outline width, edge width and bend radius are preset knobs (§6). The rendered bend radius is
`SvgRenderContext.EdgeRadius` (the preset's, or 0 when `RoundedEdges` is off).

## 6 · Style presets

`Theming/StyleSpec.cs` holds every knob; `StyleSpec.Quiet`, `.Blueprint` and `.Tonal` are the three presets.

| Knob | Quiet (default) | Blueprint | Tonal |
|---|---|---|---|
| intent | calm: tinted, soft gradient | technical drawing: outline-first | friendly tonal blocks |
| radius node / container | 8 / 12 | 2 / 4 | 14 / 22 |
| node outline | 1.25 family `Stroke` | 1.25 family `Stroke` | none |
| node fill (`FillKind`) | `Gradient`: `Top` → `Bot` (or `Flat`) | `Knockout`: `--bg` | `Soft` |
| container (`ContainerKind`) | `Strip`: same-hue wash body, 28px band strip, ink title | `Tab`: dashed outline, caps tab in the border | `Chip`: soft block, chip header + family dot, no border |
| entity (`EntityKind`) | `Band`: band header, hairlines, centred name | `Plain`: accent top rule, left name | `Card`: soft header, zebra rows, no outline |
| edge width / bend radius | 1.5 / 8 | 1 / 0 | 2 / 16 |
| markers (`MarkerKind`) | `Filled`, line colour | `Thin`, accent | `Chunky`, round-joined |
| edge label (`LabelKind`) | `Pill`: outlined, Caption 500 | `Halo`: plain mono text, `--bg` halo | `Chip`: filled `--_key-badge`, 600 |
| note (`NoteKind`) | `Rail`: accent tint + 3px rail | `Ticks`: four accent corner ticks | `Sticker`: accent band + quote mark |
| badge (`BadgeKind`) | `Band` chip | `[Bracket]` ink | `Chip` (soft) |
| title mark | pip (square) | 3px rule | disc |
| terminals | dot / ring | squares | discs |
| chart marks (`ChartMarkKind`) | `Solid`, gradient bars | `Outline`: area tint + stroke, square points | `Fat`: donut, wide bars, thick lines |
| mono at xs tier | off | on | off |
| weights label / heading | 500 / 600 | 500 / 600 | 600 / 700 |
| shadows | nodes + containers | none | nodes only |

Identical in all three: palette, roles, accent, neutrals, contrast, the four size tiers, spacing scale and geometry.

## 7 · Shared components (`Rendering/DesignSystem*.cs`)

One `DesignSystem` per render: `var ds = DesignSystem.For(context);` … `ds.Close(sb);`. It holds the spec and design
inputs, hands out families, collects the gradients and markers a diagram actually uses, and writes them as one
`<defs>` on `Close` (ids are prefixed with a hash of the colour and design inputs, so several inlined SVGs never clash).

| Component | API | Draws |
|---|---|---|
| node | `ds.NodeFill(f)`, `ds.NodeStroke(f)`, `ds.NodeStrokeWidth`, `ds.AppendBox(...)` | box in the preset fill / outline / radius |
| container | `ds.AppendContainerBody(...)` (before edges), `ds.AppendContainerHeader(...)` (after), `ds.ContainerHeaderHeight` | strip / tab / chip, optional count |
| edge | `DesignSystem.EdgeColor`, `ds.EdgeWidth`, `DashArray` (dependency / return / realize), `DotArray` (derived / trace, round caps) | |
| marker | `ds.Marker(MarkerShape.X, atStart)` → `url(#…)` | arrow, open, triangle, diamond, hollow diamond, circle |
| edge label | `ds.AppendEdgeLabel(...)`, `DesignSystem.LabelBoxWidth` | pill / halo / chip |
| note | `ds.AppendNote(...)` (optional dotted link) | rail / ticks / sticker |
| badge | `ds.AppendBadge(...)` | band / bracket / chip |
| title | `ds.AppendTitle(sb, x, cy, title)` | left aligned, preset mark |
| terminal | `ds.AppendTerminal(...)` | accent dot / square / disc, ring for end |
| text | `ds.AppendText`, `ds.TextAttributes`, `DesignSystem.Px` / `FsVar` | type roles |
| entity (`.Entities.cs`) | `ds.AppendEntityFrame`, `ds.AppendEntityName`, `AppendRowDivider`, `ds.RowFill(i)` | band / plain / card tables |
| charts (`.Charts.cs`) | `ds.MarkFill` / `MarkStroke`, `ds.BarFill`, `ds.AppendAreaAttributes`, `ds.AppendPoint`, `ds.AppendLegendItem`, `ds.AppendGridLine`, `AppendAxisLine`, `ds.SeriesLineWidth` | one palette rule for every chart |
| gradients | `ds.VerticalGradient`, `DiagonalGradient`, `SpanGradient` (sankey ribbons) | registered once, emitted by `Close` |

Area-specific helpers go in a new partial `Rendering/DesignSystem.<Area>.cs`. The older `VisualLanguage.cs` helpers
now delegate to family stages and remain for code that has not moved yet.

### Elevation classes

Shadows are applied by **CSS class**, written by `StyleBlock` from the preset and `Elevation`:
box shadows on `.node, .actor, .entity, .class-node, .architecture-service, .kanban-card`, container shadows on
`.subgraph, .kanban-column`. Keep those wrapper classes (and `data-*` attributes) on your groups, or the diagram
will look flat next to the others. `RendererStylesheetAllowlist` accepts exactly the elevation lines
`StyleBlock.AllElevationLines` can produce.

## 8 · Strict mode

`StrictStylingOptions` keeps diagram-source values out of paint and out of the stylesheet:

- Families are built only from caller-supplied values (`RenderOptions`, the theme, the data palette). No `classDef`,
  `style`, `linkStyle` or `%%{init}%%` value can reach a family or the design inputs.
- Non-strict author styling still wins as an inline override on top of the family paint (step 1 in §3); under strict
  mode those values are stripped before rendering.
- Host classes on the allow-list are emitted as `.cls-<name>` rules (light, and `prefers-color-scheme: dark`) that
  override the family's fill / stroke / text through CSS; external classes are passed through by name only.
- Role classes (`success`, `failure`, …) must be allow-listed under strict mode; their colours always come from options
  or the theme.
- Design inputs (`Style`, `Gradient`, `Tint`, `Elevation`) are host-only options and are never read from diagram source.

## 9 · Adding a diagram type: checklist

- [ ] `var ds = DesignSystem.For(context);` at the top, `ds.Close(sb);` at the end (writes `<defs>` and `</svg>`)
- [ ] Every colour comes from a **family** (`palette.Family/GroupFamily`, `ds.Cluster(i)`, `ds.Series(i)`, `ds.Role(...)`,
      `ds.Accent`, `ds.Neutral`) or a `--_*` token. **No literal hex, no ad-hoc `color-mix` ratios**
- [ ] Boxes via `ds.NodeFill/NodeStroke` or `ds.AppendBox`; containers via `ds.AppendContainerBody/Header`; tables via
      `ds.AppendEntityFrame/Name`; labels, notes, badges, titles and terminals via their `ds.Append*` component
- [ ] Every text is a `TypeRole` (`ds.AppendText` / `ds.TextAttributes`), measured at `DesignSystem.Px(role)`; no literal font sizes
- [ ] Edges: `DesignSystem.EdgeColor`, `ds.EdgeWidth`, `ds.Marker(...)`, `DashArray` / `DotArray`, bend radius `context.EdgeRadius`
- [ ] Titled diagrams: `ds.AppendTitle` (left aligned); accent only where the design says "look here"
- [ ] Layout uses the shared geometry and **never** branches on the preset
- [ ] Boxes wrapped in an elevation class (`<g class="node">`, `subgraph`, …); `data-*` attributes kept
- [ ] Non-strict author styling still wins; strict mode lets no diagram-source value reach paint
- [ ] Looks right in Quiet, Blueprint and Tonal, light and dark, gradient on and off
- [ ] Contract tests (`DesignContract` helpers) and snapshots for the new type
- [ ] `[GeneratedRegex(..., matchTimeoutMilliseconds: 2000)]` on every regex; no wall-clock in parse / layout

## Source of truth

| Concept | File |
|---|---|
| Design inputs, presets, every knob | `src/Mermaider/Theming/StyleSpec.cs`, `src/Mermaider/Models/DiagramStyle.cs` |
| Colour families and stage ratios | `src/Mermaider/Theming/ColorFamily.cs` |
| Neutral tokens, elevation rules, host classes | `src/Mermaider/Theming/StyleBlock.cs` |
| Shared components, type roles, markers, defs | `src/Mermaider/Rendering/DesignSystem.cs`, `DesignSystem.Entities.cs`, `DesignSystem.Charts.cs` |
| Cluster / container family assignment | `src/Mermaider/Rendering/ClusterPalette.cs`, `ClusterAssigner.cs`, `VisualLanguage.cs` |
| Geometry constants | `src/Mermaider/Rendering/RenderConstants.cs` |
| Reference migration (flowchart / state) | `src/Mermaider/Rendering/SvgRenderer.cs` |
| Contract tests | `tests/Mermaider.Tests/Rendering/VisualLanguageContract*.cs`, `DesignContract.cs`, `DesignInputsTests.cs` |
| Plan and design rationale | `plans/diagram-design-system.md` |
| Adding a diagram type (playbook) | `contributing/agent-add-diagram-type.md` |
