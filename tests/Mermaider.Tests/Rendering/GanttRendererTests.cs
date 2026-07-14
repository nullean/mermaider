using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

public class GanttRendererTests
{
	private const string FullGantt = """
		gantt
		title Shipping this file
		dateFormat  YYYY-MM-DD
		section Render
		Spike the renderer :done, a1, 2026-07-07, 1d
		Print this page    :active, a2, after a1, 1d
		section Polish
		Update tests       :crit, after a2, 12h
		Update docs        : 6h
		""";

	[Test]
	public void Renders_valid_svg()
	{
		var svg = MermaidRenderer.RenderSvg(FullGantt);

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Contains_title()
	{
		var svg = MermaidRenderer.RenderSvg(FullGantt);

		svg.Should().Contain("Shipping this file");
	}

	[Test]
	public void Contains_task_names()
	{
		var svg = MermaidRenderer.RenderSvg(FullGantt);

		svg.Should().Contain("Spike the renderer");
		svg.Should().Contain("Print this page");
		svg.Should().Contain("Update tests");
		svg.Should().Contain("Update docs");
	}

	[Test]
	public void Contains_section_labels()
	{
		var svg = MermaidRenderer.RenderSvg(FullGantt);

		svg.Should().Contain("Render");
		svg.Should().Contain("Polish");
	}

	[Test]
	public void Contains_task_bars()
	{
		var svg = MermaidRenderer.RenderSvg(FullGantt);

		svg.Should().Contain("<rect");
	}

	[Test]
	public void Renders_milestone_as_polygon()
	{
		var svg = MermaidRenderer.RenderSvg("""
			gantt
			dateFormat YYYY-MM-DD
			Ship :milestone, m1, 2026-07-09, 0d
			""");

		svg.Should().Contain("<polygon");
		svg.Should().Contain("Ship");
	}

	[Test]
	public void Renders_empty_gantt()
	{
		var svg = MermaidRenderer.RenderSvg("""
			gantt
			title Empty
			""");

		svg.Should().StartWith("<svg");
		svg.Should().Contain("Empty");
	}

	[Test]
	public void Detects_and_renders_via_public_api()
	{
		var svg = MermaidRenderer.RenderSvg("""
			gantt
			dateFormat YYYY-MM-DD
			A : a1, 2026-01-01, 3d
			B : after a1, 2d
			""");

		svg.Should().Contain("A");
		svg.Should().Contain("B");
	}
}
