using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

public class RadarParserTests
{
	[Test]
	public void Parses_title()
	{
		var lines = new[]
		{
			"radar-beta",
			"title Skills",
		};

		var chart = RadarParser.Parse(lines);

		chart.Title.Should().Be("Skills");
	}

	[Test]
	public void Parses_axes()
	{
		var lines = new[]
		{
			"radar-beta",
			"axis A, B, C",
		};

		var chart = RadarParser.Parse(lines);

		chart.Axes.Should().HaveCount(3);
		chart.Axes[0].Id.Should().Be("A");
		chart.Axes[1].Id.Should().Be("B");
		chart.Axes[2].Id.Should().Be("C");
	}

	[Test]
	public void Parses_axes_with_labels()
	{
		var lines = new[]
		{
			"radar-beta",
			"axis design[\"Design\"], code[\"Code\"]",
		};

		var chart = RadarParser.Parse(lines);

		chart.Axes[0].Id.Should().Be("design");
		chart.Axes[0].Label.Should().Be("Design");
		chart.Axes[1].Id.Should().Be("code");
		chart.Axes[1].Label.Should().Be("Code");
	}

	[Test]
	public void Parses_curves()
	{
		var lines = new[]
		{
			"radar-beta",
			"axis A, B, C",
			"curve c1{1, 2, 3}",
		};

		var chart = RadarParser.Parse(lines);

		chart.Curves.Should().HaveCount(1);
		chart.Curves[0].Id.Should().Be("c1");
		chart.Curves[0].Values.Should().HaveCount(3);
		chart.Curves[0].Values[0].Should().Be(1);
		chart.Curves[0].Values[2].Should().Be(3);
	}

	[Test]
	public void Parses_curve_with_label()
	{
		var lines = new[]
		{
			"radar-beta",
			"axis A, B",
			"curve c1[\"Team Alpha\"]{4, 5}",
		};

		var chart = RadarParser.Parse(lines);

		chart.Curves[0].Label.Should().Be("Team Alpha");
	}

	[Test]
	public void Parses_max_and_min()
	{
		var lines = new[]
		{
			"radar-beta",
			"axis A, B",
			"min 0",
			"max 100",
		};

		var chart = RadarParser.Parse(lines);

		chart.Min.Should().Be(0);
		chart.Max.Should().Be(100);
	}

	[Test]
	public void Parses_graticule_polygon()
	{
		var lines = new[]
		{
			"radar-beta",
			"axis A, B",
			"graticule polygon",
		};

		var chart = RadarParser.Parse(lines);

		chart.Graticule.Should().Be(RadarGraticule.Polygon);
	}

	[Test]
	public void Default_graticule_is_circle()
	{
		var lines = new[]
		{
			"radar-beta",
			"axis A, B",
		};

		var chart = RadarParser.Parse(lines);

		chart.Graticule.Should().Be(RadarGraticule.Circle);
	}
}
