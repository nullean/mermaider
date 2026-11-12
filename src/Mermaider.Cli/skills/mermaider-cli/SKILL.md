---
name: mermaider-cli
description: >-
  Use when a script, CI job or agent needs to render a Mermaid diagram file to SVG, or draw it as text, from the
  command line without Node or a browser. Mermaider.Cli is a .NET tool whose command is `mermaid`.
---

# Mermaider.Cli

Install it as a .NET tool; the command is `mermaid`, not `mermaider`. It reads a file argument or stdin and writes
to stdout or `-o`. For rendering inside a .NET program, reference the `Mermaider` package instead.

```bash
dotnet tool install --global Mermaider.Cli        # or a local tool: dotnet tool install Mermaider.Cli
```

## The calls you need

```bash
mermaid flow.mmd -o flow.svg                                      # SVG, transparent background
printf 'graph TD\n  A-->B\n' | mermaid > simple.svg               # stdin to stdout
mermaid flow.mmd -t tokyo-night --no-transparent -o flow.svg      # a theme and its background
mermaid flow.mmd --style blueprint --no-gradient --elevation 0 -o flow.svg
mermaid flow.mmd --ascii --width 80                               # box-drawing text; --plain for 7-bit ASCII
mermaid --list-themes
```

## Gotchas

- Exit codes: `0` rendered, `1` usage error, missing or empty input, or a Mermaid parse error, `2` rejected SVG or
  any other failure. Errors go to stderr, so stdout stays clean for the SVG.
- Unknown flags are ignored silently, and an unknown `--theme` only warns on stderr and renders with the default
  theme: check the spelling against `--list-themes`.
- Without a file argument it needs piped stdin; run interactively it exits `1` rather than waiting.
- `--ascii` handles flowchart, state and xychart only; other types fail with exit code `2`. A `--width` below 20
  falls back to 120.
- `--tint` takes 0.5 to 1.5 and `--elevation` 0, 1 or 2; `--style` is `quiet`, `blueprint` or `tonal`.
- Put the header on a line of its own: `graph TD; A-->B` on one line is a parse error (exit `1`).
- With `-o`, it prints `Written to <file>` on stderr and creates the output directory.

Docs: https://mermaider.nullean.net
