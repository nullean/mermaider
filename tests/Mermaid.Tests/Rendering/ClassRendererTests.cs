using AwesomeAssertions;

namespace Mermaid.Tests.Rendering;

public class ClassRendererTests
{
	private const string SimpleClass = """
		classDiagram
		class Animal {
		+String name
		+eat() void
		}
		class Dog
		Animal <|-- Dog
		""";

	[Test]
	public void Renders_valid_svg()
	{
		var svg = MermaidRenderer.RenderSvg(SimpleClass);

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Contains_class_nodes()
	{
		var svg = MermaidRenderer.RenderSvg(SimpleClass);

		svg.Should().Contain("class=\"class-node\"");
		svg.Should().Contain("data-id=\"Animal\"");
		svg.Should().Contain("data-id=\"Dog\"");
	}

	[Test]
	public void Contains_relationship_lines()
	{
		var svg = MermaidRenderer.RenderSvg(SimpleClass);

		svg.Should().Contain("class=\"class-relationship\"");
		svg.Should().Contain("data-type=\"inheritance\"");
	}

	[Test]
	public void Contains_marker_defs()
	{
		var svg = MermaidRenderer.RenderSvg(SimpleClass);

		svg.Should().Contain("id=\"cls-inherit\"");
		svg.Should().Contain("id=\"cls-composition\"");
		svg.Should().Contain("id=\"cls-aggregation\"");
		svg.Should().Contain("id=\"cls-arrow\"");
	}

	[Test]
	public void Renders_annotation()
	{
		var svg = MermaidRenderer.RenderSvg("""
			classDiagram
			class Shape {
			<<abstract>>
			+draw() void
			}
			""");

		svg.Should().Contain("data-annotation=\"abstract\"");
		svg.Should().Contain("&lt;&lt;abstract&gt;&gt;");
	}

	[Test]
	public void Uses_uniform_font()
	{
		var svg = MermaidRenderer.RenderSvg(SimpleClass);

		svg.Should().Contain("font-family: 'Inter'");
		svg.Should().Contain("class=\"mono\"");
	}

	[Test]
	public void Renders_relationship_with_label()
	{
		var svg = MermaidRenderer.RenderSvg("""
			classDiagram
			Customer "1" --> "*" Order : places
			""");

		svg.Should().Contain("data-label=\"places\"");
	}
}
