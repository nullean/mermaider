using System.Text.RegularExpressions;
using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Rendering;
using Mermaider.Theming;

namespace Mermaider.Tests.Rendering;

/// <summary>
/// (structure diagram types: class, ER, requirement.) The design-system contract for entity-like diagrams: every box is the
/// shared entity frame in its cluster family, class / ER / requirement rows share one column grid (muted mono type | name),
/// PK is an accent badge and other keys neutral, requirement risk is a role chip (dot + word), namespaces are the shared
/// container titled in the family ink, relationship markers come from the shared marker set in the line colour, and every
/// label (edge label, cardinality) is the shared edge label. It pins "uses the shared component", not hues.
/// </summary>
public class VisualLanguageContractStructureTests
{
	private static string Cluster0 => DesignContract.Cluster(0);

	private const string Er = "erDiagram\n  A ||--o{ B : has\n  A {\n    int id PK\n    string name UK\n  }\n  B {\n    int id PK\n  }";

	[Test]
	public void Er_entity_is_the_shared_entity_frame_in_its_cluster_family()
	{
		var svg = MermaidRenderer.RenderSvg(Er);

		svg.Should().Contain(DesignContract.Outline(Cluster0));
		svg.Should().Contain($"stop-color=\"{DesignContract.Band(Cluster0)}\"", "the Quiet header band is the family band (gradient on)");
	}

	[Test]
	public void Class_box_uses_the_same_frame_and_header_band_as_an_er_entity()
	{
		var svg = MermaidRenderer.RenderSvg("classDiagram\n  class A {\n    +int id\n  }\n  A --> B");

		svg.Should().Contain(DesignContract.Outline(Cluster0));
		svg.Should().Contain($"stop-color=\"{DesignContract.Band(Cluster0)}\"");
	}

	[Test]
	public void Namespaces_and_composite_states_are_the_shared_container_titled_in_the_family_ink()
	{
		foreach (var (source, marker, title) in new[]
		{
			("classDiagram\n  namespace N {\n    class A\n  }\n  A --> B", "<g class=\"subgraph ns-box\"", "N"),
			("stateDiagram-v2\n  state S {\n    [*] --> X\n  }", "<g class=\"subgraph\"", "S"),
		})
		{
			var svg = MermaidRenderer.RenderSvg(source);
			svg.Should().Contain(marker);
			var group = svg[svg.IndexOf(marker, StringComparison.Ordinal)..];
			// the container border is the family edge stage; its title is the ink stage of the same family
			var edge = Regex.Match(group, "<rect [^>]*stroke=\"color-mix\\(in srgb, (#[0-9A-Fa-f]{6}) 42%, var\\(--bg\\)\\)\"");
			edge.Success.Should().BeTrue("the container border is a family edge stage");
			Regex.IsMatch(svg, DesignContract.InkTextRegex(edge.Groups[1].Value, title)).Should().BeTrue("the title is drawn in the family ink");
			svg.Should().NotContain("height=\"12\" rx=\"1.5\" ry=\"1.5\" fill=\"var(--accent", "container headers carry no accent mark");
		}
	}

	[Test]
	public void Namespace_never_takes_the_colour_of_a_member()
	{
		var svg = MermaidRenderer.RenderSvg("classDiagram\n  namespace N {\n    class A\n  }\n  A --> B");

		var group = svg[svg.IndexOf("<g class=\"subgraph ns-box\"", StringComparison.Ordinal)..];
		Regex.Match(group, "stroke=\"([^\"]+)\"").Groups[1].Value.Should().NotBe(DesignContract.Family(Cluster0).Edge);
	}

	[Test]
	public void Class_relationship_markers_are_the_shared_markers_in_the_line_colour()
	{
		var svg = MermaidRenderer.RenderSvg("classDiagram\n  A <|-- B\n  C *-- D\n  E o-- F\n  G ..> H");

		svg.Should().NotContain("var(--_arrow)", "markers are drawn in the line colour like every other edge");
		svg.Should().MatchRegex("<marker id=\"[^\"]*mk-triangle");
		svg.Should().MatchRegex("<marker id=\"[^\"]*mk-diamond[-\"]");
		svg.Should().MatchRegex("<marker id=\"[^\"]*mk-diamondhollow");
		svg.Should().MatchRegex("<marker id=\"[^\"]*mk-open");
		svg.Should().Contain($"stroke-dasharray=\"{DesignSystem.DashArray}\"", "dependency is dashed");
	}

	[Test]
	public void Class_cardinalities_and_labels_are_shared_edge_labels()
	{
		var svg = MermaidRenderer.RenderSvg("classDiagram\n  A \"1\" --> \"*\" B : owns");

		Regex.Matches(svg, Regex.Escape(DesignContract.Pill)).Count.Should().Be(3, "label + two cardinalities");
	}

	[Test]
	public void Class_and_er_rows_share_one_column_grid()
	{
		var cls = MermaidRenderer.RenderSvg("classDiagram\n  class A {\n    +int id\n  }");
		var er = MermaidRenderer.RenderSvg("erDiagram\n  A {\n    int id\n  }");

		// type column: meta (xs, muted) mono; name column: body mono
		const string type = "class=\"mono\" text-anchor=\"start\" font-size=\"var(--fs-xs)\" font-weight=\"400\" fill=\"var(--_text-muted)\"";
		foreach (var svg in new[] { cls, er })
		{
			svg.Should().Contain(type);
			svg.Should().MatchRegex("class=\"mono\" text-anchor=\"start\" font-size=\"var\\(--fs-s\\)\" font-weight=\"400\" fill=\"var\\(--_text\\)\"[^>]*>id<");
		}

		// the visibility sign sits in the gutter before the name, in the family ink
		cls.Should().MatchRegex(Regex.Escape($"fill=\"{DesignContract.Family(Cluster0).Ink}\"") + "[^>]*>\\+<");
	}

	[Test]
	public void Pk_is_an_accent_badge_and_other_keys_are_neutral()
	{
		var svg = MermaidRenderer.RenderSvg(Er);
		var accent = ColorFamily.Accent();
		var neutral = ColorFamily.Neutral();

		svg.Should().Contain($"fill=\"{accent.Band}\"");
		svg.Should().MatchRegex(Regex.Escape($"fill=\"{accent.Ink}\"") + "[^>]*>PK<");
		svg.Should().Contain($"fill=\"{neutral.Band}\"");
		svg.Should().MatchRegex(Regex.Escape($"fill=\"{neutral.Ink}\"") + "[^>]*>UK<");
	}

	[Test]
	public void Blueprint_keys_are_bracketed_and_crows_feet_use_the_accent()
	{
		var svg = DesignContract.Render(Er, DiagramStyle.Blueprint);

		svg.Should().Contain(">[PK]<");
		svg.Should().Contain($"stroke=\"{ColorFamily.AccentBase}\"", "thin-head presets draw markers in the accent");
	}

	[Test]
	public void Er_crows_feet_follow_the_line_colour_and_knock_out_hollow_parts()
	{
		var svg = MermaidRenderer.RenderSvg(Er);

		svg.Should().MatchRegex("<circle [^>]*fill=\"var\\(--bg\\)\" stroke=\"var\\(--_line\\)\"", "o{ is a page-filled circle in the line colour");
	}

	private const string Requirements = """
		requirementDiagram
		requirement a {
		id: 1
		risk: high
		}
		element b {
		type: x
		}
		requirement c {
		id: 2
		risk: low
		}
		element d {
		type: y
		}
		b - satisfies -> a
		d - derives -> c
		""";

	[Test]
	public void Requirement_boxes_are_cluster_coloured_entity_frames()
	{
		var svg = MermaidRenderer.RenderSvg(Requirements);

		svg.Should().Contain(DesignContract.Outline(Cluster0));
		svg.Should().Contain(DesignContract.Outline(DesignContract.Cluster(1)), "a separate cluster takes another family");
		svg.Should().Contain($"stop-color=\"{DesignContract.Band(Cluster0)}\"", "the header is the entity header band");
		svg.Should().Contain("«Requirement»").And.Contain("«Element»");
		svg.Should().NotContain("var(--_arrow)", "markers follow the line colour");
	}

	[Test]
	public void Requirement_risk_is_a_role_chip_with_dot_and_word()
	{
		var svg = MermaidRenderer.RenderSvg(Requirements);
		var failure = DesignContract.Family(Themes.Default.RoleColor(ColorRole.Failure));
		var success = DesignContract.Family(Themes.Default.RoleColor(ColorRole.Success));

		svg.Should().MatchRegex("<circle [^>]*fill=\"" + Regex.Escape(failure.Ink) + "\" />");
		svg.Should().MatchRegex(Regex.Escape($"fill=\"{failure.Ink}\"") + "[^>]*>High<", "the word accompanies the colour");
		svg.Should().MatchRegex(Regex.Escape($"fill=\"{success.Ink}\"") + "[^>]*>Low<");
	}

	[Test]
	public void Requirement_relations_follow_the_dash_language()
	{
		var svg = MermaidRenderer.RenderSvg(Requirements);

		svg.Should().MatchRegex($"data-type=\"satisfies\"[^>]*stroke-dasharray=\"{DesignSystem.DashArray}\"");
		svg.Should().MatchRegex($"data-type=\"derives\"[^>]*stroke-dasharray=\"{DesignSystem.DotArray}\" stroke-linecap=\"round\"");
		svg.Should().Contain(DesignContract.Pill, "relation labels are the shared edge label");
	}

	[Test]
	public void Requirement_title_is_left_aligned_with_the_accent_mark()
	{
		var svg = MermaidRenderer.RenderSvg("requirementDiagram\ntitle My Requirements\nrequirement a {\nid: 1\n}");

		svg.Should().Contain("<g class=\"diagram-title\">");
		svg.Should().MatchRegex("text-anchor=\"start\"[^>]*>My Requirements<");
	}

	[Test]
	public void No_literal_colours_reach_structure_diagrams()
	{
		foreach (var style in new[] { DiagramStyle.Quiet, DiagramStyle.Blueprint, DiagramStyle.Tonal })
		{
			foreach (var source in new[] { Er, Requirements, "classDiagram\n  namespace N {\n    class A {\n      +int id\n    }\n  }\n  A <|-- B\n  note for A \"hi\"" })
			{
				var svg = DesignContract.Render(source, style);
				DesignContract.HexColours(svg.Replace("color-mix(in srgb, #", "", StringComparison.Ordinal)).Should().BeEmpty($"{style}: paint comes from families and tokens");
			}
		}
	}
}
