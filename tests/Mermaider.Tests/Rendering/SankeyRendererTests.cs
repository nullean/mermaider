using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

public class SankeyRendererTests
{
	private const string Basic = """
		sankey-beta
		Electricity grid,Over generation / exports,104.453
		Electricity grid,Heating and cooling - homes,113.726
		Electricity grid,Industry,342.165
		""";

	[Test]
	public void Renders_valid_svg()
	{
		var svg = MermaidRenderer.RenderSvg(Basic);

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Contains_node_labels()
	{
		var svg = MermaidRenderer.RenderSvg(Basic);

		svg.Should().Contain("Electricity grid");
		svg.Should().Contain("Industry");
		svg.Should().Contain("Heating and cooling - homes");
	}

	[Test]
	public void Draws_links_and_nodes()
	{
		var svg = MermaidRenderer.RenderSvg(Basic);

		svg.Should().Contain("<path");
		svg.Should().Contain("<rect");
		svg.Should().Contain("fill-opacity");
	}

	[Test]
	public void Uses_theme_text_for_labels()
	{
		var svg = MermaidRenderer.RenderSvg(Basic);

		svg.Should().Contain("fill=\"var(--_text)\"");
	}

	[Test]
	public void Renders_empty_diagram()
	{
		var svg = MermaidRenderer.RenderSvg("sankey-beta");

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Accessibility_role()
	{
		var svg = MermaidRenderer.RenderSvg("""
			sankey-beta
			accTitle: Energy flow
			A,B,1
			""");

		svg.Should().Contain("aria-roledescription=\"sankey diagram\"");
		svg.Should().Contain("Energy flow");
	}
}
