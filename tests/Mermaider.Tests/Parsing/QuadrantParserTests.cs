using AwesomeAssertions;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

public class QuadrantParserTests
{
	[Test]
	public void Parses_title()
	{
		var lines = new[]
		{
			"quadrantChart",
			"title Priority Matrix",
		};

		var chart = QuadrantParser.Parse(lines);

		chart.Title.Should().Be("Priority Matrix");
	}

	[Test]
	public void Parses_x_axis_with_both_labels()
	{
		var lines = new[]
		{
			"quadrantChart",
			"x-axis Low Effort --> High Effort",
		};

		var chart = QuadrantParser.Parse(lines);

		chart.XAxisLeft.Should().Be("Low Effort");
		chart.XAxisRight.Should().Be("High Effort");
	}

	[Test]
	public void Parses_x_axis_with_left_only()
	{
		var lines = new[]
		{
			"quadrantChart",
			"x-axis Effort",
		};

		var chart = QuadrantParser.Parse(lines);

		chart.XAxisLeft.Should().Be("Effort");
		chart.XAxisRight.Should().BeNull();
	}

	[Test]
	public void Parses_y_axis()
	{
		var lines = new[]
		{
			"quadrantChart",
			"y-axis Low Impact --> High Impact",
		};

		var chart = QuadrantParser.Parse(lines);

		chart.YAxisBottom.Should().Be("Low Impact");
		chart.YAxisTop.Should().Be("High Impact");
	}

	[Test]
	public void Parses_quadrant_labels()
	{
		var lines = new[]
		{
			"quadrantChart",
			"quadrant-1 Do First",
			"quadrant-2 Schedule",
			"quadrant-3 Delegate",
			"quadrant-4 Eliminate",
		};

		var chart = QuadrantParser.Parse(lines);

		chart.Quadrant1.Should().Be("Do First");
		chart.Quadrant2.Should().Be("Schedule");
		chart.Quadrant3.Should().Be("Delegate");
		chart.Quadrant4.Should().Be("Eliminate");
	}

	[Test]
	public void Parses_points()
	{
		var lines = new[]
		{
			"quadrantChart",
			"Feature A: [0.8, 0.9]",
			"Feature B: [0.3, 0.2]",
		};

		var chart = QuadrantParser.Parse(lines);

		chart.Points.Should().HaveCount(2);
		chart.Points[0].Label.Should().Be("Feature A");
		chart.Points[0].X.Should().BeApproximately(0.8, 0.001);
		chart.Points[0].Y.Should().BeApproximately(0.9, 0.001);
		chart.Points[1].Label.Should().Be("Feature B");
		chart.Points[1].X.Should().BeApproximately(0.3, 0.001);
	}

	[Test]
	public void Clamps_point_values_to_0_1()
	{
		var lines = new[]
		{
			"quadrantChart",
			"Over: [1.5, -0.2]",
		};

		var chart = QuadrantParser.Parse(lines);

		chart.Points[0].X.Should().Be(1.0);
		chart.Points[0].Y.Should().Be(0.0);
	}

	[Test]
	public void Handles_empty_chart()
	{
		var lines = new[]
		{
			"quadrantChart",
		};

		var chart = QuadrantParser.Parse(lines);

		chart.Points.Should().HaveCount(0);
		chart.Title.Should().BeNull();
	}
}
