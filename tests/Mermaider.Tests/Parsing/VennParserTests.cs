using AwesomeAssertions;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

public class VennParserTests
{
	[Test]
	public void Parses_sets()
	{
		var lines = new[]
		{
			"venn-beta",
			"set A",
			"set B",
		};

		var diagram = VennParser.Parse(lines);

		diagram.Sets.Should().HaveCount(2);
		diagram.Sets[0].Id.Should().Be("A");
		diagram.Sets[1].Id.Should().Be("B");
	}

	[Test]
	public void Parses_set_with_label()
	{
		var lines = new[]
		{
			"venn-beta",
			"set A[\"Frontend\"]",
		};

		var diagram = VennParser.Parse(lines);

		diagram.Sets[0].Id.Should().Be("A");
		diagram.Sets[0].Label.Should().Be("Frontend");
	}

	[Test]
	public void Parses_set_with_size()
	{
		var lines = new[]
		{
			"venn-beta",
			"set A: 100",
		};

		var diagram = VennParser.Parse(lines);

		diagram.Sets[0].Size.Should().Be(100);
	}

	[Test]
	public void Parses_unions()
	{
		var lines = new[]
		{
			"venn-beta",
			"set A",
			"set B",
			"union A, B[\"Overlap\"]",
		};

		var diagram = VennParser.Parse(lines);

		diagram.Unions.Should().HaveCount(1);
		diagram.Unions[0].SetIds.Should().HaveCount(2);
		diagram.Unions[0].SetIds[0].Should().Be("A");
		diagram.Unions[0].SetIds[1].Should().Be("B");
		diagram.Unions[0].Label.Should().Be("Overlap");
	}

	[Test]
	public void Parses_quoted_set_id()
	{
		var lines = new[]
		{
			"venn-beta",
			"set \"Front End\"",
		};

		var diagram = VennParser.Parse(lines);

		diagram.Sets[0].Id.Should().Be("Front End");
	}

	[Test]
	public void Handles_empty_diagram()
	{
		var lines = new[]
		{
			"venn-beta",
		};

		var diagram = VennParser.Parse(lines);

		diagram.Sets.Should().HaveCount(0);
		diagram.Unions.Should().HaveCount(0);
	}
}
