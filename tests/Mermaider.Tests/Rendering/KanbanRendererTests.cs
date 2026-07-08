using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

public class KanbanRendererTests
{
	private const string FullBoard = """
		kanban
		title Sprint Board
		  Todo
		    Task1
		    Task2
		  In Progress
		    Task3
		  Done
		    Task4
		""";

	[Test]
	public void Renders_valid_svg()
	{
		var svg = MermaidRenderer.RenderSvg(FullBoard);

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Contains_role_description()
	{
		var svg = MermaidRenderer.RenderSvg("""
			kanban
			accTitle: Board
			  Todo
			    Task1
			""");

		svg.Should().Contain("aria-roledescription=\"kanban board\"");
	}

	[Test]
	public void Contains_title()
	{
		var svg = MermaidRenderer.RenderSvg(FullBoard);

		svg.Should().Contain("Sprint Board");
	}

	[Test]
	public void Contains_column_headers()
	{
		var svg = MermaidRenderer.RenderSvg(FullBoard);

		svg.Should().Contain("Todo");
		svg.Should().Contain("In Progress");
		svg.Should().Contain("Done");
	}

	[Test]
	public void Contains_task_labels()
	{
		var svg = MermaidRenderer.RenderSvg(FullBoard);

		svg.Should().Contain("Task1");
		svg.Should().Contain("Task2");
		svg.Should().Contain("Task3");
		svg.Should().Contain("Task4");
	}

	[Test]
	public void Renders_metadata_text()
	{
		var svg = MermaidRenderer.RenderSvg("""
			kanban
			  Todo
			    id4[Create parsing tests]@{ ticket: MC-2038, assigned: 'K.Sveidqvist', priority: 'High' }
			""");

		svg.Should().Contain("Create parsing tests");
		svg.Should().Contain("MC-2038");
		svg.Should().Contain("K.Sveidqvist");
		svg.Should().Contain("High");
	}

	[Test]
	public void Renders_id_title_columns()
	{
		var svg = MermaidRenderer.RenderSvg("""
			kanban
			  todo[To Do]
			    t1[Write docs]
			  done[Done]
			    t2[Ship it]
			""");

		svg.Should().Contain("To Do");
		svg.Should().Contain("Write docs");
		svg.Should().Contain("Done");
		svg.Should().Contain("Ship it");
	}

	[Test]
	public void Renders_empty_board()
	{
		var svg = MermaidRenderer.RenderSvg("kanban");

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Uses_theme_text_fill()
	{
		var svg = MermaidRenderer.RenderSvg(FullBoard);

		svg.Should().Contain("fill=\"var(--_text)\"");
		svg.Should().Contain("fill=\"var(--_accent-text)\"");
	}
}
