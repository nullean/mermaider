using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

public class RadarRendererTests
{
	private const string BasicRadar = """
		radar-beta
		title Skills Assessment
		axis Design, Frontend, Backend, DevOps, Testing
		curve c1["Team A"]{4, 3, 5, 2, 4}
		curve c2["Team B"]{3, 5, 2, 4, 3}
		max 5
		""";

	[Test]
	public void Renders_valid_svg()
	{
		var svg = MermaidRenderer.RenderSvg(BasicRadar);

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Contains_title()
	{
		var svg = MermaidRenderer.RenderSvg(BasicRadar);

		svg.Should().Contain("Skills Assessment");
	}

	[Test]
	public void Contains_axis_labels()
	{
		var svg = MermaidRenderer.RenderSvg(BasicRadar);

		svg.Should().Contain("Design");
		svg.Should().Contain("Frontend");
		svg.Should().Contain("Backend");
		svg.Should().Contain("DevOps");
		svg.Should().Contain("Testing");
	}

	[Test]
	public void Contains_curve_polygons()
	{
		var svg = MermaidRenderer.RenderSvg(BasicRadar);

		svg.Should().Contain("<polygon");
		svg.Should().Contain("fill-opacity");
	}

	[Test]
	public void Contains_legend()
	{
		var svg = MermaidRenderer.RenderSvg(BasicRadar);

		svg.Should().Contain("Team A");
		svg.Should().Contain("Team B");
	}

	[Test]
	public void Contains_graticule_circles()
	{
		var svg = MermaidRenderer.RenderSvg(BasicRadar);

		svg.Should().Contain("opacity=\"0.5\"");
	}

	[Test]
	public void Renders_polygon_graticule()
	{
		var svg = MermaidRenderer.RenderSvg("""
			radar-beta
			axis A, B, C
			curve c1{1, 2, 3}
			graticule polygon
			""");

		svg.Should().StartWith("<svg");
	}
}
