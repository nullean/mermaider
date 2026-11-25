---
name: mermaider-animate
description: >-
  Use when someone wants an animated diagram: a request moving through a system, data flowing through a pipeline,
  a state machine running, a sequence diagram playing, an architecture building up or failing over. Mermaider.Animate
  adds an `animate` block to a Mermaid flowchart, state, sequence or architecture diagram and renders a CSS-only
  animated SVG. Write the score, not CSS or JavaScript.
---

# Mermaider.Animate

`using Mermaider.Animate;` One call: `MermaidAnimator.RenderSvg(source)`, or `MermaidAnimator.Compile(source)` for the
steps and still frames.

For readers who should step through it, `MermaidAnimator.RenderPlayer(source)` returns an HTML fragment with Play,
Pause and a chip per step, without script.

## Write a score, never timing

A score says what happens. It cannot say how long or how fast: durations, easings and rests come from the motion
preset, so every animation on a site moves alike. Do not reach for CSS keyframes or a JavaScript library for a
diagram animation; write the diagram, then the score.

```csharp
var svg = MermaidAnimator.RenderSvg("""
    flowchart LR
      src[Producers] --> topic[[orders]]
      topic --> billing[Billing]
      topic --> shipping[Shipping]
      billing -.-> dlq[(Dead letters)]

    animate
      token o = "order #42"
      step "Orders stream into the topic"
        stream src -> topic steady
      step "One order fans out"
        send o topic -> billing, shipping each
      step "Billing rejects it"
        mark billing fail
        send o billing -> dlq becomes "rejected" as fail
    """);
```

## Pick the verb by what the diagram should say

- the system appearing: `build`. A bare `animate` does not build: it plays the type's own highlight (the longest path flows, the biggest slice pulls out), or its order for sequences, state machines, gantt, timelines, git graphs and C4 dynamic
- a request's path: `tour a -> b -> c "label"`
- one message: `send a -> b "label"`; a reply: `send b -> a` along the same edge, or `ping a <-> b "req" "reply"`
- load or a continuous flow: `stream a -> b busy`
- failure: `hang x` (the wait), then `mark x fail` and `badge x "timeout" as fail`, `cut a -> b`, `send … lost`;
  recovery: `restore a -> b`, `mark x clear`, `badge x clear`
- quantity: `fill queue 3/8`, later `fill queue 8`; a number that changes: `count api 38 -> 1200 "ms" as warn`; a fixed one: `badge dlq "1"`
- attention: `focus x` (add `close` to zoom), `pulse x`
- where something happens, in words: `callout x "pool exhausted after 30 s" as fail` (a node), `callout a -> b "…"` (an edge), `callout #3 "…"` (a message); the caption says what happens, the callout says where. One or two per step
- what a diagram is about, without a story: `highlight flow a -> b -> c` (direction), `highlight x as fail` (a fatal state), `highlight path a -> b -> c as ok` (the happy path), `highlight biggest` / `highlight lowest` (let a chart's data pick); before the steps, one or two at most
- several outcomes of the same start (success, timeout, slow): `scenario "Name" as ok|fail|warn` blocks after the shared steps; the player offers them on its story map
- the player lists the steps from three captioned steps on, or with endings; `animate compact` keeps it to the figure and its caption, `animate contents` always lists them. Give the diagram a `title` (front matter) or `accTitle` and the player shows it above the figure
- a state machine: `run A -> B -> C` (start with `[*]`), a refused transition: `try A -> C rejected "why"`
- work moving (kanban): `move card to Column`; ideas branching (mindmap, treeview): `expand "Node"`; a numbered
  interaction (C4 dynamic): a bare `animate`, or `play #1..#2`
- numbers (xy chart, pie, radar, sankey): a bare `animate` singles out the biggest (`build` grows them all); `set #2 75` changes a bar, `trace A -> B`
  lights a sankey path, `focus "Slice"` singles out a slice or curve
- a plan over time (gantt): `sweep to "2024-03-01"` per step, `sweep` to the end; tasks by id, `"label"` or `#n`
- history (timeline): `show "2020"` per period, which brings its events; a git history: `play #1..#3` per step
- a sequence diagram: a bare `animate`, or `play #1..#3` per step; point at a message with `mark #3 warn`, measure a
  span with `note #4..#5 "38 ms"`

Put each idea in a `step "Caption"`; captions are the story a reader follows. `with` starts a statement together with
the one before it.

## Tell the story, not the motion

- One idea per step, in the order a reader should get it; the caption says it in words.
- Show the cause before the result: `hang api` before `mark api fail`, the request before the reply.
- A token lights the path it takes for its step, so the route stays readable; reach for `tour` to walk a route.
- Show state with `fill`, `badge` and `mark` rather than describing it in a caption alone.

## Gotchas

- Names are diagram ids (`api`), not labels (`API`). A wrong name, or a `send` between nodes with no edge between them,
  throws `AnimationException`; its `Diagnostics` list each problem with its line and what the diagram has.
- The `animate` line must start at column 0; everything above it is the diagram.
- A diagram whose first line is a `%%` comment fails to render for state, sequence and architecture types: put the
  comment after the header line.
- `scrub` needs the SVG inline in the page: as an `<img>` it shows the static diagram.
- The host's `AnimateOptions.Motion` and `.Mode` override the score's header.
- Supported diagram types: flowchart, state, sequence, architecture, class, ER, C4, block, kanban, mindmap, treeview,
  gantt, timeline, git graph, xy chart, pie, radar, sankey, requirement, journey, quadrant, treemap, venn, packet:
  every type Mermaider renders. Quadrant points, journey tasks and treemap tiles are named by label (`"Search"`). Others render, but a score on them has nothing to
  address.
