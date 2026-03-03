# Mermaid Spec Coverage Plan

Goal: extend Mermaider to cover more of the official Mermaid syntax spec.
Each feature is scoped to parse + layout + render with Verify snapshot tests.

## Out of scope

- Click events, tooltips, callbacks (`click A "url" "tooltip"`) — interactive, not relevant to static SVG
- FontAwesome / icon packs (`fa:fa-car`) — requires external icon resolution
- Image nodes (`img` syntax) — binary asset embedding
- `linkStyle` — not planned for now
- Init directives (`%%{init: {...}}%%`) — runtime config, not diagram syntax
- Frontmatter (`---\ntitle: ...\n---`)
- Security levels (sandbox/loose/strict handled separately via StrictMode)
- New v11.3.0 shape aliases (`@{ shape: rect }`) — 30+ aliases for existing shapes, low value
- Edge animation (`@{ animation: slow }`)
- Per-edge curve styling (`@{ curve: natural }`)

---

## Phase 1: Sequence diagram enhancements

Low-hanging fruit — the parser already handles most of these block types.

### 1.1 `box` participant grouping
- **Syntax:** `box [color] [title]` ... `end`
- **Scope:** Parse box declarations, visually group contained participants with a background rect
- **Parser:** New regex for `box` open/close, track which participants belong to which box
- **Layout:** Allocate box bounds around grouped participant lifelines
- **Render:** Background rect with optional color, title text above
- **Complexity:** Medium

### 1.2 `create` / `destroy` actors
- **Syntax:** `create participant B` before a message, `destroy B` before a message
- **Scope:** Participant box appears at create point, X marker at destroy point, lifeline bounded
- **Parser:** Recognize `create`/`destroy` directives, attach to next message
- **Layout:** Offset participant Y start to create point, end lifeline at destroy point
- **Render:** Draw participant box at message Y, X marker for destroy
- **Complexity:** Medium-high

### 1.3 `autonumber`
- **Syntax:** `autonumber`, `autonumber <start>`, `autonumber <start> <step>`
- **Scope:** Prepend sequential numbers to message labels
- **Parser:** Detect directive, track numbering state
- **Layout:** No change (numbers prepended to label text before layout)
- **Render:** Number appears as part of message label
- **Complexity:** Low

### 1.4 Bidirectional arrows
- **Syntax:** `<<->>`, `<<-->>`
- **Scope:** Arrows on both ends of a message line
- **Parser:** Extend arrow regex to match `<<` prefix
- **Render:** `marker-start` + `marker-end` on the line
- **Complexity:** Low

---

## Phase 2: State diagram enhancements

The state parser exists but composite states and special nodes need work.

### 2.1 Composite (nested) states
- **Syntax:** `state "Label" as id { ... }`
- **Scope:** States can contain sub-state-machines with their own transitions and start/end
- **Parser:** Track brace nesting, recursive parse of inner state machine
- **Layout:** Nested Sugiyama layout with padding, or treat as subgraph
- **Render:** Outer rounded rect with header, inner state machine rendered inside
- **Complexity:** High (requires recursive layout)

### 2.2 State notes
- **Syntax:** `note right of State1 : text` or multi-line `note right of State1` ... `end note`
- **Parser:** Regex for `note (left|right) of <id>`, capture text
- **Layout:** Position note box adjacent to referenced state
- **Render:** Note rect with text, similar to sequence notes (use `--_accent-*` vars)
- **Complexity:** Medium

### 2.3 Fork / join
- **Syntax:** `state fork_state <<fork>>`, `state join_state <<join>>`
- **Scope:** Horizontal bars representing concurrent split/merge
- **Parser:** Detect `<<fork>>` / `<<join>>` stereotypes
- **Layout:** Render as wide horizontal bar node shape
- **Render:** Black horizontal rect (no text)
- **Complexity:** Medium

### 2.4 Concurrency
- **Syntax:** `--` separator inside composite state
- **Scope:** Side-by-side regions within a composite state
- **Parser:** Detect `--` as region separator, parse each region independently
- **Layout:** Arrange regions horizontally with divider line
- **Render:** Dashed vertical divider between concurrent regions
- **Complexity:** High (depends on composite states)

### 2.5 Choice pseudo-state
- **Syntax:** `state choice_state <<choice>>`
- **Scope:** Diamond node for conditional branching
- **Parser:** Detect `<<choice>>` stereotype
- **Layout/Render:** Diamond shape (reuse flowchart diamond)
- **Complexity:** Low

---

## Phase 3: Class diagram enhancements

### 3.1 Lollipop interfaces
- **Syntax:** `foo --() bar`
- **Scope:** Circle-on-stick notation for provided interfaces
- **Parser:** Recognize `--()` and `()--` edge patterns
- **Layout:** Standard edge routing
- **Render:** Small circle marker at the interface end instead of arrow
- **Complexity:** Medium

### 3.2 Class notes
- **Syntax:** `note "text"`, `note for ClassName "text"`
- **Parser:** Regex for `note` directive with optional `for <class>` target
- **Layout:** Position note near target class or in margin
- **Render:** Note rect with `--_accent-*` styling
- **Complexity:** Medium

### 3.3 `direction` directive
- **Syntax:** `direction TB`, `direction LR`
- **Scope:** Override default top-to-bottom layout
- **Parser:** Already partially handled; ensure it's wired through
- **Layout:** Pass direction to Sugiyama
- **Complexity:** Low

---

## Phase 4: ER diagram enhancements

### 4.1 Entity aliases
- **Syntax:** `p[Person]`, `c["Customer Account"]`
- **Scope:** Display name differs from entity ID
- **Parser:** Extend entity regex to capture `[alias]` suffix
- **Layout:** Use alias for box label, ID for relationship wiring
- **Render:** Display alias text in entity header
- **Complexity:** Low

### 4.2 Optional relationship labels
- **Syntax:** `CUSTOMER ||--o{ ORDER` (no `: label`)
- **Scope:** Currently the parser requires `: label`; make it optional
- **Parser:** Adjust relationship regex to make label group optional
- **Render:** Skip label text when absent
- **Complexity:** Low

### 4.3 `direction` directive
- **Syntax:** `erDiagram\n  direction LR`
- **Scope:** Override default layout direction
- **Parser:** Detect `direction` line
- **Layout:** Pass to Sugiyama
- **Complexity:** Low

---

## Phase 5: Flowchart enhancements

### 5.1 Invisible edges
- **Syntax:** `A ~~~ B`
- **Scope:** Edge affects layout but is not rendered
- **Parser:** Recognize `~~~` as invisible edge style
- **Layout:** Include in graph for positioning
- **Render:** Skip drawing
- **Complexity:** Low

### 5.2 Markdown in labels
- **Syntax:** `` B["`The **cat** in the hat`"] ``
- **Scope:** Bold, italic, line breaks in node/edge labels
- **Parser:** Detect backtick-wrapped labels, parse inline markdown
- **Layout:** Text metrics need to account for bold/italic weight differences
- **Render:** `<tspan>` elements with `font-weight` / `font-style` attributes
- **Complexity:** Medium-high

### 5.3 `default` classDef
- **Syntax:** `classDef default fill:#f9f,stroke:#333`
- **Scope:** Apply default styling to all nodes without explicit class
- **Parser:** Already handles `classDef`; need to detect `default` name specially
- **Layout:** No change
- **Render:** Apply default class styles when no class is assigned
- **Complexity:** Low

---

## Implementation status

| Order | Feature | Phase | Complexity | Status |
|-------|---------|-------|------------|--------|
| 1 | Autonumber | 1.3 | Low | **Done** |
| 2 | Bidirectional arrows (seq) | 1.4 | Low | **Done** |
| 3 | Choice pseudo-state | 2.5 | Low | **Done** |
| 4 | Direction (class) | 3.3 | Low | **Done** |
| 5 | Direction (ER) | 4.3 | Low | **Done** |
| 6 | Optional ER labels | 4.2 | Low | **Done** |
| 7 | Entity aliases | 4.1 | Low | **Done** |
| 8 | Invisible edges | 5.1 | Low | **Done** |
| 9 | Default classDef | 5.3 | Low | **Done** |
| 10 | Box grouping (seq) | 1.1 | Medium | **Done** |
| 11 | State notes | 2.2 | Medium | **Done** |
| 12 | Class notes | 3.2 | Medium | **Done** |
| 13 | Lollipop interfaces | 3.1 | Medium | **Done** |
| 14 | Fork/join (state) | 2.3 | Medium | **Done** |
| 15 | Create/destroy actors | 1.2 | Medium-high | **Done** |
| 16 | Markdown in labels | 5.2 | Medium-high | **Done** |
| 17 | Composite states | 2.1 | High | **Done** (already supported via subgraphs) |
| 18 | Concurrency (state) | 2.4 | High | Deferred (requires side-by-side sub-layouts) |
