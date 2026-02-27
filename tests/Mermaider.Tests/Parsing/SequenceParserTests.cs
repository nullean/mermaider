using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

public class SequenceParserTests
{
	[Test]
	public void Parses_participants_and_actors()
	{
		var lines = new[]
		{
			"sequenceDiagram",
			"participant A as Alice",
			"actor B as Bob",
		};

		var diagram = SequenceParser.Parse(lines);

		diagram.Actors.Should().HaveCount(2);
		diagram.Actors[0].Id.Should().Be("A");
		diagram.Actors[0].Label.Should().Be("Alice");
		diagram.Actors[0].Type.Should().Be(SequenceActorType.Participant);
		diagram.Actors[1].Id.Should().Be("B");
		diagram.Actors[1].Label.Should().Be("Bob");
		diagram.Actors[1].Type.Should().Be(SequenceActorType.Actor);
	}

	[Test]
	public void Parses_solid_filled_arrow_message()
	{
		var lines = new[]
		{
			"sequenceDiagram",
			"A->>B: Hello",
		};

		var diagram = SequenceParser.Parse(lines);

		diagram.Messages.Should().HaveCount(1);
		diagram.Messages[0].From.Should().Be("A");
		diagram.Messages[0].To.Should().Be("B");
		diagram.Messages[0].Label.Should().Be("Hello");
		diagram.Messages[0].LineStyle.Should().Be(SequenceLineStyle.Solid);
		diagram.Messages[0].ArrowHead.Should().Be(SequenceArrowHead.Filled);
	}

	[Test]
	public void Parses_dashed_filled_arrow_message()
	{
		var lines = new[]
		{
			"sequenceDiagram",
			"A-->>B: Reply",
		};

		var diagram = SequenceParser.Parse(lines);

		diagram.Messages[0].LineStyle.Should().Be(SequenceLineStyle.Dashed);
		diagram.Messages[0].ArrowHead.Should().Be(SequenceArrowHead.Filled);
	}

	[Test]
	public void Parses_open_arrow_message()
	{
		var lines = new[]
		{
			"sequenceDiagram",
			"A-)B: Async",
		};

		var diagram = SequenceParser.Parse(lines);

		diagram.Messages[0].LineStyle.Should().Be(SequenceLineStyle.Solid);
		diagram.Messages[0].ArrowHead.Should().Be(SequenceArrowHead.Open);
	}

	[Test]
	public void Parses_solid_arrow_message()
	{
		var lines = new[]
		{
			"sequenceDiagram",
			"A->B: Simple",
		};

		var diagram = SequenceParser.Parse(lines);

		diagram.Messages[0].LineStyle.Should().Be(SequenceLineStyle.Solid);
		diagram.Messages[0].ArrowHead.Should().Be(SequenceArrowHead.Open);
	}

	[Test]
	public void Parses_cross_arrow_as_filled()
	{
		var lines = new[]
		{
			"sequenceDiagram",
			"A-xB: Lost",
		};

		var diagram = SequenceParser.Parse(lines);

		diagram.Messages[0].ArrowHead.Should().Be(SequenceArrowHead.Filled);
	}

	[Test]
	public void Parses_activation_marks()
	{
		var lines = new[]
		{
			"sequenceDiagram",
			"A->>+B: Request",
			"B-->>-A: Response",
		};

		var diagram = SequenceParser.Parse(lines);

		diagram.Messages[0].Activate.Should().BeTrue();
		diagram.Messages[0].Deactivate.Should().BeFalse();
		diagram.Messages[1].Deactivate.Should().BeTrue();
		diagram.Messages[1].Activate.Should().BeFalse();
	}

	[Test]
	public void Auto_creates_actors_from_messages()
	{
		var lines = new[]
		{
			"sequenceDiagram",
			"Alice->>Bob: Hi",
		};

		var diagram = SequenceParser.Parse(lines);

		diagram.Actors.Should().HaveCount(2);
		diagram.Actors[0].Id.Should().Be("Alice");
		diagram.Actors[0].Label.Should().Be("Alice");
		diagram.Actors[1].Id.Should().Be("Bob");
	}

	[Test]
	public void Parses_loop_block()
	{
		var lines = new[]
		{
			"sequenceDiagram",
			"loop Every minute",
			"A->>B: Ping",
			"end",
		};

		var diagram = SequenceParser.Parse(lines);

		diagram.Blocks.Should().HaveCount(1);
		diagram.Blocks[0].Type.Should().Be(SequenceBlockType.Loop);
		diagram.Blocks[0].Label.Should().Be("Every minute");
		diagram.Blocks[0].StartIndex.Should().Be(0);
		diagram.Blocks[0].EndIndex.Should().Be(0);
	}

	[Test]
	public void Parses_alt_block_with_else_divider()
	{
		var lines = new[]
		{
			"sequenceDiagram",
			"alt Valid",
			"A->>B: OK",
			"else Invalid",
			"A->>B: Error",
			"end",
		};

		var diagram = SequenceParser.Parse(lines);

		diagram.Blocks.Should().HaveCount(1);
		diagram.Blocks[0].Type.Should().Be(SequenceBlockType.Alt);
		diagram.Blocks[0].Label.Should().Be("Valid");
		diagram.Blocks[0].Dividers.Should().HaveCount(1);
		diagram.Blocks[0].Dividers[0].Label.Should().Be("Invalid");
	}

	[Test]
	public void Parses_notes()
	{
		var lines = new[]
		{
			"sequenceDiagram",
			"participant A",
			"participant B",
			"A->>B: Hello",
			"Note right of B: Important",
		};

		var diagram = SequenceParser.Parse(lines);

		diagram.Notes.Should().HaveCount(1);
		diagram.Notes[0].Position.Should().Be(SequenceNotePosition.Right);
		diagram.Notes[0].ActorIds.Should().HaveCount(1);
		diagram.Notes[0].ActorIds[0].Should().Be("B");
		diagram.Notes[0].Text.Should().Be("Important");
	}

	[Test]
	public void Parses_note_over_multiple_actors()
	{
		var lines = new[]
		{
			"sequenceDiagram",
			"participant A",
			"participant B",
			"Note over A,B: Shared note",
		};

		var diagram = SequenceParser.Parse(lines);

		diagram.Notes[0].Position.Should().Be(SequenceNotePosition.Over);
		diagram.Notes[0].ActorIds.Should().HaveCount(2);
	}
}
