#:project ../src/Mermaider/Mermaider.csproj
#:project ../examples/Mermaider.Examples/Mermaider.Examples.csproj
#:project ../src/Mermaider.Animate/Mermaider.Animate.csproj

// Regenerates every screenshot the README, the docs pages and the landing page use.
//
//   dotnet run scripts/screenshots.cs
//
// Outputs (paths relative to the repository root):
//   .github/readme/<type>.light.svg / .dark.svg          README: each diagram section's own code sample, light and dark;
//                                                        the section's image is rewritten as a <picture> that follows the
//                                                        reader's colour scheme
//   docs/images/diagrams/<type>.svg                       docs pages (Quiet, zinc-light, transparent)
//   docs/images/styles/<style>.svg                        theming page style comparison (one flowchart per style)
//   docs/screenshots/<style>/<theme>/<type>.svg           landing page gallery (every style x theme, opaque)
//   docs/screenshots/hero.svg                             landing page hero (the code sample's diagram, transparent)
//   docs/screenshots/animate/<score>.svg                  landing page animations: every preview score, animated, zinc-dark
//   docs/screenshots/animate/player.html                  landing page player: the endings score, with its story map

using System.Text.RegularExpressions;
using Mermaider;
using Mermaider.Animate;
using Mermaider.Examples;
using Mermaider.Models;
using Mermaider.Theming;

var root = FindRoot();

// One showcase example per diagram type: the slug in Mermaider.Examples that shows the type best at card size.
(string Type, string Slug)[] showcase =
[
	("flowchart", "flowchart-subgraphs"),
	("sequence", "sequence-full"),
	("class", "class-basic"),
	("er", "er-complex"),
	("state", "state-simple"),
	("architecture", "architecture-elastic-stack"),
	("c4", "c4-context"),
	("gitgraph", "gitgraph-feature"),
	("mindmap", "mindmap-project"),
	("pie", "pie-showdata"),
	("xychart", "xychart-sales"),
	("gantt", "gantt-multi-after"),
	("kanban", "kanban-sprint"),
	("timeline", "timeline-sections"),
	("sankey", "sankey-energy"),
	("treemap", "treemap-nested"),
	("block", "block-layers"),
	("quadrant", "quadrant-skills"),
	("requirement", "requirement-relations"),
	("radar", "radar-product"),
	("venn", "venn-three"),
	("treeview", "treeview-monorepo"),
	("packet", "packet-tcp-flags"),
	("journey", "journey-onboarding"),
];

// Landing page theme picker: each entry is a light / dark pair of built-in themes.
(string Family, string Light, string Dark)[] themeFamilies =
[
	("zinc", "zinc-light", "zinc-dark"),
	("github", "github-light", "github-dark"),
	("catppuccin", "catppuccin-latte", "catppuccin-mocha"),
	("nord", "nord-light", "nord"),
	("solarized", "solarized-light", "solarized-dark"),
	("tokyo-night", "tokyo-night-light", "tokyo-night"),
];

var styles = new[] { DiagramStyle.Quiet, DiagramStyle.Blueprint, DiagramStyle.Tonal };

string Source(string slug) => DiagramExamples.All.Single(e => e.Slug == slug).Source;

RenderOptions Options(string themeName, DiagramStyle style, bool transparent)
{
	var t = Themes.BuiltIn[themeName];
	return new RenderOptions
	{
		Bg = t.Bg,
		Fg = t.Fg,
		Line = t.Line,
		Accent = t.Accent,
		Muted = t.Muted,
		Surface = t.Surface,
		Border = t.Border,
		DataPalette = t.DataPalette,
		Default = t.Default,
		Success = t.Success,
		Failure = t.Failure,
		Warning = t.Warning,
		Info = t.Info,
		Font = "Inter",
		Style = style,
		Transparent = transparent,
		Strict = new StrictStylingOptions(),
	};
}

void Write(string relative, string svg)
{
	var path = Path.Combine(root, relative);
	_ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
	File.WriteAllText(path, svg);
}

var count = 0;
foreach (var (type, slug) in showcase)
{
	// docs pages: Quiet on the page itself
	Write($"docs/images/diagrams/{type}.svg", MermaidRenderer.RenderSvg(Source(slug), Options("zinc-light", DiagramStyle.Quiet, transparent: true)));

	// landing gallery: every style x theme
	foreach (var style in styles)
	{
		foreach (var (_, light, dark) in themeFamilies)
		{
			foreach (var theme in new[] { light, dark })
			{
				Write(
					$"docs/screenshots/{style.ToString().ToLowerInvariant()}/{theme}/{type}.svg",
					MermaidRenderer.RenderSvg(Source(slug), Options(theme, style, transparent: false))
				);
				count++;
			}
		}
	}
}

// Theming page: the same flowchart in each style preset (committed, unlike the landing variants).
foreach (var style in styles)
	Write(
		$"docs/images/styles/{style.ToString().ToLowerInvariant()}.svg",
		MermaidRenderer.RenderSvg(Source("flowchart-subgraphs"), Options("zinc-light", style, transparent: true))
	);

// Hero: the exact diagram the landing page's code sample renders, in the page's own colours, transparent.
Write(
	"docs/screenshots/hero.svg",
	MermaidRenderer.RenderSvg(
		"""
	flowchart TD
	  A[Parse] --> B[Layout]
	  B --> C[Render SVG]
	""",
		new RenderOptions
		{
			Bg = "#08090B",
			Fg = "#E9ECF1",
			Accent = "#7FE0FF",
			Default = "#7FE0FF",
			Font = "Inter",
			Transparent = true,
			Strict = new StrictStylingOptions(),
		}
	)
);

// the landing page's animations: preview scores, so the landing page shows what the preview and the golden frames check
var scores = Path.Combine(root, "examples/Mermaider.Animate.Preview/scores");
// every preview score: the first plays in the player, the rest fill the landing page's animation gallery
foreach (var name in Directory.GetFiles(scores, "*.mmd").Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal))
	Write(
		$"docs/screenshots/animate/{name}.svg",
		MermaidAnimator.RenderSvg(
			File.ReadAllText(Path.Combine(scores, name + ".mmd")),
			new AnimateOptions { Render = Options("zinc-dark", DiagramStyle.Quiet, transparent: true) }
		)
	);
// the player is live HTML (its controls are radio buttons), so it is a page of its own the landing page frames
Write(
	"docs/screenshots/animate/player.html",
	$$"""
	<!doctype html>
	<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
	<title>Mermaider.Animate player</title>
	<style>html, body { margin: 0; background: #0D1013; color: #E9ECF1; font-family: Inter, system-ui, sans-serif; } body { padding: 12px; }</style>
	</head><body>
	{{MermaidAnimator.RenderPlayer(File.ReadAllText(Path.Combine(scores, "24-endings.mmd")), new AnimateOptions { Render = Options("zinc-dark", DiagramStyle.Quiet, transparent: true) })}}
	</body></html>
	"""
);

var readmeCount = RenderReadme();

Console.WriteLine($"{showcase.Length} diagram types: {readmeCount} README sections, docs, {count} landing variants, hero.");

// Renders every "### <type>" section under "## Supported Diagrams" from its first RenderSvg("""…""") sample and puts a
// light / dark <picture> right after that code block (replacing any image the section had).
int RenderReadme()
{
	const string rawBase = "https://raw.githubusercontent.com/nullean/mermaider/main/.github/readme/";
	var readmePath = Path.Combine(root, "README.md");
	var lines = File.ReadAllLines(readmePath).ToList();
	var start = lines.FindIndex(l => l == "## Supported Diagrams");
	var end = lines.FindIndex(start, l => l.StartsWith("## ", StringComparison.Ordinal) && l != "## Supported Diagrams");
	var rendered = 0;
	for (var i = start; i < end; i++)
	{
		if (!lines[i].StartsWith("### ", StringComparison.Ordinal))
			continue;

		var heading = lines[i][4..].Trim();
		var type = heading switch
		{
			"User Journey" => "journey",
			"XY Chart" => "xychart",
			_ => heading.Split(' ')[0].ToLowerInvariant(),
		};
		var next = lines.FindIndex(i + 1, l => l.StartsWith("### ", StringComparison.Ordinal) || l.StartsWith("## ", StringComparison.Ordinal));
		if (next < 0 || next > end)
			next = end;

		// drop the section's existing diagram images
		for (var j = next - 1; j > i; j--)
		{
			if (lines[j].Contains("docs/screenshots/", StringComparison.Ordinal) || lines[j].Contains(".github/readme/", StringComparison.Ordinal))
			{
				lines.RemoveAt(j);
				if (j < lines.Count && j - 1 > i && lines[j - 1].Length == 0 && (j >= lines.Count || lines[j].Length == 0))
					lines.RemoveAt(j - 1);
				next--;
				end--;
			}
		}

		var open = lines.FindIndex(i, l => l.Contains("RenderSvg(\"\"\"", StringComparison.Ordinal));
		if (open < 0 || open >= next)
			continue;
		var close = lines.FindIndex(open + 1, l => l.TrimStart().StartsWith("\"\"\"", StringComparison.Ordinal));
		var fence = lines.FindIndex(close, l => l.StartsWith("```", StringComparison.Ordinal));
		var body = lines.Skip(open + 1).Take(close - open - 1).ToList();
		var indent = body.Where(l => l.Trim().Length > 0).Min(l => l.Length - l.TrimStart().Length);
		var source = string.Join('\n', body.Select(l => l.Length >= indent ? l[indent..] : l.TrimStart()));

		Write($".github/readme/{type}.light.svg", MermaidRenderer.RenderSvg(source, Options("zinc-light", DiagramStyle.Quiet, transparent: true)));
		Write($".github/readme/{type}.dark.svg", MermaidRenderer.RenderSvg(source, Options("zinc-dark", DiagramStyle.Quiet, transparent: true)));
		var picture =
			$"<p align=\"center\"><picture><source media=\"(prefers-color-scheme: dark)\" srcset=\"{rawBase}{type}.dark.svg\" /><img src=\"{rawBase}{type}.light.svg\" alt=\"{heading}\" /></picture></p>";
		lines.InsertRange(fence + 1, ["", picture]);
		end += 2;
		rendered++;
	}

	File.WriteAllText(readmePath, string.Join('\n', lines) + "\n");
	return rendered;
}

static string FindRoot()
{
	var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
	while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "mermaid-dotnet.slnx")))
		dir = dir.Parent;
	return dir?.FullName ?? throw new InvalidOperationException("run from inside the repository");
}
