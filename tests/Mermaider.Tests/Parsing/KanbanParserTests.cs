using AwesomeAssertions;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

public class KanbanParserTests
{
	[Test]
	public void Parses_bare_columns_and_tasks()
	{
		var lines = new[]
		{
			"kanban",
			"  Todo",
			"    Task1",
			"    Task2",
			"  In Progress",
			"    Task3",
			"  Done",
			"    Task4",
		};

		var diagram = KanbanParser.Parse(lines);

		diagram.Columns.Should().HaveCount(3);
		diagram.Columns[0].Id.Should().Be("Todo");
		diagram.Columns[0].Title.Should().Be("Todo");
		diagram.Columns[0].Tasks.Should().HaveCount(2);
		diagram.Columns[0].Tasks[0].Title.Should().Be("Task1");
		diagram.Columns[0].Tasks[1].Title.Should().Be("Task2");
		diagram.Columns[1].Title.Should().Be("In Progress");
		diagram.Columns[1].Tasks.Should().HaveCount(1);
		diagram.Columns[1].Tasks[0].Title.Should().Be("Task3");
		diagram.Columns[2].Title.Should().Be("Done");
		diagram.Columns[2].Tasks[0].Title.Should().Be("Task4");
	}

	[Test]
	public void Parses_id_title_syntax()
	{
		var lines = new[]
		{
			"kanban",
			"  todo[To Do]",
			"    t1[Write docs]",
			"  done[Done]",
			"    t2[Ship it]",
		};

		var diagram = KanbanParser.Parse(lines);

		diagram.Columns[0].Id.Should().Be("todo");
		diagram.Columns[0].Title.Should().Be("To Do");
		diagram.Columns[0].Tasks[0].Id.Should().Be("t1");
		diagram.Columns[0].Tasks[0].Title.Should().Be("Write docs");
		diagram.Columns[1].Id.Should().Be("done");
		diagram.Columns[1].Tasks[0].Id.Should().Be("t2");
	}

	[Test]
	public void Parses_task_metadata()
	{
		var lines = new[]
		{
			"kanban",
			"  Todo",
			"    id4[Create parsing tests]@{ ticket: MC-2038, assigned: 'K.Sveidqvist', priority: 'High' }",
		};

		var diagram = KanbanParser.Parse(lines);

		var task = diagram.Columns[0].Tasks[0];
		task.Id.Should().Be("id4");
		task.Title.Should().Be("Create parsing tests");
		task.Ticket.Should().Be("MC-2038");
		task.Assigned.Should().Be("K.Sveidqvist");
		task.Priority.Should().Be("High");
	}

	[Test]
	public void Parses_title()
	{
		var lines = new[]
		{
			"kanban",
			"title Sprint Board",
			"  Todo",
			"    Task1",
		};

		var diagram = KanbanParser.Parse(lines);

		diagram.Title.Should().Be("Sprint Board");
		diagram.Columns.Should().HaveCount(1);
	}

	[Test]
	public void Handles_empty_kanban()
	{
		var lines = new[]
		{
			"kanban",
		};

		var diagram = KanbanParser.Parse(lines);

		diagram.Columns.Should().HaveCount(0);
		diagram.Title.Should().BeNull();
	}

	[Test]
	public void Bare_text_id_equals_label()
	{
		var lines = new[]
		{
			"kanban",
			"  Review",
			"    Design review",
		};

		var diagram = KanbanParser.Parse(lines);

		diagram.Columns[0].Id.Should().Be("Review");
		diagram.Columns[0].Title.Should().Be("Review");
		diagram.Columns[0].Tasks[0].Id.Should().Be("Design review");
		diagram.Columns[0].Tasks[0].Title.Should().Be("Design review");
	}

	[Test]
	public void Parses_unquoted_metadata_values()
	{
		var lines = new[]
		{
			"kanban",
			"  Todo",
			"    task[Work]@{ assigned: knsv, ticket: MC-1, priority: Low }",
		};

		var diagram = KanbanParser.Parse(lines);

		var task = diagram.Columns[0].Tasks[0];
		task.Assigned.Should().Be("knsv");
		task.Ticket.Should().Be("MC-1");
		task.Priority.Should().Be("Low");
	}

	[Test]
	public void Column_without_tasks()
	{
		var lines = new[]
		{
			"kanban",
			"  Empty",
			"  Full",
			"    Item",
		};

		var diagram = KanbanParser.Parse(lines);

		diagram.Columns.Should().HaveCount(2);
		diagram.Columns[0].Tasks.Should().HaveCount(0);
		diagram.Columns[1].Tasks.Should().HaveCount(1);
	}
}
