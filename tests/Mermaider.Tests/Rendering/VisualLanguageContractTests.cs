using AwesomeAssertions;
using Mermaider.Rendering;
using Mermaider.Theming;

namespace Mermaider.Tests.Rendering;

/// <summary>
/// The structural contract of the shared visual language (see <see cref="VisualLanguage"/>): the same cluster colour is a
/// darker border plus a light tint of the same hue, subgraphs/namespaces carry their title in the border colour on a plain
/// box without a header band, and every edge label is the same pill. It deliberately does not pin which hue is chosen,
/// so palette changes do not break it, but it does fail when one diagram type stops looking like the others.
/// </summary>
public class VisualLanguageContractTests
{
	private static string Cluster0 => Themes.Default.PaletteAt(0);

	[Test]
	public void Flowchart_node_is_a_tint_of_its_cluster_colour_with_a_darker_border()
	{
		var svg = MermaidRenderer.RenderSvg("flowchart TD\n  A --> B");

		svg.Should().Contain($"fill=\"{VisualLanguage.Tint(Cluster0, VisualLanguage.NodeTint)}\"");
		svg.Should().Contain($"stroke=\"{VisualLanguage.Border(Cluster0)}\"");
	}

	[Test]
	public void Er_entity_uses_the_same_cluster_border_and_tints()
	{
		var svg = MermaidRenderer.RenderSvg("erDiagram\n  A ||--o{ B : has\n  A {\n    int id PK\n  }\n  B {\n    int id PK\n  }");

		svg.Should().Contain($"stroke=\"{VisualLanguage.Border(Cluster0)}\"");
		svg.Should().Contain(VisualLanguage.Tint(Cluster0, VisualLanguage.HeaderTint));
	}

	[Test]
	public void Subgraph_is_a_plain_box_with_its_title_in_the_border_colour()
	{
		var svg = MermaidRenderer.RenderSvg("flowchart TD\n  subgraph G[Group]\n    A --> B\n  end");

		var group = svg[svg.IndexOf("<g class=\"subgraph\"", StringComparison.Ordinal)..];
		var stroke = System.Text.RegularExpressions.Regex.Match(group, "<rect [^>]*stroke=\"([^\"]+)\"").Groups[1].Value;
		stroke.Should().NotBeNullOrEmpty();
		svg.Should().Contain($"fill=\"{stroke}\"", "the title is drawn in the box's border colour");
		System.Text.RegularExpressions.Regex.Matches(group[..group.IndexOf("</g>", StringComparison.Ordinal)], "<rect").Count
			.Should().Be(1, "a subgraph is one box, not a box plus a header band");
	}

	[Test]
	public void Edge_labels_are_the_same_pill_in_every_diagram_type()
	{
		var pill = $"fill=\"var(--bg)\" stroke=\"var(--_line)\" stroke-width=\"{RenderConstants.StrokeWidths.Connector}\"";

		MermaidRenderer.RenderSvg("flowchart TD\n  A -->|yes| B").Should().Contain(pill);
		MermaidRenderer.RenderSvg("stateDiagram-v2\n  [*] --> A\n  A --> B : go").Should().Contain(pill);
		MermaidRenderer.RenderSvg("erDiagram\n  A ||--o{ B : has").Should().Contain(pill);
		MermaidRenderer.RenderSvg("classDiagram\n  A --> B : uses").Should().Contain(pill);
	}

	[Test]
	public void Class_box_uses_the_same_cluster_border_and_header_tint_as_an_er_entity()
	{
		var svg = MermaidRenderer.RenderSvg("classDiagram\n  class A {\n    +int id\n  }\n  A --> B");

		svg.Should().Contain($"stroke=\"{VisualLanguage.Border(Cluster0)}\"");
		svg.Should().Contain($"fill=\"{VisualLanguage.Tint(Cluster0, VisualLanguage.HeaderTint)}\"");
	}

	[Test]
	public void State_node_is_a_cluster_tint_like_a_flowchart_node()
	{
		var svg = MermaidRenderer.RenderSvg("stateDiagram-v2\n  [*] --> A\n  A --> B");

		svg.Should().Contain($"fill=\"{VisualLanguage.Tint(Cluster0, VisualLanguage.NodeTint)}\"");
		svg.Should().Contain($"stroke=\"{VisualLanguage.Border(Cluster0)}\"");
	}

	[Test]
	public void Namespaces_and_composite_states_are_plain_boxes_titled_in_the_border_colour()
	{
		foreach (var (source, marker) in new[]
		{
			("classDiagram\n  namespace N {\n    class A\n  }\n  A --> B", "<g class=\"ns-box\""),
			("stateDiagram-v2\n  state S {\n    [*] --> X\n  }", "<g class=\"subgraph\""),
		})
		{
			var svg = MermaidRenderer.RenderSvg(source);
			var group = svg[svg.IndexOf(marker, StringComparison.Ordinal)..];
			var stroke = System.Text.RegularExpressions.Regex.Match(group, "<rect [^>]*stroke=\"([^\"]+)\"").Groups[1].Value;
			svg.Should().Contain($"fill=\"{stroke}\"", "the title is drawn in the box's border colour");
		}
	}

	[Test]
	public void Class_relationship_markers_follow_the_line_colour()
	{
		var svg = MermaidRenderer.RenderSvg("classDiagram\n  A <|-- B\n  C *-- D");

		svg.Should().NotContain("var(--_arrow)", "markers are drawn in the line colour like every other edge");
		svg.Should().Contain("<marker id=\"cls-composition\"");
	}

	[Test]
	public void Class_cardinalities_are_pills()
	{
		var pill = $"fill=\"var(--bg)\" stroke=\"var(--_line)\" stroke-width=\"{RenderConstants.StrokeWidths.Connector}\"";
		var svg = MermaidRenderer.RenderSvg("classDiagram\n  A \"1\" --> \"*\" B");

		System.Text.RegularExpressions.Regex.Matches(svg, System.Text.RegularExpressions.Regex.Escape(pill)).Count.Should().Be(2);
	}

	[Test]
	public void Sequence_participants_that_talk_share_a_cluster_colour()
	{
		var svg = MermaidRenderer.RenderSvg("sequenceDiagram\n  A->>B: hi\n  C->>D: yo");

		svg.Should().Contain($"fill=\"{VisualLanguage.Tint(Cluster0, VisualLanguage.NodeTint)}\" stroke=\"{VisualLanguage.Border(Cluster0)}\"");
		var second = Themes.Default.AutoPaletteAt(1);
		svg.Should().Contain($"fill=\"{VisualLanguage.Tint(second, VisualLanguage.NodeTint)}\" stroke=\"{VisualLanguage.Border(second)}\"", "an unrelated pair is a second cluster");
	}

	[Test]
	public void Sequence_frames_are_tinted_groups_with_the_keyword_in_the_border_colour()
	{
		var svg = MermaidRenderer.RenderSvg("sequenceDiagram\n  A->>B: hi\n  alt ok\n    B-->>A: yes\n  else bad\n    B-->>A: no\n  end");

		var group = svg[svg.IndexOf("<g class=\"block\"", StringComparison.Ordinal)..];
		var stroke = System.Text.RegularExpressions.Regex.Match(group, "<rect [^>]*stroke=\"([^\"]+)\"").Groups[1].Value;
		svg.Should().Contain($"fill=\"{stroke}\"", "the keyword is drawn in the frame's border colour");
		svg.Should().NotContain("var(--_arrow)", "markers follow the line colour");
	}

	[Test]
	public void Requirement_boxes_are_cluster_coloured_tables_and_unrelated_boxes_get_another_colour()
	{
		var svg = MermaidRenderer.RenderSvg("""
			requirementDiagram
			requirement a {
			id: 1
			}
			element b {
			type: x
			}
			requirement c {
			id: 2
			}
			b - satisfies -> a
			""");

		svg.Should().Contain($"stroke=\"{VisualLanguage.Border(Cluster0)}\"");
		var second = Themes.Default.AutoPaletteAt(1);
		svg.Should().Contain($"stroke=\"{VisualLanguage.Border(second)}\"", "a box without relations is its own cluster");
		svg.Should().Contain($"fill=\"{VisualLanguage.Tint(Cluster0, VisualLanguage.HeaderTint)}\"", "the header is tinted like an entity header");
		svg.Should().NotContain("var(--_arrow)", "markers follow the line colour");
	}
}
