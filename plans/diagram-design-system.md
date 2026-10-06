# Diagram design system: Quiet, Blueprint, Tonal

Source: the Claude Design canvas "Mermaider diagram redesign" (token spec, component sheets, decision map,
three-way comparison, before/after boards for all 24 types). This plan turns it into code.

## What the design asks for

One token system, three **style presets** that restyle all 24 diagram types without per-type work:

| Knob | Quiet (default) | Blueprint | Tonal |
|---|---|---|---|
| radius node / container | 8 / 12 | 2 / 4 | 14 / 22 |
| node outline | 1.25 family stroke | 1.25 family stroke | none |
| node fill | gradient top → bot (or flat) | `--bg` knock-out | family soft |
| container | 28px strip: band + accent mark + wash body | dashed 1px outline, caps tab in the border | soft block, chip header + accent dot, no border |
| entity | band header, hairline rows, centred name | accent top rule, left name, hairlines | soft header, zebra rows, no outline |
| edge width / bend radius | 1.5 / 8 | 1 / 0 | 2 / 16 |
| markers | filled, line colour | thin, accent | chunky, round-joined |
| edge label | outlined pill, xs 500 | plain mono xs text with bg halo | filled badge chip, 600 |
| note | accent tint + 3px accent rail | four accent registration ticks | accent-band sticker |
| badge | band chip | `[bracketed]` ink | solid soft chip |
| title mark | accent square pip | 3px accent rule | accent disc |
| terminals | accent dot / ring | accent squares | accent discs |
| chart marks | solid, gradient bars | outline: area tint + stroke, square markers | fat: donut, 38px bars, smooth line |
| mono at xs tier | off | on | off |
| weights label / heading | 500 / 600 | 500 / 600 | 600 / 700 |
| elevation | e1 nodes, e2 containers | none | e3 nodes, containers flat |

Identical in all three: palette, roles, accent, neutrals, contrast, the four size tiers, and geometry
(node padding 20 × 12, row 28, pill 20, strip 28, 4px spacing scale). **Layout is preset-independent;
only paint changes.**

## Design-system changes (inputs and tokens)

### New inputs (RenderOptions, DiagramColors/theme-independent, CLI, Gallery editor)
- `Style` — `DiagramStyle.Quiet | Blueprint | Tonal` (default Quiet).
- `Gradient` — bool, default true. Off → every fill is the flat stage.
- `Tint` — double 0.5–1.5, default 1.0. Multiplies family tint ratios (top, bot, flat, band, soft, tint, edge).
- `Elevation` — 0 none / 1 default / 2 adds ambient. Blueprint ignores it (no shadows by design).
- `--shadow` is derived in C# from bg luminance (light vs dark), never user-supplied.

### Neutral tokens (ratios change for contrast; names kept)
| Token | Before | After |
|---|---|---|
| `--_text-sec` | 55% | 72% |
| `--_text-muted` | 35% | 64% |
| `--_text-faint` | 20% | 30% (decoration only, never text) |
| `--_line` | 32% | 50% |
| `--_line-soft` | — | 14% (grid, dividers, lifelines, pill border) |
| `--_line-strong` | — | 68% (terminals, emphasis rules) |
| `--_group-fill` | 3% | 4% |
| `--_key-badge` | 8% | 9% |
| `--_inner-stroke` | 10% | 12% |

### Colour families
Every coloured thing resolves through a **family**: cluster `p0…pN` (auto palette), neutral `n` (base `--_line`),
accent `a` (base `--accent`), roles `s f w i`, host class `x` (strict-mode allow-listed class). Each family derives
eleven stages, all `color-mix(in srgb, …)` so they re-theme with bg/fg:

| Stage | Derivation | Used for |
|---|---|---|
| top | base 15% → bg | node gradient start |
| bot | base 7% → bg | node gradient end |
| flat | base 11% → bg | node fill, gradient off |
| band | base 22% → bg | header band, badge, tag |
| soft | base 26% → bg | tonal fill, treemap leaf, gantt done |
| tint | base 6% → bg (+3%/level) | container body |
| wash | accent 6% over (base 12% → bg) | container gradient start |
| edge | base 42% → bg | container border, soft rules |
| stroke | base 74% → fg | node outline, markers, bar outline |
| ink | base 44% → fg | container titles, stereotypes, badge text |
| area | base at 20% opacity | radar, venn, sankey (overlap-safe) |

Host class mapping (strict mode): top ← fill, bot ← fill 88%, band ← fill 85% + stroke, stroke/edge/marker ← stroke, ink ← color.

### Type roles (on the four tiers)
title l·700 · heading m·600 · subheading s·600 ink · label s·500 · body s·400 · caption xs·500 (edge labels, pills) ·
tag xs·600 ink · meta xs·400 muted · eyebrow xs·600 caps +.08em. Node labels move from `--fs-m` to `--fs-s`.

### Geometry
radius per preset (above) · stroke box 1.25 / divider 1 / connector 1.5 (Quiet) · node padding 20 × 12 ·
entity cell pad 16 / row 28 · container inset 12 / strip 28 · pill height 20 (xs + 4) · title baseline y 40,
content from 88 when titled · spacing scale 4 8 12 16 20 24 32 48.

### Sanitizer / stylesheet
- `<linearGradient>`/`<stop>` are already allowed; stops use `color-mix(…)` / `var(--_x)` values (already pass `IsAllowedColor`).
- Add `paint-order` attribute (text halo in Blueprint and on chart labels).
- Area fills use `fill-opacity` (already allowed) instead of `color-mix(… transparent)`.
- `InternalColorVariablePattern` gains `_line-soft`, `_line-strong`, `--p0…`.
- `RendererStylesheetAllowlist` validates against the exact set of fixed blocks `StyleBlock` can produce
  (one per preset × elevation × light/dark shadow), generated from one shared source so they cannot drift.

## Code architecture

1. `Theming/DiagramStyle.cs` — public enum + `StyleSpec` record (every knob in the table above) with three static presets.
   `NormalizedRenderStyles` carries `Spec`, `Gradient`, `Tint`, `Elevation`; `SvgRenderContext` exposes them.
2. `Theming/ColorFamily.cs` — family base + stage derivations (tint-scaled), host-class families, `FamilyPalette`
   that resolves p<i>/n/a/roles for a diagram (replaces the ad-hoc hex math in `ClusterPalette`).
3. `Rendering/SvgDefs.cs` — collects gradients used during a render (node top→bot per family, container wash,
   bar/ribbon gradients) and emits one `<defs>`; deduplicated, deterministic ids.
4. `Rendering/Components.cs` (grow `VisualLanguage`) — the shared parts, each switching on `StyleSpec`:
   `Node(shape, family)`, `Container(family, depth, title, count?)`, `EntityHeader/Rows`, `EdgePath(points)` with bend
   radius, `Marker(kind)` for 11 kinds, `EdgeLabel`, `Note`, `Badge`, `Title`, `Terminal`, `Legend`, `Axis/Grid`, `ChartMark`.
5. `StyleBlock` — new neutral ratios, elevation rules from `StyleSpec` + shadow colour, `.mono-xs` handling.

## Phases

### Phase 1 — foundation (single owner)
Inputs + presets + families + defs + components + stylesheet + sanitizer + contract tests + CLI flags.
Exit: foundation compiles, existing renderers still render (they may look half-migrated), unit tests for families,
stylesheet allowlist round-trips all preset/elevation variants, sanitizer accepts gradients/paint-order.

### Phase 2 — entity-like renderers onto components (parallel)
flowchart + state (`SvgRenderer`), block, class, ER, requirement, sequence, architecture, mindmap, kanban, journey, timeline.
Includes: node labels at `--fs-s`, padding 20 × 12, accent terminals, accent decision family, containers via the shared
component, dash vocabulary (solid / dashed 5·4 / dotted 1.5·4.5), entity column grid shared by class/ER/requirement,
PK accent badge, requirement risk as role chip (dot + word), sequence: participants as nodes, ghost chips at bottom,
plain message labels, accent activation bars, keyword-tab frames; architecture icon cards on the node recipe.

### Phase 3 — C4 onto the system
Kinds → families (in-scope system = accent, person = p0, external = neutral), boundary = dashed container,
wrapped descriptions (no clipping), edge labels routed clear of box text.

### Phase 4 — charts (parallel)
One palette rule: mark = solid p<i>, region = area (20%) + p<i> outline, grid `--_line-soft`, axes `--_line`,
labels outside marks. Per preset marks (solid / outline / fat incl. donut + centre figure).
pie, xychart (gradient bars), quadrant (accent Invest cell, neutral panels, eyebrow labels), radar (area fill),
sankey (source → target gradient ribbons), treemap redesign (value ramp 10–36%, big numeral + share, hottest tile
accent dot), venn (area fills, two-line labels, no overlaps).

### Phase 5 — time, trees, boards (parallel)
Left-aligned title with preset mark on every type. gantt (section family; done = soft + outline, active = solid +
accent ring, critical = failure role, legend), gitgraph (lanes on the edge language, tags as badges), packet
(fields as family cells), treeview (rounded orthogonal branches), timeline axis + journey curve in accent,
journey faces from section family (roles no longer used for sentiment).

### Phase 6 — docs, tooling, verification
DESIGN.md rewrite, docs/theming, README, CLAUDE.md; Gallery editor: STYLE / GRADIENT / TINT / ELEVATION controls;
CLI `--style --no-gradient --tint --elevation`; regenerate snapshots; screenshot sheets for all 24 types × 3 presets ×
light/dark; publish an "Implemented" board to the canvas for comparison.

## Risks
- Snapshot churn: every golden file changes. Review by screenshot sheets, not diffs.
- Layout shifts from padding/font changes affect layout tests (edge-label spacing, ER crossings). Fix expectations, not layout.
- Stylesheet grows with gradient defs: only emit what a diagram uses.
- Strict-mode invariant: host class colours are caller-supplied; no diagram-source value may reach a family.
