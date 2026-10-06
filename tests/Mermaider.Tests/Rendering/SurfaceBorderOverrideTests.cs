using System.Text.RegularExpressions;
using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Theming;

namespace Mermaider.Tests.Rendering;

/// <summary>
/// <see cref="RenderOptions.Surface"/> / <see cref="RenderOptions.Border"/>: optional explicit fill and outline for ordinary
/// boxes (the <c>Default</c> role's family), in every preset, without touching other clusters, roles, the accent or neutrals.
/// </summary>
public partial class SurfaceBorderOverrideTests
{
	private const string Surface = "#fefce8";
	private const string Border = "#a16207";
	private const string SurfaceFill = "fill=\"var(--surface)\"";
	private const string BorderStroke = "stroke=\"var(--border)\"";

	public static IEnumerable<DiagramStyle> Styles() => [DiagramStyle.Quiet, DiagramStyle.Blueprint, DiagramStyle.Tonal];

	private static string Render(string source, DiagramStyle style = DiagramStyle.Quiet, string? surface = Surface, string? border = Border, string? line = null) =>
		MermaidRenderer.RenderSvg(source, new RenderOptions { Style = style, Surface = surface, Border = border, Line = line });

	/// <summary>The first shape element inside the <c>&lt;g&gt;</c> carrying <c>data-id="<paramref name="id"/>"</c>.</summary>
	private static string Shape(string svg, string id)
	{
		var m = Regex.Match(svg, "data-id=\"" + Regex.Escape(id) + "\"[^>]*>\\s*<(rect|polygon|path|circle)[^>]*>");
		m.Success.Should().BeTrue($"node {id} is rendered");
		return m.Value;
	}

	private static int Count(string svg, string needle) => Regex.Count(svg, Regex.Escape(needle));

	private const string Flow = "flowchart TD\n  A[One] --> B[Two]\n  A --> X{Decide}\n  A --> S[Ok]:::success\n  A --> T([Stop])\n  C[Three] --> D[Four]";

	[Test]
	[MethodDataSource(nameof(Styles))]
	public void Surface_and_border_paint_the_default_family_boxes(DiagramStyle style)
	{
		var svg = Render(Flow, style);

		foreach (var id in new[] { "A", "B" })
		{
			var shape = Shape(svg, id);
			shape.Should().Contain(SurfaceFill, "Surface wins over the preset fill");
			if (style == DiagramStyle.Tonal)
				shape.Should().Contain("stroke=\"none\"", "Tonal draws no outlines, so Border has nothing to paint");
			else
				shape.Should().Contain(BorderStroke);
		}

		svg.Should().Contain($"--surface:{Surface}").And.Contain($"--border:{Border}");
	}

	[Test]
	public void The_quiet_gradient_is_replaced_by_the_solid_surface()
	{
		var svg = Render("flowchart TD\n  A --> B");

		svg.Should().NotContain("ng-p0", "the default family no longer registers a node gradient");
		Shape(svg, "A").Should().Contain(SurfaceFill);
	}

	[Test]
	[MethodDataSource(nameof(Styles))]
	public void Other_clusters_roles_accent_and_neutrals_keep_their_derived_paint(DiagramStyle style)
	{
		var svg = Render(Flow, style);

		foreach (var id in new[] { "X", "S", "T", "C", "D" })
			Shape(svg, id).Should().NotContain("var(--surface)", $"{id} is not an ordinary box").And.NotContain("var(--border)");
	}

	[Test]
	public void Containers_keep_their_derived_paint()
	{
		var svg = Render("flowchart TD\n  subgraph G[Group]\n    A --> B\n  end");

		var container = MyRegex().Match(svg).Value;
		container.Should().NotBeEmpty();
		container.Should().NotContain("var(--surface)").And.NotContain("var(--border)");
	}

	[Test]
	public void Defaults_do_not_reference_surface_or_border()
	{
		var svg = Render(Flow, surface: null, border: null);

		svg.Should().NotContain("var(--surface)").And.NotContain("var(--border)").And.NotContain("--surface:").And.NotContain("--border:");
	}

	[Test]
	[MethodDataSource(nameof(Styles))]
	public void Entity_headers_take_the_surface_and_frames_the_border(DiagramStyle style)
	{
		var sources = new[]
		{
			"erDiagram\n  CUSTOMER ||--o{ ORDER : places\n  CUSTOMER {\n    string name\n  }\n  ORDER {\n    int id\n  }",
			"classDiagram\n  class Animal {\n    +name\n    +eat()\n  }\n  Animal <|-- Dog",
			"requirementDiagram\n  requirement r1 {\n    id: 1\n    text: hi\n  }\n  element e1 {\n    type: sim\n  }\n  e1 - satisfies -> r1",
		};

		foreach (var source in sources)
		{
			var svg = Render(source, style);
			Count(svg, SurfaceFill).Should().Be(2, "both connected entities are the default family");
			if (style == DiagramStyle.Tonal)
				svg.Should().NotContain(BorderStroke, "Tonal cards draw no outline");
			else
				svg.Should().Contain(BorderStroke);
		}
	}

	[Test]
	[MethodDataSource(nameof(Styles))]
	public void Participants_services_blocks_and_people_honour_the_override(DiagramStyle style)
	{
		var sources = new[]
		{
			"sequenceDiagram\n  Alice->>Bob: hi",
			"architecture-beta\n  service db(database)[DB]\n  service api(server)[API]\n  db:L -- R:api",
			"block-beta\n  a b c",
			"stateDiagram-v2\n  [*] --> A\n  A --> B",
			"C4Context\n  Person(c, \"Cust\")\n  System(s, \"Sys\")\n  Rel(c, s, \"uses\")",
		};

		foreach (var source in sources)
			Render(source, style).Should().Contain(SurfaceFill, source.Split('\n')[0]);
	}

	[Test]
	public void Charts_keep_their_series_colours()
	{
		var svg = Render("pie\n  \"A\" : 1\n  \"B\" : 2");

		svg.Should().NotContain("var(--surface)").And.NotContain("var(--border)");
	}

	[Test]
	public void The_line_follows_the_border_when_line_is_not_set()
	{
		Render("flowchart TD\n  A --> B").Should().Contain($"--line:{Border}");
		// a theme picked in the diagram source spells out its line; the caller's border still wins over it
		Render("%%{init: {\"theme\": \"nord\"}}%%\nflowchart TD\n  A --> B").Should().Contain($"--line:{Border}");
	}

	[Test]
	public void An_explicit_line_wins_over_the_border()
	{
		Render("flowchart TD\n  A --> B", line: "#ff8800").Should().Contain("--line:#ff8800");

		var colors = Themes.Default with { Line = null, Border = Border };
		colors.ResolvedLine.Should().Be(Border);
		(colors with { Line = "#ff8800" }).ResolvedLine.Should().Be("#ff8800");
	}

	[Test]
	public void No_built_in_theme_sets_surface_or_border()
	{
		foreach (var (name, theme) in Themes.BuiltIn.Append(new("default", Themes.Default)))
		{
			theme.Surface.Should().BeNull(name);
			theme.Border.Should().BeNull(name);
			theme.Line.Should().Be(ColorUtils.MixHex(theme.Default!, theme.Fg, ColorFamily.StrokeRatio / 100), $"{name} spells out its line as the default box border");
		}
	}

	[Test]
	[Arguments("red;}</style><script>alert(1)</script>")]
	[Arguments("url(javascript:alert(1))")]
	[Arguments("expression(alert(1))")]
	public void Unsafe_values_are_rejected(string unsafeValue)
	{
		var svg = Render(Flow, surface: unsafeValue, border: unsafeValue);

		svg.Should().NotContain("var(--surface)").And.NotContain("var(--border)").And.NotContain("alert");
		svg.Should().NotContain("--surface:").And.NotContain("--border:");
		svg.Should().Contain($"--line:{Themes.Default.Line}", "a rejected border leaves the theme's line in place");
	}

	[Test]
	public void Strict_mode_keeps_the_caller_override()
	{
		var svg = MermaidRenderer.RenderSvg(Flow, new RenderOptions
		{
			Surface = Surface,
			Border = Border,
			Strict = new StrictStylingOptions { AllowedClasses = [] },
		});

		Shape(svg, "A").Should().Contain(SurfaceFill).And.Contain(BorderStroke);
	}

	[GeneratedRegex("<g class=\"subgraph\"[^>]*>\\s*<rect[^>]*>")]
	private static partial Regex MyRegex();
}
