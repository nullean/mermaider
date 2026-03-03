using AwesomeAssertions;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

public class TreemapParserTests
{
	[Test]
	public void Parses_flat_leaves()
	{
		var lines = new[]
		{
			"treemap-beta",
			"  \"A\": 30",
			"  \"B\": 20",
			"  \"C\": 50",
		};

		var diagram = TreemapParser.Parse(lines);

		diagram.Roots.Should().HaveCount(3);
		diagram.Roots[0].Label.Should().Be("A");
		diagram.Roots[0].Value.Should().Be(30);
	}

	[Test]
	public void Parses_nested_hierarchy()
	{
		var lines = new[]
		{
			"treemap-beta",
			"  \"Section A\"",
			"    \"Item 1\": 30",
			"    \"Item 2\": 20",
			"  \"Section B\"",
			"    \"Item 3\": 50",
		};

		var diagram = TreemapParser.Parse(lines);

		diagram.Roots.Should().HaveCount(2);
		diagram.Roots[0].Label.Should().Be("Section A");
		diagram.Roots[0].Children.Should().HaveCount(2);
		diagram.Roots[0].Children[0].Label.Should().Be("Item 1");
		diagram.Roots[0].Children[0].Value.Should().Be(30);
		diagram.Roots[1].Label.Should().Be("Section B");
		diagram.Roots[1].Children.Should().HaveCount(1);
	}

	[Test]
	public void Computes_parent_value_from_children()
	{
		var lines = new[]
		{
			"treemap-beta",
			"  \"Parent\"",
			"    \"A\": 10",
			"    \"B\": 20",
		};

		var diagram = TreemapParser.Parse(lines);

		diagram.Roots[0].ComputedValue.Should().Be(30);
	}

	[Test]
	public void Handles_empty_treemap()
	{
		var lines = new[]
		{
			"treemap-beta",
		};

		var diagram = TreemapParser.Parse(lines);

		diagram.Roots.Should().HaveCount(0);
	}
}
