using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

public class MindmapRendererTests
{
	[Test]
	public void Renders_valid_svg()
	{
		var svg = MermaidRenderer.RenderSvg("""
			mindmap
			  root
			    Topic A
			    Topic B
			""");

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Contains_root_label()
	{
		var svg = MermaidRenderer.RenderSvg("""
			mindmap
			  ((Central Idea))
			    Topic A
			""");

		svg.Should().Contain("Central Idea");
	}

	[Test]
	public void Contains_child_labels()
	{
		var svg = MermaidRenderer.RenderSvg("""
			mindmap
			  root
			    Topic A
			    Topic B
			""");

		svg.Should().Contain("Topic A");
		svg.Should().Contain("Topic B");
	}

	[Test]
	public void Contains_links()
	{
		var svg = MermaidRenderer.RenderSvg("""
			mindmap
			  root
			    Child
			""");

		svg.Should().Contain("<path");
	}

	[Test]
	public void Renders_circle_shape()
	{
		var svg = MermaidRenderer.RenderSvg("""
			mindmap
			  ((Circle))
			""");

		svg.Should().Contain("<circle");
	}

	[Test]
	public void Renders_square_shape()
	{
		var svg = MermaidRenderer.RenderSvg("""
			mindmap
			  [Square Node]
			""");

		svg.Should().Contain("<rect");
	}

	[Test]
	public void Renders_nested_tree()
	{
		var svg = MermaidRenderer.RenderSvg("""
			mindmap
			  root
			    A
			      A1
			      A2
			    B
			      B1
			""");

		svg.Should().StartWith("<svg");
		svg.Should().Contain("A1");
		svg.Should().Contain("B1");
	}
}
