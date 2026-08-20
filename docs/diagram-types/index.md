# Diagram types

Mermaider supports all major Mermaid diagram types. Every type shares the same CSS token system — set `Bg`, `Fg`, and `Accent` once in `RenderOptions` and the change applies everywhere.

## Structural

### Flowchart

General-purpose directed graphs. Supports LR, TD, RL, BT orientations, subgraphs, and all node shapes.

```
flowchart TD
    A[Start] --> B{Decision}
    B -->|yes| C[Done]
    B -->|no| D[Retry] --> B
```

### Sequence

Participant interaction diagrams with loops, alt blocks, and activation bars.

```
sequenceDiagram
    Client->>Server: GET /api/data
    Server-->>Client: 200 OK
```

### Class

UML class diagrams with inheritance, composition, and member visibility.

```
classDiagram
    Animal <|-- Dog
    Animal : +name string
    Animal : +speak() void
```

### Entity Relationship

ER diagrams with cardinality notation.

```
erDiagram
    CUSTOMER ||--o{ ORDER : places
    ORDER ||--|{ LINE-ITEM : contains
```

### State

State machine diagrams with transitions, forks, and composite states.

```
stateDiagram-v2
    [*] --> Idle
    Idle --> Running : start
    Running --> [*] : stop
```

### Architecture (`architecture-beta`)

Service/group topology diagrams with explicit edge directions (L/R/T/B). Uses a bespoke directional-grid layout — not Sugiyama.

```
architecture-beta
    group api[API Layer]
    service db(database)[Database] in api
    service svc(server)[Service] in api
    db:R --> L:svc
```

### C4

C4 model diagrams (Context, Container, Component) with boundaries and relationships.

### Block (`block-beta`)

Free-form block layout for custom structural diagrams.

### Requirement

Requirements traceability diagrams with verification links.

## Flow / process

### Gitgraph

Git branch and commit history visualization.

```
gitGraph
    commit
    branch feature
    checkout feature
    commit
    checkout main
    merge feature
```

### Gantt

Project timeline charts with tasks, milestones, and sections.

### Kanban (`kanban`)

Kanban board layout with columns and tickets.

### Journey

User journey maps across stages and actors.

### Timeline

Chronological event timelines.

### Packet (`packet-beta`)

Network packet / byte-field diagrams.

## Data visualization

### Pie

Proportional pie charts.

```
pie title Languages
    "C#" : 72
    "F#" : 18
    "Other" : 10
```

### XY Chart

Cartesian bar and line charts with labeled axes.

### Quadrant

Two-axis scatter / categorization charts with labeled quadrants.

### Sankey (`sankey-beta`)

Flow / energy-transfer Sankey diagrams.

### Radar

Multi-axis radar / spider charts.

## Hierarchy

### Mindmap

Radial mind-map trees.

### Treeview (`treeview`)

Indented tree list visualization.

### Treemap

Space-filling hierarchical area charts.

### Venn

Set relationship Venn diagrams.

---

## Allowed diagram types

Restrict which types a renderer accepts via `AllowedDiagrams`:

```csharp
var options = new RenderOptions
{
    AllowedDiagrams = DiagramTypes.Flowchart | DiagramTypes.Sequence | DiagramTypes.Class
};

// Diagrams of any other type throw MermaidParseException
string svg = MermaidRenderer.RenderSvg(input, options);
```
