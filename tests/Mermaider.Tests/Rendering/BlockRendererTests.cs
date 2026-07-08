using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

public class BlockRendererTests
{
	private const string SimpleGrid = """
		block-beta
		columns 3
		  A["A"] B["B"] C["C"]
		  D["D"] E["E"] F["F"]
		""";

	[Test]
	public void Renders_valid_svg()
	{
		var svg = MermaidRenderer.RenderSvg(SimpleGrid);

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Contains_all_labels()
	{
		var svg = MermaidRenderer.RenderSvg(SimpleGrid);

		foreach (var label in new[] { "A", "B", "C", "D", "E", "F" })
			svg.Should().Contain($">{label}</text>");
	}

	[Test]
	public void Draws_rects_for_nodes()
	{
		var svg = MermaidRenderer.RenderSvg(SimpleGrid);

		// 6 nodes → 6 rects
		svg.Split("<rect ", StringSplitOptions.None).Length.Should().Be(7);
	}

	[Test]
	public void Uses_theme_text_and_stroke()
	{
		var svg = MermaidRenderer.RenderSvg(SimpleGrid);

		svg.Should().Contain("fill=\"var(--_text)\"");
		svg.Should().Contain("stroke=\"var(--_node-stroke)\"");
		svg.Should().Contain("fill=\"var(--_node-fill)\"");
	}

	[Test]
	public void Renders_title()
	{
		var svg = MermaidRenderer.RenderSvg("""
			block-beta
			title Services
			columns 2
			  A["API"] B["DB"]
			""");

		svg.Should().Contain("Services");
	}

	[Test]
	public void Renders_edges()
	{
		var svg = MermaidRenderer.RenderSvg("""
			block-beta
			columns 2
			  A["A"] B["B"]
			  A --> B
			""");

		svg.Should().Contain("<line ");
		svg.Should().Contain("marker-end=\"url(#block-arrow)\"");
		svg.Should().Contain("stroke=\"var(--_line)\"");
	}

	[Test]
	public void Rounded_nodes_use_larger_rx()
	{
		var svg = MermaidRenderer.RenderSvg("""
			block-beta
			  R("Round")
			""");

		svg.Should().Contain("rx=\"10\"");
	}

	[Test]
	public void Renders_empty_block()
	{
		var svg = MermaidRenderer.RenderSvg("block-beta");

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Accessibility_role()
	{
		var svg = MermaidRenderer.RenderSvg("""
			block-beta
			accTitle: Blocks
			  A["A"]
			""");

		svg.Should().Contain("aria-roledescription=\"block diagram\"");
	}

	[Test]
	public void Detects_block_without_beta()
	{
		var svg = MermaidRenderer.RenderSvg("""
			block
			columns 1
			  A["Only"]
			""");

		svg.Should().Contain("Only");
		svg.Should().Contain("<rect ");
	}

	[Test]
	public void Space_keyword_leaves_gap_without_drawing_rect()
	{
		var svg = MermaidRenderer.RenderSvg("""
			block-beta
			columns 3
			  A["A"] space B["B"]
			""");

		// Two real nodes → two rects (plus Split's leading fragment)
		svg.Split("<rect ", StringSplitOptions.None).Length.Should().Be(3);
		svg.Should().Contain(">A</text>");
		svg.Should().Contain(">B</text>");
	}

	[Test]
	public void Literal_underscore_space_id_still_renders()
	{
		var svg = MermaidRenderer.RenderSvg("""
			block-beta
			columns 2
			  __space_0["Slot"] space
			""");

		// Real node with magic-looking id must still draw; space keyword must not
		svg.Should().Contain(">Slot</text>");
		svg.Split("<rect ", StringSplitOptions.None).Length.Should().Be(2);
	}
}
