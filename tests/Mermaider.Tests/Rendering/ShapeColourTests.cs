using AwesomeAssertions;
using Mermaider.Rendering;
using Mermaider.Theming;

namespace Mermaider.Tests.Rendering;

/// <summary>Shapes with a conventional meaning (decision, terminal, data store) are tinted under user styling and above the cluster colour.</summary>
public class ShapeColourTests
{
	private static string Cluster0 => Themes.Default.PaletteAt(0);

	private static string NodeRect(string svg, string id)
	{
		var start = svg.IndexOf($"data-id=\"{id}\"", StringComparison.Ordinal);
		start.Should().BeGreaterThan(0, $"node {id} should be rendered");
		var tail = svg[start..];
		var end = tail.IndexOf("</g>", StringComparison.Ordinal);
		return tail[..end];
	}

	[Test]
	public void A_decision_diamond_gets_the_accent_tint_but_keeps_the_cluster_border()
	{
		var node = NodeRect(MermaidRenderer.RenderSvg("flowchart TD\n  A[Start] --> B{Check}"), "B");

		node.Should().Contain("fill=\"var(--_accent-fill)\"");
		node.Should().Contain($"stroke=\"{VisualLanguage.Border(Cluster0)}\"");
	}

	[Test]
	public void A_terminal_stadium_is_a_neutral_tint_with_a_heavier_border()
	{
		var node = NodeRect(MermaidRenderer.RenderSvg("flowchart TD\n  A([Done]) --> B[Next]"), "A");

		node.Should().Contain("fill=\"color-mix(in srgb, var(--fg) 9%, var(--bg))\"");
		node.Should().Contain($"stroke-width=\"{RenderConstants.StrokeWidths.InnerBox + VisualLanguage.TerminalExtraStroke}\"");
	}

	[Test]
	public void A_data_store_cylinder_has_one_muted_look_whatever_the_cluster()
	{
		var node = NodeRect(MermaidRenderer.RenderSvg("flowchart TD\n  A[App] --> D[(Database)]"), "D");

		node.Should().Contain("stroke=\"var(--_text-muted)\"");
		node.Should().Contain("fill=\"color-mix(in srgb, var(--_text-muted) 18%, var(--bg))\"");
	}

	[Test]
	public void Other_shapes_keep_the_cluster_colour()
	{
		var node = NodeRect(MermaidRenderer.RenderSvg("flowchart TD\n  A[Plain] --> B((Circle))"), "B");

		node.Should().Contain($"fill=\"{VisualLanguage.Tint(Cluster0, VisualLanguage.NodeTint)}\"");
	}

	[Test]
	public void User_style_beats_the_shape_colour()
	{
		var node = NodeRect(MermaidRenderer.RenderSvg("flowchart TD\n  A --> B{Check}\n  style B fill:#abcdef"), "B");

		node.Should().Contain("fill=\"#abcdef\"");
	}

	[Test]
	public void A_role_class_beats_the_shape_colour()
	{
		var node = NodeRect(MermaidRenderer.RenderSvg("flowchart TD\n  A --> B{Check}\n  class B failure"), "B");

		node.Should().Contain($"fill=\"{VisualLanguage.Tint(CategoricalPalette.Red, VisualLanguage.NodeTint)}\"");
	}

	[Test]
	public void State_diagrams_use_cluster_colours_and_tint_the_choice_marker()
	{
		var svg = MermaidRenderer.RenderSvg("stateDiagram-v2\n  [*] --> Check\n  state c <<choice>>\n  Check --> c\n  c --> Done : ok");

		NodeRect(svg, "Check").Should().Contain($"fill=\"{VisualLanguage.Tint(Cluster0, VisualLanguage.NodeTint)}\"");
		NodeRect(svg, "c").Should().Contain("fill=\"var(--_accent-fill)\"");
	}

	[Test]
	public void State_role_classes_work_like_flowchart_ones()
	{
		var svg = MermaidRenderer.RenderSvg("stateDiagram-v2\n  [*] --> Ok\n  Ok --> Bad\n  class Ok success\n  class Bad failure");

		NodeRect(svg, "Ok").Should().Contain(VisualLanguage.Tint(CategoricalPalette.Green, VisualLanguage.NodeTint));
		NodeRect(svg, "Bad").Should().Contain(VisualLanguage.Tint(CategoricalPalette.Red, VisualLanguage.NodeTint));
	}
}
