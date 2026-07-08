using AwesomeAssertions;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

public class SankeyParserTests
{
	[Test]
	public void Parses_basic_links()
	{
		var diagram = SankeyParser.Parse(
		[
			"sankey-beta",
			"A,B,10",
			"B,C,5",
		]);

		diagram.Links.Should().HaveCount(2);
		diagram.Links[0].Source.Should().Be("A");
		diagram.Links[0].Target.Should().Be("B");
		diagram.Links[0].Value.Should().Be(10);
		diagram.Links[1].Value.Should().Be(5);
	}

	[Test]
	public void Parses_quoted_fields_with_commas()
	{
		var diagram = SankeyParser.Parse(
		[
			"sankey",
			"Pumped heat,\"Heating and cooling, homes\",193.026",
		]);

		diagram.Links.Should().HaveCount(1);
		diagram.Links[0].Target.Should().Be("Heating and cooling, homes");
		diagram.Links[0].Value.Should().BeApproximately(193.026, 0.001);
	}

	[Test]
	public void Parses_escaped_quotes()
	{
		var fields = SankeyParser.ParseCsvFields("a,\"say \"\"hi\"\"\",1");
		fields.Should().HaveCount(3);
		fields[1].Should().Be("say \"hi\"");
	}

	[Test]
	public void Skips_empty_and_invalid_rows()
	{
		var diagram = SankeyParser.Parse(
		[
			"sankey-beta",
			"",
			"A,B,0",
			"A,B,-1",
			"onlytwo,fields",
			"A,B,notanumber",
			"A,B,3",
		]);

		diagram.Links.Should().HaveCount(1);
		diagram.Links[0].Value.Should().Be(3);
	}

	[Test]
	public void Accepts_sankey_without_beta()
	{
		var diagram = SankeyParser.Parse(
		[
			"sankey",
			"X,Y,1",
		]);

		diagram.Links.Should().HaveCount(1);
	}
}
