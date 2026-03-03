using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

public class MindmapParserTests
{
	[Test]
	public void Parses_root_node()
	{
		var lines = new[]
		{
			"mindmap",
			"  root((Central Idea))",
		};

		var diagram = MindmapParser.Parse(lines);

		diagram.Root.Label.Should().Be("Central Idea");
		diagram.Root.Shape.Should().Be(MindmapShape.Circle);
	}

	[Test]
	public void Parses_children()
	{
		var lines = new[]
		{
			"mindmap",
			"  root",
			"    Topic A",
			"    Topic B",
		};

		var diagram = MindmapParser.Parse(lines);

		diagram.Root.Children.Should().HaveCount(2);
		diagram.Root.Children[0].Label.Should().Be("Topic A");
		diagram.Root.Children[1].Label.Should().Be("Topic B");
	}

	[Test]
	public void Parses_nested_children()
	{
		var lines = new[]
		{
			"mindmap",
			"  root",
			"    Topic A",
			"      Subtopic 1",
			"      Subtopic 2",
			"    Topic B",
		};

		var diagram = MindmapParser.Parse(lines);

		diagram.Root.Children.Should().HaveCount(2);
		diagram.Root.Children[0].Children.Should().HaveCount(2);
		diagram.Root.Children[0].Children[0].Label.Should().Be("Subtopic 1");
	}

	[Test]
	public void Parses_square_shape()
	{
		var lines = new[]
		{
			"mindmap",
			"  [Square Node]",
		};

		var diagram = MindmapParser.Parse(lines);

		diagram.Root.Label.Should().Be("Square Node");
		diagram.Root.Shape.Should().Be(MindmapShape.Square);
	}

	[Test]
	public void Parses_rounded_shape()
	{
		var lines = new[]
		{
			"mindmap",
			"  (Rounded Node)",
		};

		var diagram = MindmapParser.Parse(lines);

		diagram.Root.Label.Should().Be("Rounded Node");
		diagram.Root.Shape.Should().Be(MindmapShape.Rounded);
	}

	[Test]
	public void Parses_hexagon_shape()
	{
		var lines = new[]
		{
			"mindmap",
			"  {{Hexagon Node}}",
		};

		var diagram = MindmapParser.Parse(lines);

		diagram.Root.Label.Should().Be("Hexagon Node");
		diagram.Root.Shape.Should().Be(MindmapShape.Hexagon);
	}

	[Test]
	public void Parses_cloud_shape()
	{
		var lines = new[]
		{
			"mindmap",
			"  ))Cloud Node((",
		};

		var diagram = MindmapParser.Parse(lines);

		diagram.Root.Label.Should().Be("Cloud Node");
		diagram.Root.Shape.Should().Be(MindmapShape.Cloud);
	}

	[Test]
	public void Handles_empty_mindmap()
	{
		var lines = new[]
		{
			"mindmap",
		};

		var diagram = MindmapParser.Parse(lines);

		diagram.Root.Should().NotBeNull();
	}
}
