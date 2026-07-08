using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

public class XyChartParserTests
{
	[Test]
	public void Parses_title_and_series()
	{
		var chart = XyChartParser.Parse(
		[
			"xychart-beta",
			"title \"Sales Revenue\"",
			"x-axis [jan, feb, mar]",
			"y-axis \"Revenue\" 0 --> 100",
			"bar [10, 20, 30]",
			"line [15, 25, 28]",
		]);

		chart.Title.Should().Be("Sales Revenue");
		chart.XCategories.Should().Equal("jan", "feb", "mar");
		chart.YAxisTitle.Should().Be("Revenue");
		chart.YMin.Should().Be(0);
		chart.YMax.Should().Be(100);
		chart.Series.Should().HaveCount(2);
		chart.Series[0].Type.Should().Be(XySeriesType.Bar);
		chart.Series[1].Type.Should().Be(XySeriesType.Line);
		chart.Series[0].Values.Should().Equal(10, 20, 30);
	}

	[Test]
	public void Parses_named_series()
	{
		var chart = XyChartParser.Parse(
		[
			"xychart",
			"line \"avg\" [1, 2, 3]",
			"bar p50 [4, 5, 6]",
		]);

		chart.Series[0].Name.Should().Be("avg");
		chart.Series[1].Name.Should().Be("p50");
	}

	[Test]
	public void Parses_horizontal_header()
	{
		var chart = XyChartParser.Parse(
		[
			"xychart horizontal",
			"line [1, 2]",
		]);

		chart.Horizontal.Should().BeTrue();
	}

	[Test]
	public void Parses_compact_header_title()
	{
		var chart = XyChartParser.Parse(
		[
			"xychart-beta title Demo",
			"bar [1]",
		]);

		chart.Title.Should().Be("Demo");
	}

	[Test]
	public void Parses_quoted_categories()
	{
		var chart = XyChartParser.Parse(
		[
			"xychart",
			"x-axis [\"non fiction\", other]",
			"bar [1, 2]",
		]);

		chart.XCategories![0].Should().Be("non fiction");
		chart.XCategories[1].Should().Be("other");
	}

	[Test]
	public void Ignores_point_labels_on_numbers()
	{
		var chart = XyChartParser.Parse(
		[
			"xychart",
			"line [540 \"PaLM\", 65, 7]",
		]);

		chart.Series[0].Values.Should().Equal(540, 65, 7);
	}

	[Test]
	public void Keeps_slot_for_invalid_numbers()
	{
		var chart = XyChartParser.Parse(
		[
			"xychart",
			"bar [1, NaN, 3]",
		]);

		chart.Series[0].Values.Should().Equal(1, 0, 3);
	}

	[Test]
	public void Rejects_nan_axis_range()
	{
		var chart = XyChartParser.Parse(
		[
			"xychart",
			"y-axis NaN --> Infinity",
			"bar [1, 2]",
		]);

		chart.YMin.Should().BeNull();
		chart.YMax.Should().BeNull();
	}
}
