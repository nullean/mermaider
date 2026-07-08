using AwesomeAssertions;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

public class JourneyParserTests
{
	[Test]
	public void Parses_title()
	{
		var lines = new[]
		{
			"journey",
			"title My working day",
		};

		var diagram = JourneyParser.Parse(lines);

		diagram.Title.Should().Be("My working day");
	}

	[Test]
	public void Parses_title_on_header_line()
	{
		var lines = new[]
		{
			"journey title Compact Day",
			"section A",
			"Task: 3: Me",
		};

		var diagram = JourneyParser.Parse(lines);

		diagram.Title.Should().Be("Compact Day");
	}

	[Test]
	public void Parses_sections_tasks_scores_and_actors()
	{
		var lines = new[]
		{
			"journey",
			"title My working day",
			"section Go to work",
			"Make tea: 5: Me",
			"Go upstairs: 3: Me",
			"Do work: 1: Me, Cat",
			"section Go home",
			"Go downstairs: 5: Me",
			"Sit down: 5: Me",
		};

		var diagram = JourneyParser.Parse(lines);

		diagram.Title.Should().Be("My working day");
		diagram.Sections.Should().HaveCount(2);

		diagram.Sections[0].Name.Should().Be("Go to work");
		diagram.Sections[0].Tasks.Should().HaveCount(3);
		diagram.Sections[0].Tasks[0].Name.Should().Be("Make tea");
		diagram.Sections[0].Tasks[0].Score.Should().Be(5);
		diagram.Sections[0].Tasks[0].Actors.Should().Equal("Me");
		diagram.Sections[0].Tasks[2].Name.Should().Be("Do work");
		diagram.Sections[0].Tasks[2].Score.Should().Be(1);
		diagram.Sections[0].Tasks[2].Actors.Should().Equal("Me", "Cat");

		diagram.Sections[1].Name.Should().Be("Go home");
		diagram.Sections[1].Tasks.Should().HaveCount(2);
	}

	[Test]
	public void Parses_task_without_actors()
	{
		var lines = new[]
		{
			"journey",
			"Only score: 4",
		};

		var diagram = JourneyParser.Parse(lines);

		diagram.Sections[0].Tasks.Should().HaveCount(1);
		diagram.Sections[0].Tasks[0].Name.Should().Be("Only score");
		diagram.Sections[0].Tasks[0].Score.Should().Be(4);
		diagram.Sections[0].Tasks[0].Actors.Should().BeEmpty();
	}

	[Test]
	public void Clamps_score_to_1_through_5()
	{
		var lines = new[]
		{
			"journey",
			"Too low: 0: Me",
			"Too high: 9: Me",
		};

		var diagram = JourneyParser.Parse(lines);

		diagram.Sections[0].Tasks[0].Score.Should().Be(1);
		diagram.Sections[0].Tasks[1].Score.Should().Be(5);
	}

	[Test]
	public void Parses_winprint_showcase_journey()
	{
		// From winprint testfiles/mermaid.md
		var lines = new[]
		{
			"journey",
			"title My working day",
			"section Go to work",
			"Make tea: 5: Me",
			"Go upstairs: 3: Me",
			"Do work: 1: Me, Cat",
			"section Go home",
			"Go downstairs: 5: Me",
			"Sit down: 5: Me",
		};

		var diagram = JourneyParser.Parse(lines);

		diagram.Sections.Should().HaveCount(2);
		diagram.Sections.SelectMany(s => s.Tasks).Should().HaveCount(5);
	}

	[Test]
	public void Handles_empty_journey()
	{
		var lines = new[]
		{
			"journey",
			"title Empty",
		};

		var diagram = JourneyParser.Parse(lines);

		diagram.Title.Should().Be("Empty");
		diagram.Sections.Should().HaveCount(1);
		diagram.Sections[0].Tasks.Should().BeEmpty();
	}
}
