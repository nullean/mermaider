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
}
