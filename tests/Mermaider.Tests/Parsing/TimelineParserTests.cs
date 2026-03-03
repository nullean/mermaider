using AwesomeAssertions;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

public class TimelineParserTests
{
	[Test]
	public void Parses_title()
	{
		var lines = new[]
		{
			"timeline",
			"title History of Tech",
		};

		var diagram = TimelineParser.Parse(lines);

		diagram.Title.Should().Be("History of Tech");
	}

	[Test]
	public void Parses_periods_with_events()
	{
		var lines = new[]
		{
			"timeline",
			"2002 : LinkedIn",
			"2004 : Facebook : Google",
		};

		var diagram = TimelineParser.Parse(lines);

		diagram.Sections.Should().HaveCount(1);
		diagram.Sections[0].Name.Should().BeNull();
		diagram.Sections[0].Periods.Should().HaveCount(2);
		diagram.Sections[0].Periods[0].Label.Should().Be("2002");
		diagram.Sections[0].Periods[0].Events.Should().HaveCount(1);
		diagram.Sections[0].Periods[0].Events[0].Should().Be("LinkedIn");
		diagram.Sections[0].Periods[1].Events.Should().HaveCount(2);
		diagram.Sections[0].Periods[1].Events[0].Should().Be("Facebook");
		diagram.Sections[0].Periods[1].Events[1].Should().Be("Google");
	}

	[Test]
	public void Parses_sections()
	{
		var lines = new[]
		{
			"timeline",
			"section Early",
			"2002 : LinkedIn",
			"section Modern",
			"2010 : Instagram",
		};

		var diagram = TimelineParser.Parse(lines);

		diagram.Sections.Should().HaveCount(2);
		diagram.Sections[0].Name.Should().Be("Early");
		diagram.Sections[0].Periods.Should().HaveCount(1);
		diagram.Sections[1].Name.Should().Be("Modern");
		diagram.Sections[1].Periods.Should().HaveCount(1);
	}

	[Test]
	public void Parses_continuation_events()
	{
		var lines = new[]
		{
			"timeline",
			"2004 : Facebook",
			": Google",
			": Flickr",
		};

		var diagram = TimelineParser.Parse(lines);

		diagram.Sections[0].Periods[0].Events.Should().HaveCount(3);
		diagram.Sections[0].Periods[0].Events[0].Should().Be("Facebook");
		diagram.Sections[0].Periods[0].Events[1].Should().Be("Google");
		diagram.Sections[0].Periods[0].Events[2].Should().Be("Flickr");
	}

	[Test]
	public void Handles_empty_timeline()
	{
		var lines = new[]
		{
			"timeline",
		};

		var diagram = TimelineParser.Parse(lines);

		diagram.Sections.Should().HaveCount(0);
		diagram.Title.Should().BeNull();
	}

	[Test]
	public void Parses_mixed_sections_and_default()
	{
		var lines = new[]
		{
			"timeline",
			"title Test",
			"2000 : Event A",
			"section Phase 2",
			"2010 : Event B",
		};

		var diagram = TimelineParser.Parse(lines);

		diagram.Title.Should().Be("Test");
		diagram.Sections.Should().HaveCount(2);
		diagram.Sections[0].Name.Should().BeNull();
		diagram.Sections[1].Name.Should().Be("Phase 2");
	}
}
