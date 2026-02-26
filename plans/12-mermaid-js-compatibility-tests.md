# Plan: Mermaid-JS Compatibility Test Suite

## Motivation

The official [mermaid-js/mermaid](https://github.com/mermaid-js/mermaid) repository contains
extensive parser and rendering tests across all diagram types. While the tests themselves
are JavaScript/Cypress and cannot be run directly, the **diagram text inputs** embedded in
those spec files are an invaluable source of real-world syntax coverage.

By extracting these diagram strings into our test suite, we can:

1. Verify our parsers handle the same syntax the canonical Mermaid parser accepts
2. Catch regressions when refactoring parsers or renderers
3. Discover unsupported features (tests that throw become a backlog of known gaps)
4. Build confidence that the custom Sugiyama layout engine (plan 11) produces valid
   output for a wide variety of graph structures

## Source Material

### Parser Specs (unit-level)

Located in `packages/mermaid/src/diagrams/*/parser/*.spec.js`:

| File | Diagram Type | What It Tests |
|------|-------------|---------------|
| `flow.spec.js` | Flowchart | Edges, directions, node shapes, labels, semicolons |
| `flow-edges.spec.js` | Flowchart | All edge types (solid, dotted, thick, bidirectional, long) |
| `flow-singlenode.spec.js` | Flowchart | Single node variations (shapes, IDs, keywords in IDs) |
| `flow-style.spec.js` | Flowchart | `classDef`, `style`, `class`, `linkStyle` directives |
| `subgraph.spec.js` | Flowchart | Subgraph syntax, nesting, IDs, titles, direction override |
| `stateDiagram.spec.js` | State | Transitions, start/end markers, nested states |
| `classDiagram.spec.js` | Class | Relationships, members, visibility, stereotypes |
| `erDiagram.spec.js` | ER | Cardinalities, attributes, relationship labels |

### Rendering Specs (Cypress integration)

Located in `cypress/integration/rendering/*.spec.js`:

| File | What It Tests |
|------|---------------|
| `flowchart-v2.spec.js` | Complex flowcharts, subgraphs, edge routing, styles |
| `sequencediagram.spec.js` | Messages, blocks (alt/loop/par), notes, activation, actors |
| `stateDiagram-v2.spec.js` | State diagrams with nested states, notes, forks |
| `classDiagram.spec.js` | Class relationships, labels, namespaces |
| `erDiagram.spec.js` | Entity relationships, attributes, all cardinality types |

## Implementation

### What Was Built

A single test class `tests/Mermaid.Tests/MermaidCompatTests.cs` with:

- **78 parameterized test cases** across 6 test methods
- Diagram text extracted from the upstream spec files
- One `[MethodDataSource]` per diagram type returning `(string Name, string Source)` tuples
- Each test feeds the raw Mermaid text into `MermaidRenderer.RenderSvg()` and asserts:
  - No exception thrown
  - Output is non-null/non-empty
  - Output contains `<svg` and `</svg>` tags

### Test Distribution

| Test Method | Count | Coverage |
|------------|-------|----------|
| `Flowchart_renders` | 38 | Nodes, edges, directions, shapes, classDef, subgraphs, chaining |
| `Sequence_renders` | 10 | Basic, alt/else, loop, activation, notes, par, nested blocks |
| `State_renders` | 4 | Basic, failure/retry, multiple end states, linear flow |
| `Class_renders` | 5 | Inheritance, all relationships, interfaces, abstract, visibility |
| `Er_renders` | 5 | Basic, attributes, complex schema, all cardinalities |
| `Detects_and_renders_all_types` | 8 | Diagram type detection for all supported headers |
| **Total** | **78** | |

### Design Decisions

1. **No snapshot verification** — these tests only assert "does not crash" and "produces SVG".
   Snapshot tests already exist for visual regression. Compat tests are about parser breadth.

2. **Raw text, not fixtures on disk** — diagrams are inline in the test class for
   discoverability. Each test case has a descriptive name derived from the upstream spec.

3. **No feature-flag gating** — all tests run unconditionally. If a diagram fails to parse,
   it fails loudly. This surfaces unsupported syntax immediately.

4. **Tuples for data sources** — TUnit's `[MethodDataSource]` with `(string, string)` tuples
   provides clean test names in the runner output (e.g. `Flowchart_renders(christmas shopping, ...)`).

## Expansion Strategy

### Phase 1 (Done): Core Syntax

The initial 78 tests cover the most common syntax patterns from the upstream spec files.

### Phase 2: Edge Cases and Error Handling

Extract additional tests for:
- Malformed input (should throw `MermaidParseException`)
- Unicode in labels and node IDs
- Empty diagrams and diagrams with only comments
- Extremely long labels and deeply nested subgraphs

### Phase 3: Feature Parity Tracking

As the upstream `mermaid-js` project adds new syntax, periodically review their spec files
and add corresponding test cases. Track unsupported features as GitHub issues labeled
`compatibility`.

### Phase 4: Layout Engine Validation

When the lightweight Sugiyama engine (plan 11) is implemented, run all 78 compat tests
with both `LayoutEngine.Msagl` and `LayoutEngine.Lightweight` to ensure structural parity.

## Relationship to Other Plans

- **Plan 08 (Testing Strategy):** Compat tests complement existing snapshot and unit tests
- **Plan 11 (Lightweight Layout Engine):** Compat tests serve as the structural validation
  gate for the new engine — same inputs must produce valid SVG with both engines
