using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

public class VennRendererTests
{
	[Test]
	public void Renders_valid_svg()
	{
		var svg = MermaidRenderer.RenderSvg("""
			venn-beta
			set A["Frontend"]
			set B["Backend"]
			union A, B["Full Stack"]
			""");

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Contains_set_labels()
	{
		var svg = MermaidRenderer.RenderSvg("""
			venn-beta
			set A["Frontend"]
			set B["Backend"]
			""");

		svg.Should().Contain("Frontend");
		svg.Should().Contain("Backend");
	}

	[Test]
	public void Contains_circles()
	{
		var svg = MermaidRenderer.RenderSvg("""
			venn-beta
			set A
			set B
			""");

		svg.Should().Contain("<circle");
		svg.Should().Contain("fill-opacity");
	}

	[Test]
	public void Contains_union_label()
	{
		var svg = MermaidRenderer.RenderSvg("""
			venn-beta
			set A["Frontend"]
			set B["Backend"]
			union A, B["Full Stack"]
			""");

		svg.Should().Contain("Full Stack");
	}

	[Test]
	public void Renders_three_set_venn()
	{
		var svg = MermaidRenderer.RenderSvg("""
			venn-beta
			set A["Design"]
			set B["Code"]
			set C["Testing"]
			union A, B["Design + Code"]
			""");

		svg.Should().StartWith("<svg");
		svg.Should().Contain("Design");
		svg.Should().Contain("Code");
		svg.Should().Contain("Testing");
	}

	[Test]
	public void Renders_single_set()
	{
		var svg = MermaidRenderer.RenderSvg("""
			venn-beta
			set A["Only Set"]
			""");

		svg.Should().StartWith("<svg");
		svg.Should().Contain("Only Set");
	}
}
