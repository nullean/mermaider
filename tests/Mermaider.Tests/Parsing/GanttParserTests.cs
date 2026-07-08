using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

public class GanttParserTests
{
	[Test]
	public void Parses_title_and_dateFormat()
	{
		var lines = new[]
		{
			"gantt",
			"title Shipping",
			"dateFormat YYYY-MM-DD",
		};

		var diagram = GanttParser.Parse(lines);

		diagram.Title.Should().Be("Shipping");
		diagram.DateFormat.Should().Be("YYYY-MM-DD");
	}

	[Test]
	public void Parses_title_on_header_line()
	{
		var lines = new[]
		{
			"gantt title Compact Title",
			"dateFormat YYYY-MM-DD",
			"Task : a1, 2026-07-07, 1d",
		};

		var diagram = GanttParser.Parse(lines);

		diagram.Title.Should().Be("Compact Title");
	}

	[Test]
	public void Parses_sections_and_tasks_with_dates()
	{
		var lines = new[]
		{
			"gantt",
			"dateFormat YYYY-MM-DD",
			"section Render",
			"Spike :done, a1, 2026-07-07, 2026-07-08",
			"Print :active, a2, 2026-07-08, 2026-07-09",
		};

		var diagram = GanttParser.Parse(lines);

		diagram.Sections.Should().HaveCount(1);
		diagram.Sections[0].Name.Should().Be("Render");
		diagram.Sections[0].Tasks.Should().HaveCount(2);

		var spike = diagram.Sections[0].Tasks[0];
		spike.Name.Should().Be("Spike");
		spike.Id.Should().Be("a1");
		spike.Tags.Should().HaveFlag(GanttTaskTags.Done);
		spike.Start.Date.Should().Be(new DateTime(2026, 7, 7));
		spike.End.Date.Should().Be(new DateTime(2026, 7, 8));

		var print = diagram.Sections[0].Tasks[1];
		print.Tags.Should().HaveFlag(GanttTaskTags.Active);
	}

	[Test]
	public void Resolves_after_and_duration()
	{
		var lines = new[]
		{
			"gantt",
			"dateFormat YYYY-MM-DD",
			"First :done, a1, 2026-07-07, 1d",
			"Second :active, a2, after a1, 2d",
		};

		var diagram = GanttParser.Parse(lines);
		var tasks = diagram.Sections[0].Tasks;

		tasks[0].Start.Date.Should().Be(new DateTime(2026, 7, 7));
		tasks[0].End.Date.Should().Be(new DateTime(2026, 7, 8));
		tasks[1].Start.Date.Should().Be(new DateTime(2026, 7, 8));
		tasks[1].End.Date.Should().Be(new DateTime(2026, 7, 10));
	}

	[Test]
	public void Resolves_duration_only_after_previous()
	{
		var lines = new[]
		{
			"gantt",
			"dateFormat YYYY-MM-DD",
			"First : a1, 2026-07-07, 1d",
			"Second : 6h",
		};

		var diagram = GanttParser.Parse(lines);
		var tasks = diagram.Sections[0].Tasks;

		tasks[1].Start.Should().Be(tasks[0].End);
		tasks[1].End.Should().Be(tasks[0].End.AddHours(6));
	}

	[Test]
	public void Parses_crit_and_milestone()
	{
		var lines = new[]
		{
			"gantt",
			"dateFormat YYYY-MM-DD",
			"Critical path :crit, c1, 2026-07-07, 2d",
			"Ship it :milestone, m1, 2026-07-09, 0d",
		};

		var diagram = GanttParser.Parse(lines);
		var tasks = diagram.Sections[0].Tasks;

		tasks[0].Tags.Should().HaveFlag(GanttTaskTags.Crit);
		tasks[1].Tags.Should().HaveFlag(GanttTaskTags.Milestone);
		tasks[1].Start.Should().Be(tasks[1].End);
	}

	[Test]
	public void Parses_winprint_showcase_gantt()
	{
		var lines = new[]
		{
			"gantt",
			"title Shipping this file",
			"dateFormat  YYYY-MM-DD",
			"section Render",
			"Spike the renderer :done, a1, 2026-07-07, 1d",
			"Print this page    :active, a2, after a1, 1d",
			"section Polish",
			"Update tests       :crit, after a2, 12h",
			"Update docs        : 6h",
		};

		var diagram = GanttParser.Parse(lines);

		diagram.Title.Should().Be("Shipping this file");
		diagram.Sections.Should().HaveCount(2);
		diagram.Sections[0].Name.Should().Be("Render");
		diagram.Sections[0].Tasks.Should().HaveCount(2);
		diagram.Sections[1].Name.Should().Be("Polish");
		diagram.Sections[1].Tasks.Should().HaveCount(2);

		var spike = diagram.Sections[0].Tasks[0];
		spike.Id.Should().Be("a1");
		spike.Tags.Should().HaveFlag(GanttTaskTags.Done);

		var print = diagram.Sections[0].Tasks[1];
		print.Start.Should().Be(spike.End);

		var tests = diagram.Sections[1].Tasks[0];
		tests.Tags.Should().HaveFlag(GanttTaskTags.Crit);
		tests.Start.Should().Be(print.End);

		var docs = diagram.Sections[1].Tasks[1];
		docs.Start.Should().Be(tests.End);
		docs.End.Should().Be(tests.End.AddHours(6));
	}

	[Test]
	public void Ignores_excludes_directive()
	{
		var lines = new[]
		{
			"gantt",
			"dateFormat YYYY-MM-DD",
			"excludes weekends",
			"Task : a1, 2026-01-01, 1d",
		};

		var diagram = GanttParser.Parse(lines);

		diagram.Sections[0].Tasks.Should().HaveCount(1);
	}

	[Test]
	public void Parses_hyphenated_task_id_as_id_not_date()
	{
		var lines = new[]
		{
			"gantt",
			"dateFormat YYYY-MM-DD",
			"Feature work :done, task-1, 2026-07-07, 2d",
			"Follow up : after task-1, 1d",
		};

		var diagram = GanttParser.Parse(lines);
		var tasks = diagram.Sections[0].Tasks;

		tasks[0].Id.Should().Be("task-1");
		tasks[0].Start.Date.Should().Be(new DateTime(2026, 7, 7));
		tasks[0].End.Date.Should().Be(new DateTime(2026, 7, 9));
		tasks[1].Start.Should().Be(tasks[0].End);
	}

	[Test]
	public void Does_not_use_wall_clock_when_dates_missing()
	{
		// Duration-only chain with no absolute dates must resolve from a fixed synthetic origin.
		var lines = new[]
		{
			"gantt",
			"dateFormat YYYY-MM-DD",
			"A : 1d",
			"B : 2d",
		};

		var diagram = GanttParser.Parse(lines);
		var tasks = diagram.Sections[0].Tasks;

		tasks[0].Start.Should().Be(new DateTime(2020, 1, 1));
		tasks[0].End.Should().Be(new DateTime(2020, 1, 2));
		tasks[1].Start.Should().Be(tasks[0].End);
		tasks[1].End.Should().Be(new DateTime(2020, 1, 4));
	}
}
