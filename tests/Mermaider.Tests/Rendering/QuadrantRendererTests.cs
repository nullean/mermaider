using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

public class QuadrantRendererTests
{
	private const string FullChart = """
		quadrantChart
		title Priority Matrix
		x-axis Low Effort --> High Effort
		y-axis Low Impact --> High Impact
		quadrant-1 Do First
		quadrant-2 Schedule
		quadrant-3 Delegate
		quadrant-4 Eliminate
		Feature A: [0.8, 0.9]
		Feature B: [0.2, 0.3]
		""";

	[Test]
	public void Renders_valid_svg()
	{
		var svg = MermaidRenderer.RenderSvg(FullChart);

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Contains_title()
	{
		var svg = MermaidRenderer.RenderSvg(FullChart);

		svg.Should().Contain("Priority Matrix");
	}

	[Test]
	public void Contains_quadrant_labels()
	{
		var svg = MermaidRenderer.RenderSvg(FullChart);

		svg.Should().Contain("Do First");
		svg.Should().Contain("Schedule");
		svg.Should().Contain("Delegate");
		svg.Should().Contain("Eliminate");
	}

	[Test]
	public void Contains_axis_labels()
	{
		var svg = MermaidRenderer.RenderSvg(FullChart);

		svg.Should().Contain("Low Effort");
		svg.Should().Contain("High Effort");
		svg.Should().Contain("Low Impact");
		svg.Should().Contain("High Impact");
	}

	[Test]
	public void Contains_points()
	{
		var svg = MermaidRenderer.RenderSvg(FullChart);

		svg.Should().Contain("<circle");
		svg.Should().Contain("Feature A");
		svg.Should().Contain("Feature B");
	}

	[Test]
	public void Renders_without_points()
	{
		var svg = MermaidRenderer.RenderSvg("""
			quadrantChart
			title Empty
			quadrant-1 Q1
			quadrant-2 Q2
			quadrant-3 Q3
			quadrant-4 Q4
			""");

		svg.Should().StartWith("<svg");
		svg.Should().Contain("Q1");
	}

	[Test]
	public void Renders_minimal_chart()
	{
		var svg = MermaidRenderer.RenderSvg("""
			quadrantChart
			Feature A: [0.5, 0.5]
			""");

		svg.Should().StartWith("<svg");
		svg.Should().Contain("Feature A");
	}
}
