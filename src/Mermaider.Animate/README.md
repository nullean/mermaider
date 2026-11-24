# Mermaider.Animate

![Mermaider](https://raw.githubusercontent.com/nullean/mermaider/main/docs/images/png/mermaider-mark-180.png)

Animated diagrams for [Mermaider](https://github.com/nullean/mermaider): add an `animate` block after a Mermaid
diagram, and the SVG plays it with CSS alone.

[![NuGet](https://img.shields.io/nuget/v/Mermaider.Animate?style=flat-square)](https://www.nuget.org/packages/Mermaider.Animate)

A score names what happens: a request travels from the client to the API, a node fails, the balancer fails over. The
score never names a duration, an easing or a keyframe. Those come from one motion preset, so every animated diagram
on a site moves the same way, the way every diagram already shares one set of colours.

## Install

```sh
dotnet add package Mermaider.Animate
```

## Quick start

```csharp
using Mermaider.Animate;

var svg = MermaidAnimator.RenderSvg("""
    flowchart LR
      client[Client] --> lb[Balancer] --> api[API] --> db[(DB)]

    animate
      build
      step "A request takes the usual path"
        tour client -> lb -> api -> db "GET /orders"
      step "The API stops answering"
        mark api fail
        cut lb -> api
    """);
```

A source without an `animate` block renders exactly as `MermaidRenderer.RenderSvg` renders it.

## The score

```
animate [loop|once|scrub] [calm|crisp|still]
  token o = "order #42" [as ok|fail|warn|info]
  step "Caption"
    <statement>
    with <statement>          starts together with the statement before it
```

A statement outside any step is a step of its own. A bare `animate` plays the diagram type's default animation: a
flowchart or architecture diagram builds up along its flow, a state machine runs every transition, and a sequence
diagram plays its messages in order.

| Verb | What happens |
|---|---|
| `build` | the diagram appears along its flow: groups, then nodes column by column, each column's edges drawing first |
| `show a, b [each]` · `hide …` | nodes, groups (`group x`) or edges (`a -> b`) appear or leave; `each` staggers them |
| `focus a, b [close]` · `focus all` | everything else dims for the rest of the step; `close` also moves the camera in |
| `pulse a` | a node draws attention once |
| `mark a ok\|fail\|warn\|info\|clear` | a node or an edge takes a role colour, until cleared |
| `draw a -> b` · `erase a -> b` | a line draws from its source, and its arrowhead lands when the line arrives |
| `cut a -> b` · `restore a -> b` | a link breaks and comes back |
| `send [token] a -> b[, c] ["label"] [as role] [becomes "label"] [slow\|fast] [lost\|stall] [each]` | a token travels the edge; against an edge's direction it travels as a reply |
| `stream a -> b [trickle\|steady\|busy] [always]` | a continuous flow for the step, or for the whole score with `always` |
| `tour a -> b -> c ["label"]` | a token walks the path, with focus following it |
| `ping a <-> b ["request"] ["reply"]` | a request out and a reply back |
| `run s1 -> s2 -> s3` | a state machine follows the path: a ring marks the current state, and each transition's event travels its edge |
| `try a -> b rejected ["reason"]` | a refused transition: the token stops short, and the ring stays where it was |
| `play [#n[..#m]]` | sequence messages play in order |
| `caption "…"` | changes the step's caption |

Every name is checked against the diagram. A misspelt node, a `send` between nodes that share no edge or a `run`
through a transition the machine does not have is an `AnimationException`, with every problem listed with its line
and what the diagram does have.

## Playback

| Mode | Behaviour |
|---|---|
| `loop` (default) | plays, rests, fades out and plays again |
| `once` | plays to the end and holds the last frame |
| `scrub` | progress follows the diagram scrolling through the viewport (`animation-timeline: view()`); browsers without scroll-driven animations play it once, and an `<img>` shows the static diagram |

Under `prefers-reduced-motion: reduce`, tokens, streams, pulses and rises stop; captions, marks, focus and lines
still change, as cuts at the moment they would start.

The host can override the score's choices with `AnimateOptions.Motion` and `AnimateOptions.Mode`, as it sets colours
with `RenderOptions`:

```csharp
var animation = MermaidAnimator.Compile(source, new AnimateOptions
{
    Render = new RenderOptions { Bg = "#0f1115", Fg = "#e6e8ee" },
    Motion = MotionPreset.Crisp,
});
string svg = animation.Svg;
string poster = animation.RenderFrame(animation.ScoreDuration);   // the last frame, as a static SVG
```

## How it works

The diagram renders as it always does. `Mermaider.Animate` reads it back through the `data-*` attributes every
renderer writes, wraps the elements a score touches, and adds an overlay for tokens, rings and marks. Every animation
runs on one clock: each lasts the whole timeline, with its keyframes placed as percentages of it, so no two elements
can drift apart. Only `opacity`, `transform` and `stroke-dashoffset` animate. The stylesheet holds generated class
names and numbers, never text from the diagram or the score.

Diagram types: flowchart, state, sequence and architecture.
