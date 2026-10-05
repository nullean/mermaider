using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

public partial class BlockRendererTests
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
		svg.Should().Contain($"stroke=\"{Mermaider.Rendering.VisualLanguage.Border(Theming.Themes.Default.AutoPaletteAt(0))}\"", "blocks take their cluster colour");
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

		svg.Should().Contain("<path class=\"block-edge\" data-from=\"A\" data-to=\"B\"");
		svg.Should().MatchRegex("marker-end=\"url\\(#m[0-9a-f]{8}-mk-arrow\\)\"", "edges use the shared arrow marker");
		svg.Should().Contain("stroke=\"var(--_line)\"");
	}

	[Test]
	public void Rounded_nodes_use_larger_rx()
	{
		var svg = MermaidRenderer.RenderSvg("""
			block-beta
			columns 2
			  R("Round") S["Square"]
			""");

		svg.Should().Contain("rx=\"14\"", "a rounded block is the preset node radius plus 6");
		svg.Should().Contain("rx=\"8\"", "a plain block takes the preset node radius");
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

	[Test]
	public void Edge_labels_are_pills()
	{
		var svg = MermaidRenderer.RenderSvg("""
			block-beta
			columns 2
			  A["A"] B["B"]
			  A -- "sends" --> B
			""");

		svg.Should().Contain(">sends</text>");
		svg.Should().Contain("<rect ", "the label sits on a pill");
	}

	[Test]
	public void Spanning_blocks_cover_their_columns()
	{
		var svg = MermaidRenderer.RenderSvg("""
			block-beta
			columns 3
			  Wide["Wide"]:3
			  A B C
			""");

		var widths = MyRegex().Matches(svg)
			.Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();
		widths.Max().Should().BeGreaterThan(widths.Min() * 2.5, "a three-column block is as wide as three cells and their gaps");
	}

	[Test]
	public void Edges_never_cross_other_blocks()
	{
		var svg = MermaidRenderer.RenderSvg("""
			block-beta
			columns 3
			  A B C
			  A --> C
			""");

		svg.Should().Contain("<path class=\"block-edge\"");
		// not a straight line through B: the route leaves through the channel above the row
		var path = System.Text.RegularExpressions.Regex.Match(svg, "class=\"block-edge\"[^>]*\\sd=\"([^\"]+)\"").Groups[1].Value;
		path.Should().Contain("Q");
	}

	[System.Text.RegularExpressions.GeneratedRegex("<rect [^>]*\\swidth=\"([0-9.]+)\"")]
	private static partial System.Text.RegularExpressions.Regex MyRegex();
}
