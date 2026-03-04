using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

public class SvgRendererTests
{
	[Test]
	public void RendersSvgWithXmlHeader()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			  A --> B
			""");

		svg.Should().StartWith("<svg xmlns=");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void SvgContainsNodes()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			  A[Hello] --> B[World]
			""");

		svg.Should().Contain("data-id=\"A\"");
		svg.Should().Contain("data-id=\"B\"");
		svg.Should().Contain("Hello");
		svg.Should().Contain("World");
	}

	[Test]
	public void SvgContainsEdges()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			  A --> B
			""");

		svg.Should().Contain("class=\"edge\"");
		svg.Should().Contain("data-from=\"A\"");
		svg.Should().Contain("data-to=\"B\"");
	}

	[Test]
	public void SvgContainsStyleBlock()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			  A --> B
			""");

		svg.Should().Contain("<style>");
		svg.Should().Contain("</style>");
		svg.Should().Contain("color-mix");
	}

	[Test]
	public void RespectCustomColors()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			  A --> B
			""", new()
		{
			Bg = "#1e1e2e",
			Fg = "#cdd6f4",
		});

		svg.Should().Contain("--bg:#1e1e2e");
		svg.Should().Contain("--fg:#cdd6f4");
	}

	[Test]
	public void TransparentBackgroundByDefault()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			  A --> B
			""");

		svg.Should().NotContain("background:var(--bg)");
	}

	[Test]
	public void OpaqueBackgroundWhenTransparentDisabled()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			  A --> B
			""", new() { Transparent = false });

		svg.Should().Contain("background:var(--bg)");
	}

	[Test]
	public void LinkStyleAppliesStrokeToEdge()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph LR
			  A --> B
			  linkStyle 0 stroke:#ff3,stroke-width:4px
			""");

		svg.Should().Contain("stroke=\"#ff3\"");
		svg.Should().Contain("stroke-width=\"4px\"");
	}

	[Test]
	public void LinkStyleAppliesDasharray()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph LR
			  A --> B
			  linkStyle 0 stroke-dasharray:5 5
			""");

		svg.Should().Contain("stroke-dasharray=\"5 5\"");
	}

	[Test]
	public void LinkStyleDefaultAppliesStrokeToAllEdges()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph LR
			  A --> B
			  B --> C
			  linkStyle default stroke:#333,stroke-width:1px
			""");

		// Both edges should have the custom stroke
		svg.Should().Contain("stroke=\"#333\"");
		svg.Should().Contain("stroke-width=\"1px\"");
	}

	[Test]
	public void LinkStyleColorApplesToEdgeLabel()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph LR
			  A -->|yes| B
			  linkStyle 0 color:red
			""");

		svg.Should().Contain("fill=\"red\"");
	}
}
