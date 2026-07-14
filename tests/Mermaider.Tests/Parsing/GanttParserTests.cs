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
	public void Single_date_token_is_end_not_start()
	{
		// Mermaid: one metadata item = end date; start = previous task end.
		var lines = new[]
		{
			"gantt",
			"dateFormat YYYY-MM-DD",
			"A : a1, 2026-07-01, 5d",
			"B : 2026-07-10",
		};

		var diagram = GanttParser.Parse(lines);
		var tasks = diagram.Sections[0].Tasks;

		tasks[0].Start.Date.Should().Be(new DateTime(2026, 7, 1));
		tasks[0].End.Date.Should().Be(new DateTime(2026, 7, 6));
		tasks[1].Start.Date.Should().Be(new DateTime(2026, 7, 6));
		tasks[1].End.Date.Should().Be(new DateTime(2026, 7, 10));
	}

	[Test]
	public void Single_date_after_id_is_end()
	{
		// Dialect: id + single date → end date, start from previous.
		var lines = new[]
		{
			"gantt",
			"dateFormat YYYY-MM-DD",
			"A : a1, 2026-07-01, 2d",
			"B : b1, 2026-07-10",
		};

		var diagram = GanttParser.Parse(lines);
		var tasks = diagram.Sections[0].Tasks;

		tasks[1].Id.Should().Be("b1");
		tasks[1].Start.Should().Be(tasks[0].End);
		tasks[1].End.Date.Should().Be(new DateTime(2026, 7, 10));
	}

	[Test]
	public void Month_duration_is_calendar_month_not_minute()
	{
		// Mermaid: M = months (case-sensitive), m = minutes.
		var lines = new[]
		{
			"gantt",
			"dateFormat YYYY-MM-DD",
			"Month work : a1, 2026-01-15, 1M",
			"Minute work : a2, 2026-03-01, 1m",
		};

		var diagram = GanttParser.Parse(lines);
		var tasks = diagram.Sections[0].Tasks;

		tasks[0].Start.Date.Should().Be(new DateTime(2026, 1, 15));
		tasks[0].End.Date.Should().Be(new DateTime(2026, 2, 15));
		tasks[1].Start.Date.Should().Be(new DateTime(2026, 3, 1));
		tasks[1].End.Should().Be(new DateTime(2026, 3, 1, 0, 1, 0));
	}

	[Test]
	public void Parses_year_week_and_ms_durations()
	{
		var lines = new[]
		{
			"gantt",
			"dateFormat YYYY-MM-DD",
			"Year : y1, 2020-01-01, 1y",
			"Week : w1, 2021-01-01, 2w",
			"Ms : ms1, 2021-02-01, 500ms",
		};

		var diagram = GanttParser.Parse(lines);
		var tasks = diagram.Sections[0].Tasks;

		tasks[0].End.Date.Should().Be(new DateTime(2021, 1, 1));
		tasks[1].End.Date.Should().Be(new DateTime(2021, 1, 15));
		tasks[2].End.Should().Be(new DateTime(2021, 2, 1, 0, 0, 0).AddMilliseconds(500));
	}

	[Test]
	public void Huge_duration_throws_MermaidParseException()
	{
		// ~2.7e5 days exceeds the ~200-year cap (scientific notation is not a valid duration token).
		var lines = new[]
		{
			"gantt",
			"dateFormat YYYY-MM-DD",
			"Boom : a1, 2026-01-01, 999999d",
		};

		var act = () => GanttParser.Parse(lines);

		act.Should().Throw<MermaidParseException>();
	}

	[Test]
	public void Parses_DD_MM_YYYY_dateFormat()
	{
		var lines = new[]
		{
			"gantt",
			"dateFormat DD-MM-YYYY",
			"Task : a1, 07-07-2026, 2d",
		};

		var diagram = GanttParser.Parse(lines);
		var task = diagram.Sections[0].Tasks[0];

		task.Start.Date.Should().Be(new DateTime(2026, 7, 7));
		task.End.Date.Should().Be(new DateTime(2026, 7, 9));
	}

	[Test]
	public void Invalid_dateFormat_throws_MermaidParseException()
	{
		// Unbalanced custom format quote is rejected by .NET DateTime format parser.
		var lines = new[]
		{
			"gantt",
			"dateFormat \"YYYY",
			"Task : a1, 2026-01-01, 1d",
		};

		var act = () => GanttParser.Parse(lines);

		act.Should().Throw<MermaidParseException>();
	}

	[Test]
	public void Excludes_weekends_extends_duration_based_end()
	{
		// 2026-01-01 is Thursday. 3 calendar days → Sun 4th; weekends push end to Tue 6th.
		var lines = new[]
		{
			"gantt",
			"dateFormat YYYY-MM-DD",
			"excludes weekends",
			"Task : a1, 2026-01-01, 3d",
		};

		var diagram = GanttParser.Parse(lines);
		var task = diagram.Sections[0].Tasks[0];

		task.Start.Date.Should().Be(new DateTime(2026, 1, 1));
		task.End.Date.Should().Be(new DateTime(2026, 1, 6));
	}

	[Test]
	public void Excludes_weekends_does_not_extend_manual_end_date()
	{
		var lines = new[]
		{
			"gantt",
			"dateFormat YYYY-MM-DD",
			"excludes weekends",
			"Task : a1, 2026-01-01, 2026-01-04",
		};

		var diagram = GanttParser.Parse(lines);
		var task = diagram.Sections[0].Tasks[0];

		task.End.Date.Should().Be(new DateTime(2026, 1, 4));
	}

	[Test]
	public void Accepts_excludes_line_without_error()
	{
		var lines = new[]
		{
			"gantt",
			"dateFormat YYYY-MM-DD",
			"excludes weekends",
			"Task : a1, 2026-01-05, 1d",
		};

		var diagram = GanttParser.Parse(lines);

		diagram.Sections[0].Tasks.Should().HaveCount(1);
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

	[Test]
	public void Two_token_start_and_duration_without_id()
	{
		var lines = new[]
		{
			"gantt",
			"dateFormat YYYY-MM-DD",
			"Task : 2026-07-07, 3d",
		};

		var diagram = GanttParser.Parse(lines);
		var task = diagram.Sections[0].Tasks[0];

		task.Id.Should().BeNull();
		task.Start.Date.Should().Be(new DateTime(2026, 7, 7));
		task.End.Date.Should().Be(new DateTime(2026, 7, 10));
	}

	[Test]
	public void Throws_on_click_directive()
	{
		var lines = new[]
		{
			"gantt",
			"dateFormat YYYY-MM-DD",
			"Task : done, a1, 2026-07-07, 1d",
			"click a1 href \"https://example.com\"",
		};

		var act = () => GanttParser.Parse(lines);

		act.Should().Throw<MermaidParseException>()
			.WithMessage("*click*");
	}

	[Test]
	public void Throws_on_click_call_directive()
	{
		var lines = new[]
		{
			"gantt",
			"dateFormat YYYY-MM-DD",
			"Task : done, a1, 2026-07-07, 1d",
			"  Click a1 call myCallback()",
		};

		var act = () => GanttParser.Parse(lines);

		act.Should().Throw<MermaidParseException>()
			.WithMessage("*click*");
	}
}
