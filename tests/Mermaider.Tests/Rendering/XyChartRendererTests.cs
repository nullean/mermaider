using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

public class XyChartRendererTests
{
	private const string Basic = """
		xychart-beta
		title "Sales Revenue"
		x-axis [jan, feb, mar, apr, may, jun]
		y-axis "Revenue (in $)" 4000 --> 11000
		bar [5000, 6000, 7500, 8200, 9500, 10500]
		line [5000, 6000, 7500, 8200, 9500, 10500]
		""";

	[Test]
	public void Renders_valid_svg()
	{
		var svg = MermaidRenderer.RenderSvg(Basic);

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Contains_title_and_labels()
	{
		var svg = MermaidRenderer.RenderSvg(Basic);

		svg.Should().Contain("Sales Revenue");
		svg.Should().Contain("jan");
		svg.Should().Contain("Revenue (in $)");
	}

	[Test]
	public void Draws_bars_and_lines()
	{
		var svg = MermaidRenderer.RenderSvg(Basic);

		svg.Should().Contain("<rect");
		svg.Should().Contain("<polyline");
		svg.Should().Contain("<circle");
	}

	[Test]
	public void Uses_theme_text()
	{
		var svg = MermaidRenderer.RenderSvg(Basic);

		svg.Should().Contain("fill=\"var(--_text)\"");
		svg.Should().Contain("stroke=\"var(--_line)\"");
	}

	[Test]
	public void Renders_empty_chart()
	{
		var svg = MermaidRenderer.RenderSvg("xychart-beta");

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Named_series_legend()
	{
		var svg = MermaidRenderer.RenderSvg("""
			xychart-beta
			x-axis [a, b]
			line "avg" [1, 2]
			line "p95" [3, 4]
			""");

		svg.Should().Contain("avg");
		svg.Should().Contain("p95");
	}

	[Test]
	public void Accessibility_role()
	{
		var svg = MermaidRenderer.RenderSvg("""
			xychart-beta
			accTitle: Revenue
			bar [1, 2, 3]
			""");

		svg.Should().Contain("aria-roledescription=\"XY chart\"");
		svg.Should().Contain("Revenue");
	}
}
