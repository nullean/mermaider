# 07 — Incremental Diagram Type Support

## Overview

beautiful-mermaid supports 5 diagram types. Each has its own parser, layout, renderer, and type definitions. The flowchart/state pipeline is the most complex and shares the generic graph layout engine. The other 3 (sequence, class, ER) each have specialized pipelines.

## Diagram Types by Priority

| Priority | Type | TS Lines | Parser | Layout | Renderer | Notes |
|---|---|---|---|---|---|---|
| **P0** | Flowchart | ~1,400 (shared) | Regex, 575 lines | ELK/MSAGL generic | Generic SVG | Core pipeline — everything builds on this |
| **P0** | State | Included above | Shared parser, 120 lines | Same layout engine | Same renderer | Maps to same graph model as flowchart |
| **P1** | Sequence | 1,066 | 207 lines | Custom (no ELK) | Custom SVG, 334 lines | Independent pipeline — no graph layout needed |
| **P2** | Class | 1,019 | 290 lines | ELK-based, 211 lines | Custom SVG, 397 lines | Uses ELK but with member lists in nodes |
| **P2** | ER | 853 | 181 lines | ELK-based, 161 lines | Custom SVG, 420 lines | Uses ELK but with relationship cardinality |

## P0: Flowchart & State Diagrams

These share the full generic pipeline (parser → ELK layout → SVG renderer). Implementing these covers:

- The complete `MermaidGraph` model
- All 14 node shapes
- All edge styles (solid, dotted, thick) and arrows
- Subgraph nesting with direction overrides
- Edge bundling and shape clipping
- The complete theming system

**State diagrams** differ only in parsing syntax — the output is the same `MermaidGraph` with `Rounded` shapes for states and `StateStart`/`StateEnd` pseudostates.

## P1: Sequence Diagrams

Sequence diagrams have a completely **separate pipeline** — no graph layout engine needed. The layout is deterministic: participants across the top, messages as horizontal arrows between lifelines.

### Sequence Types (from `sequence/types.ts`)

```csharp
public sealed record SequenceDiagram
{
    public required IReadOnlyList<Participant> Participants { get; init; }
    public required IReadOnlyList<SequenceMessage> Messages { get; init; }
    public required IReadOnlyList<ActivationBlock> Activations { get; init; }
    public required IReadOnlyList<SequenceGroup> Groups { get; init; }  // alt, opt, loop, etc.
    public required IReadOnlyList<SequenceNote> Notes { get; init; }
}

public readonly record struct Participant(string Id, string Label, bool IsActor);

public sealed record SequenceMessage(
    string From, string To, string? Label,
    SequenceArrowType ArrowType,
    bool IsSelfMessage
);

public enum SequenceArrowType { Solid, Dashed, SolidOpen, DashedOpen }
```

### Sequence Layout

Custom layout algorithm (~379 lines):
- Participants equally spaced horizontally
- Messages stacked vertically with fixed row height
- Self-messages get extra height and a loop-back path
- Activation bars alongside lifelines
- Groups (alt/opt/loop) as nested rectangles

No MSAGL/ELK needed — pure arithmetic.

### Sequence Renderer

Custom SVG rendering (~334 lines):
- Participant boxes at top (and optionally bottom)
- Dashed vertical lifelines
- Horizontal message arrows with labels
- Activation bar rectangles
- Group rectangles with condition labels
- Note boxes

## P2: Class Diagrams

### Class Types

```csharp
public sealed record ClassDiagram
{
    public required IReadOnlyList<ClassDefinition> Classes { get; init; }
    public required IReadOnlyList<ClassRelationship> Relationships { get; init; }
}

public sealed record ClassDefinition(
    string Id, string Label,
    string? Annotation,  // <<interface>>, <<abstract>>, etc.
    IReadOnlyList<ClassMember> Members
);

public sealed record ClassMember(string Text, ClassMemberType Type);
public enum ClassMemberType { Field, Method }

public sealed record ClassRelationship(
    string From, string To, string? Label,
    ClassRelationType Type  // Inheritance, Composition, Aggregation, etc.
);
```

### Class Layout

Uses ELK/MSAGL but node sizes are larger (include member lists). ~211 lines.

### Class Renderer

Custom SVG with compartmented boxes (~397 lines):
- Class name header (with annotation above)
- Horizontal divider line
- Member list (fields, methods) in monospace font
- Relationship arrows with specialized markers (diamond for composition, triangle for inheritance, etc.)

## P2: ER Diagrams

### ER Types

```csharp
public sealed record ErDiagram
{
    public required IReadOnlyList<ErEntity> Entities { get; init; }
    public required IReadOnlyList<ErRelationship> Relationships { get; init; }
}

public sealed record ErEntity(
    string Id, string Label,
    IReadOnlyList<ErAttribute> Attributes
);

public sealed record ErAttribute(string Name, string Type, bool IsPrimaryKey, bool IsForeignKey);

public sealed record ErRelationship(
    string From, string To, string Label,
    ErCardinality FromCardinality,
    ErCardinality ToCardinality
);

public enum ErCardinality { ZeroOrOne, ExactlyOne, ZeroOrMore, OneOrMore }
```

### ER Layout

Uses ELK/MSAGL with entity boxes sized to fit attributes. ~161 lines.

### ER Renderer

Custom SVG (~420 lines):
- Entity boxes with attribute lists
- PK/FK badges
- Relationship lines with crow's foot notation (cardinality markers)
- Relationship labels at midpoints

## Implementation Strategy

### Phase 1: Flowchart + State (MVP)

Build the entire pipeline end-to-end. This gives us:
- Working parser → layout → renderer
- All shared infrastructure (text metrics, theming, SVG helpers)
- Test harness with golden files

### Phase 2: Sequence Diagrams

Independent pipeline — can be built in parallel with Phase 1 polish.
Lower risk because layout is deterministic (no graph layout dependency).

### Phase 3: Class + ER Diagrams

Both reuse the graph layout engine from Phase 1 but add:
- Specialized parsers
- Compartmented node rendering (class members, ER attributes)
- Custom arrow/marker definitions

## Files per Diagram Type

### Sequence (P1)
| File | Lines (est.) |
|---|---|
| `Models/SequenceDiagram.cs` | ~60 |
| `Parsing/SequenceParser.cs` | ~170 |
| `Layout/SequenceLayout.cs` | ~300 |
| `Rendering/SequenceRenderer.cs` | ~280 |

### Class (P2)
| File | Lines (est.) |
|---|---|
| `Models/ClassDiagram.cs` | ~50 |
| `Parsing/ClassParser.cs` | ~240 |
| `Layout/ClassLayout.cs` | ~180 |
| `Rendering/ClassRenderer.cs` | ~330 |

### ER (P2)
| File | Lines (est.) |
|---|---|
| `Models/ErDiagram.cs` | ~40 |
| `Parsing/ErParser.cs` | ~150 |
| `Layout/ErLayout.cs` | ~130 |
| `Rendering/ErRenderer.cs` | ~350 |
