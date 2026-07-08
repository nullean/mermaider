using AwesomeAssertions;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

public class BlockParserTests
{
	[Test]
	public void Parses_grid_nodes_and_columns()
	{
		var diagram = BlockParser.Parse(
		[
			"block-beta",
			"columns 3",
			"A[\"A\"] B[\"B\"] C[\"C\"]",
			"D[\"D\"] E[\"E\"] F[\"F\"]",
		]);

		diagram.Columns.Should().Be(3);
		diagram.Nodes.Should().HaveCount(6);
		diagram.Nodes[0].Id.Should().Be("A");
		diagram.Nodes[0].Label.Should().Be("A");
		diagram.Nodes[5].Id.Should().Be("F");
		diagram.Nodes[5].Label.Should().Be("F");
	}

	[Test]
	public void Parses_block_header_without_beta()
	{
		var diagram = BlockParser.Parse(["block", "A[\"One\"]"]);

		diagram.Nodes.Should().HaveCount(1);
		diagram.Nodes[0].Label.Should().Be("One");
	}

	[Test]
	public void Defaults_columns_to_one()
	{
		var diagram = BlockParser.Parse(
		[
			"block-beta",
			"A[\"First\"]",
			"B[\"Second\"]",
		]);

		diagram.Columns.Should().Be(1);
		diagram.Nodes.Should().HaveCount(2);
	}

	[Test]
	public void Parses_id_only_and_unquoted_labels()
	{
		var diagram = BlockParser.Parse(
		[
			"block-beta",
			"columns 2",
			"A B[Label]",
		]);

		diagram.Nodes.Should().HaveCount(2);
		diagram.Nodes[0].Id.Should().Be("A");
		diagram.Nodes[0].Label.Should().Be("A");
		diagram.Nodes[1].Id.Should().Be("B");
		diagram.Nodes[1].Label.Should().Be("Label");
	}

	[Test]
	public void Parses_rounded_paren_form()
	{
		var diagram = BlockParser.Parse(
		[
			"block-beta",
			"R(\"Rounded\")",
		]);

		diagram.Nodes.Should().HaveCount(1);
		diagram.Nodes[0].Rounded.Should().BeTrue();
		diagram.Nodes[0].Label.Should().Be("Rounded");
	}

	[Test]
	public void Parses_title_line_and_compact_header()
	{
		var withLine = BlockParser.Parse(
		[
			"block-beta",
			"title Grid Layout",
			"A[\"A\"]",
		]);
		withLine.Title.Should().Be("Grid Layout");

		var compact = BlockParser.Parse(
		[
			"block-beta title Compact",
			"A[\"A\"]",
		]);
		compact.Title.Should().Be("Compact");
	}

	[Test]
	public void Parses_simple_edges()
	{
		var diagram = BlockParser.Parse(
		[
			"block-beta",
			"columns 2",
			"A[\"A\"] B[\"B\"]",
			"A --> B",
		]);

		diagram.Edges.Should().HaveCount(1);
		diagram.Edges[0].From.Should().Be("A");
		diagram.Edges[0].To.Should().Be("B");
	}

	[Test]
	public void Skips_duplicate_ids()
	{
		var diagram = BlockParser.Parse(
		[
			"block-beta",
			"A[\"First\"] A[\"Second\"]",
		]);

		diagram.Nodes.Should().HaveCount(1);
		diagram.Nodes[0].Label.Should().Be("First");
	}

	[Test]
	public void Parses_space_spacers()
	{
		var diagram = BlockParser.Parse(
		[
			"block-beta",
			"columns 3",
			"A[\"A\"] space B[\"B\"]",
		]);

		diagram.Nodes.Should().HaveCount(3);
		diagram.Nodes[1].IsSpace.Should().BeTrue();
		diagram.Nodes[1].Label.Should().BeEmpty();
		diagram.Nodes[0].IsSpace.Should().BeFalse();
		diagram.Nodes[2].IsSpace.Should().BeFalse();
	}

	[Test]
	public void Literal_space_prefixed_ids_are_not_spacers()
	{
		var diagram = BlockParser.Parse(
		[
			"block-beta",
			"columns 2",
			"__space_0[\"Slot\"] space",
		]);

		diagram.Nodes.Should().HaveCount(2);
		diagram.Nodes[0].Id.Should().Be("__space_0");
		diagram.Nodes[0].Label.Should().Be("Slot");
		diagram.Nodes[0].IsSpace.Should().BeFalse();
		diagram.Nodes[1].IsSpace.Should().BeTrue();
	}

	[Test]
	public void Ignores_invalid_columns()
	{
		var diagram = BlockParser.Parse(
		[
			"block-beta",
			"columns 0",
			"A[\"A\"]",
		]);

		diagram.Columns.Should().Be(1);
	}

	[Test]
	public void Handles_empty_block()
	{
		var diagram = BlockParser.Parse(["block-beta"]);

		diagram.Nodes.Should().BeEmpty();
		diagram.Edges.Should().BeEmpty();
		diagram.Title.Should().BeNull();
	}
}
