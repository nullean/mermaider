using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

public class TreemapRendererTests
{
	[Test]
	public void Renders_valid_svg()
	{
		var svg = MermaidRenderer.RenderSvg("""
			treemap-beta
			"A": 30
			"B": 20
			"C": 50
			""");

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Contains_rectangles()
	{
		var svg = MermaidRenderer.RenderSvg("""
			treemap-beta
			"Products": 60
			"Services": 40
			""");

		svg.Should().Contain("<rect");
	}

	[Test]
	public void Contains_labels()
	{
		var svg = MermaidRenderer.RenderSvg("""
			treemap-beta
			"Products": 60
			"Services": 40
			""");

		svg.Should().Contain("Products");
		svg.Should().Contain("Services");
	}

	[Test]
	public void Contains_values()
	{
		var svg = MermaidRenderer.RenderSvg("""
			treemap-beta
			"Products": 60
			"Services": 40
			""");

		svg.Should().Contain("60");
		svg.Should().Contain("40");
	}

	[Test]
	public void Renders_nested_treemap()
	{
		var svg = MermaidRenderer.RenderSvg("""
			treemap-beta
			  "Root"
			    "A": 30
			    "B": 70
			""");

		svg.Should().StartWith("<svg");
		svg.Should().Contain("Root");
		svg.Should().Contain("A");
	}
}
