using AwesomeAssertions;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

public class PieParserTests
{
	[Test]
	public void Parses_basic_slices()
	{
		var lines = new[]
		{
			"pie",
			"\"Dogs\" : 386",
			"\"Cats\" : 85",
			"\"Rats\" : 15",
		};

		var chart = PieParser.Parse(lines);

		chart.Slices.Should().HaveCount(3);
		chart.Slices[0].Label.Should().Be("Dogs");
		chart.Slices[0].Value.Should().Be(386);
		chart.Slices[1].Label.Should().Be("Cats");
		chart.Slices[1].Value.Should().Be(85);
		chart.Slices[2].Label.Should().Be("Rats");
		chart.Slices[2].Value.Should().Be(15);
	}

	[Test]
	public void Parses_title()
	{
		var lines = new[]
		{
			"pie",
			"title Pet Adoption",
			"\"Dogs\" : 50",
		};

		var chart = PieParser.Parse(lines);

		chart.Title.Should().Be("Pet Adoption");
	}

	[Test]
	public void Parses_showData_flag()
	{
		var lines = new[]
		{
			"pie showData",
			"\"A\" : 10",
		};

		var chart = PieParser.Parse(lines);

		chart.ShowData.Should().BeTrue();
	}

	[Test]
	public void ShowData_defaults_to_false()
	{
		var lines = new[]
		{
			"pie",
			"\"A\" : 10",
		};

		var chart = PieParser.Parse(lines);

		chart.ShowData.Should().BeFalse();
	}

	[Test]
	public void Parses_decimal_values()
	{
		var lines = new[]
		{
			"pie",
			"\"A\" : 33.33",
			"\"B\" : 66.67",
		};

		var chart = PieParser.Parse(lines);

		chart.Slices[0].Value.Should().BeApproximately(33.33, 0.001);
		chart.Slices[1].Value.Should().BeApproximately(66.67, 0.001);
	}

	[Test]
	public void Ignores_zero_value_slices()
	{
		var lines = new[]
		{
			"pie",
			"\"A\" : 10",
			"\"B\" : 0",
		};

		var chart = PieParser.Parse(lines);

		chart.Slices.Should().HaveCount(1);
	}

	[Test]
	public void Handles_empty_pie()
	{
		var lines = new[]
		{
			"pie",
		};

		var chart = PieParser.Parse(lines);

		chart.Slices.Should().HaveCount(0);
		chart.Title.Should().BeNull();
	}
}
