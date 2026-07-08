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
	public void Parses_title_on_header_line()
	{
		var lines = new[]
		{
			"pie title Pets adopted by volunteers",
			"\"Dogs\" : 386",
			"\"Cats\" : 85",
		};

		var chart = PieParser.Parse(lines);

		chart.Title.Should().Be("Pets adopted by volunteers");
		chart.ShowData.Should().BeFalse();
		chart.Slices.Should().HaveCount(2);
		chart.Slices[0].Label.Should().Be("Dogs");
		chart.Slices[0].Value.Should().Be(386);
	}

	[Test]
	public void Parses_showData_and_title_on_header_line()
	{
		var lines = new[]
		{
			"pie showData title Key elements",
			"\"Calcium\" : 42.96",
			"\"Potassium\" : 50.05",
		};

		var chart = PieParser.Parse(lines);

		chart.ShowData.Should().BeTrue();
		chart.Title.Should().Be("Key elements");
		chart.Slices.Should().HaveCount(2);
	}

	[Test]
	public void Body_title_overrides_header_title()
	{
		var lines = new[]
		{
			"pie title Header Title",
			"title Body Title",
			"\"A\" : 10",
		};

		var chart = PieParser.Parse(lines);

		chart.Title.Should().Be("Body Title");
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
