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

	[Test]
	public void Multi_bar_series_use_distinct_declaration_colors()
	{
		// Palette index 0 = #4e79a7, 1 = #f28e2b (declaration order).
		var svg = MermaidRenderer.RenderSvg("""
			xychart-beta
			x-axis [a, b]
			bar "first" [1, 2]
			bar "second" [3, 4]
			""");

		svg.Should().Contain("fill=\"#4e79a7\"");
		svg.Should().Contain("fill=\"#f28e2b\"");
		// Legend swatches match series fills by declaration index.
		svg.Should().Contain("first");
		svg.Should().Contain("second");
	}

	[Test]
	public void Bar_then_line_colors_follow_declaration_index()
	{
		var svg = MermaidRenderer.RenderSvg("""
			xychart-beta
			x-axis [a, b]
			bar "bars" [1, 2]
			line "trend" [1, 2]
			""");

		svg.Should().Contain("fill=\"#4e79a7\"");
		svg.Should().Contain("stroke=\"#f28e2b\"");
	}

	[Test]
	public void Horizontal_draws_category_on_y_and_wide_bars()
	{
		var svg = MermaidRenderer.RenderSvg("""
			xychart horizontal
			x-axis [a, b, c]
			y-axis 0 --> 10
			bar [2, 5, 8]
			""");

		svg.Should().Contain("<rect");
		// Category labels still present; bars grow along X (width > height for positive values).
		svg.Should().Contain(">a</text>");
		// Horizontal bars: width tracks value (e.g. 8/10 of plot), height is bar thickness.
		svg.Should().MatchRegex(@"width=""[1-9]\d+(?:\.\d+)?"" height=""\d+(?:\.\d+)?""");
	}
}
