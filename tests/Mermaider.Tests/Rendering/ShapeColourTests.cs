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

	/// <summary>A node painted in family <paramref name="key"/> references that family's gradient.</summary>
	private static string GradientOf(string key) => $"-ng-{key})\"";

	[Test]
	public void A_decision_diamond_is_the_accent_family()
	{
		var node = NodeRect(MermaidRenderer.RenderSvg("flowchart TD\n  A[Start] --> B{Check}"), "B");

		node.Should().Contain(GradientOf("a"));
		node.Should().Contain($"stroke=\"{ColorFamily.Accent().Stroke}\"");
	}

	[Test]
	public void A_terminal_stadium_is_the_neutral_family_with_a_heavier_outline()
	{
		var node = NodeRect(MermaidRenderer.RenderSvg("flowchart TD\n  A([Done]) --> B[Next]"), "A");

		node.Should().Contain(GradientOf("n"));
		node.Should().Contain($"stroke-width=\"{StyleSpec.Quiet.OutlineWidth + 0.5}\"");
	}

	[Test]
	public void A_data_store_cylinder_is_the_neutral_family_whatever_the_cluster()
	{
		var node = NodeRect(MermaidRenderer.RenderSvg("flowchart TD\n  A[App] --> D[(Database)]"), "D");

		node.Should().Contain(GradientOf("n"));
		node.Should().Contain($"stroke=\"{ColorFamily.Neutral().Stroke}\"");
	}

	[Test]
	public void Other_shapes_keep_the_cluster_colour()
	{
		var node = NodeRect(MermaidRenderer.RenderSvg("flowchart TD\n  A[Plain] --> B((Circle))"), "B");

		node.Should().Contain(GradientOf("p0"));
		node.Should().Contain($"stroke=\"{VisualLanguage.Border(Cluster0)}\"");
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

		node.Should().Contain(GradientOf("f"));
		node.Should().Contain($"stroke=\"{VisualLanguage.Border(CategoricalPalette.Red)}\"");
	}

	[Test]
	public void State_diagrams_use_cluster_colours_and_the_choice_marker_is_the_accent_family()
	{
		var svg = MermaidRenderer.RenderSvg("stateDiagram-v2\n  [*] --> Check\n  state c <<choice>>\n  Check --> c\n  c --> Done : ok");

		NodeRect(svg, "Check").Should().Contain(GradientOf("p0"));
		NodeRect(svg, "c").Should().Contain(GradientOf("a"));
	}

	[Test]
	public void State_role_classes_work_like_flowchart_ones()
	{
		var svg = MermaidRenderer.RenderSvg("stateDiagram-v2\n  [*] --> Ok\n  Ok --> Bad\n  class Ok success\n  class Bad failure");

		NodeRect(svg, "Ok").Should().Contain(GradientOf("s"));
		NodeRect(svg, "Bad").Should().Contain(GradientOf("f"));
		svg.Should().Contain($"stop-color=\"{new ColorFamily("s", CategoricalPalette.Green).Top}\"");
	}
}
