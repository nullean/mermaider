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
	private static string Cluster0 => Themes.Default.AutoPaletteAt(0);

	[Test]
	public void Flowchart_node_is_a_gradient_of_its_cluster_family_with_a_family_outline()
	{
		var svg = MermaidRenderer.RenderSvg("flowchart TD\n  A --> B");

		svg.Should().Contain(DesignContract.NodeGradientStop(Cluster0));
		svg.Should().Contain(DesignContract.Outline(Cluster0));
	}

	[Test]
	public void Gradient_off_fills_nodes_with_the_flat_stage()
	{
		var svg = DesignContract.Render("flowchart TD\n  A --> B", gradient: false);

		svg.Should().NotContain("<linearGradient");
		svg.Should().Contain($"fill=\"{DesignContract.Family(Cluster0).Flat}\"");
	}

	[Test]
	public void Subgraph_is_the_shared_container_with_its_title_in_the_family_ink()
	{
		var svg = MermaidRenderer.RenderSvg("flowchart TD\n  subgraph G[Group]\n    A --> B\n  end");

		var groupColour = Themes.Default.AutoPaletteAt(1);
		svg.Should().Contain($"<g class=\"subgraph\"");
		svg.Should().Contain(DesignContract.Band(groupColour), "the header strip is the family band");
		svg.Should().NotContain(DesignContract.AccentMark, "container headers carry no accent mark, only the family band and ink");
		svg.Should().MatchRegex(DesignContract.InkTextRegex(groupColour, "Group"), "the title is drawn in the family ink");
	}

	[Test]
	public void Edge_labels_are_the_same_pill_in_every_diagram_type()
	{
		MermaidRenderer.RenderSvg("flowchart TD\n  A -->|yes| B").Should().Contain(DesignContract.Pill);
		MermaidRenderer.RenderSvg("stateDiagram-v2\n  [*] --> A\n  A --> B : go").Should().Contain(DesignContract.Pill);
		MermaidRenderer.RenderSvg("erDiagram\n  A ||--o{ B : has").Should().Contain(DesignContract.Pill);
		MermaidRenderer.RenderSvg("classDiagram\n  A --> B : uses").Should().Contain(DesignContract.Pill);
	}

	[Test]
	public void State_node_is_a_cluster_gradient_like_a_flowchart_node_and_terminals_are_accent()
	{
		var svg = MermaidRenderer.RenderSvg("stateDiagram-v2\n  [*] --> A\n  A --> B\n  B --> [*]");

		svg.Should().Contain(DesignContract.NodeGradientStop(Cluster0));
		svg.Should().Contain(DesignContract.Outline(Cluster0));
		svg.Should().Contain("<circle", "start / end are terminals");
		svg.Should().Contain(DesignContract.AccentMark, "terminals are drawn in the accent");
		svg.Should().NotContain("fill=\"var(--_text)\" stroke=\"none\"", "terminals are no longer heavy black dots");
	}

	[Test]
	public void Each_style_preset_paints_a_flowchart_differently_with_the_same_geometry()
	{
		const string src = "flowchart TD\n  subgraph G[Group]\n    A -->|go| B\n  end";
		var quiet = DesignContract.Render(src, Models.DiagramStyle.Quiet);
		var blueprint = DesignContract.Render(src, Models.DiagramStyle.Blueprint);
		var tonal = DesignContract.Render(src, Models.DiagramStyle.Tonal);

		static string ViewBox(string svg) => System.Text.RegularExpressions.Regex.Match(svg, "viewBox=\"([^\"]+)\"").Groups[1].Value;
		ViewBox(blueprint).Should().Be(ViewBox(quiet), "presets only change paint, never layout");
		ViewBox(tonal).Should().Be(ViewBox(quiet));

		blueprint.Should().Contain("stroke-dasharray=\"3 4\"", "blueprint containers are dashed outlines");
		blueprint.Should().Contain("paint-order=\"stroke\"", "blueprint labels are halo text, not pills");
		blueprint.Should().Contain("fill=\"var(--bg)\" stroke=\"" + DesignContract.Family(Cluster0).Stroke, "blueprint nodes are knocked out");
		tonal.Should().Contain($"fill=\"{DesignContract.Family(Cluster0).Soft}\" stroke=\"none\"", "tonal nodes are soft blocks without outlines");
		tonal.Should().Contain("fill=\"var(--_key-badge)\" stroke=\"none\"", "tonal labels are filled chips");
	}

	[Test]
	public void Two_differently_themed_diagrams_never_share_a_gradient_or_marker_id()
	{
		var light = MermaidRenderer.RenderSvg("flowchart TD\n  A --> B");
		var dark = MermaidRenderer.RenderSvg("flowchart TD\n  A --> B", new Models.RenderOptions { Bg = "#18181B", Fg = "#FAFAFA" });

		static IEnumerable<string> Ids(string svg) => System.Text.RegularExpressions.Regex.Matches(svg, " id=\"([^\"]+)\"").Select(m => m.Groups[1].Value);
		Ids(light).Should().NotBeEmpty();
		Ids(light).Intersect(Ids(dark)).Should().BeEmpty();
	}
}
