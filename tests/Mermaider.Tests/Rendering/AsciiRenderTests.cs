using AwesomeAssertions;
using Mermaider.Models;

namespace Mermaider.Tests.Rendering;

/// <summary>
/// The text renderer. Same parse, same Sugiyama layout, sizes measured in character cells rather than pixels,
/// so what comes back is a grid every character already lands on.
/// </summary>
public class AsciiRenderTests
{
	private const string Flow = """
		flowchart LR
		  A[(OrderEvents)] -->|"2/s"| B{{Orders}}
		  B --> C[Order]
		  C -->|"Shipped"| D((Notify))
		  A --> E[/Volumes/]
		""";

	private static string[] Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

	[Test]
	public void A_flowchart_draws_a_box_per_node_with_its_label_intact()
	{
		var text = MermaidRenderer.RenderAscii(Flow);

		foreach (var label in new[] { "OrderEvents", "Orders", "Order", "Notify", "Volumes" })
			text.Should().Contain(label, $"'{label}' is a node and a box is what a node is drawn as");

		// a label a line ran through would arrive with a box-drawing character inside it; the border it is
		// drawn against may well be a tee, which is an edge arriving rather than an edge crossing
		foreach (var line in Lines(text))
		{
			if (!line.Contains("OrderEvents", StringComparison.Ordinal))
				continue;
			line.Should().Contain("│ OrderEvents ", "nothing is drawn through the inside of a box");
		}

		text.Should().Contain("▶", "an edge ends in an arrowhead");
		text.Should().Contain("2/s", "an edge label is drawn in the gap the edge leaves through");
	}

	[Test]
	public void The_same_chart_top_down_puts_every_box_on_its_own_row()
	{
		var text = MermaidRenderer.RenderAscii(Flow.Replace("flowchart LR", "flowchart TD", StringComparison.Ordinal));

		text.Should().Contain("▼", "a top-down edge arrives from above");
		var rows = Lines(text);
		var events = rows.Select((line, i) => (line, i)).Single(r => r.line.Contains("│ OrderEvents │", StringComparison.Ordinal)).i;
		var orders = rows.Select((line, i) => (line, i)).Single(r => r.line.Contains("│ Orders │", StringComparison.Ordinal)).i;
		orders.Should().BeGreaterThan(events, "what a record flows into is drawn under what it flows from");
	}

	[Test]
	public void Plain_ascii_uses_nothing_a_terminal_could_refuse()
	{
		var text = MermaidRenderer.RenderAscii(Flow, new AsciiOptions { Ascii = true });

		text.Should().NotBeEmpty();
		foreach (var character in text)
			((int)character).Should().BeLessThan(128, $"'{character}' is not ASCII");
	}

	[Test]
	public void A_box_series_is_drawn_as_one_box_and_whisker_per_category()
	{
		var text = MermaidRenderer.RenderAscii("""
			xychart-beta
			    title "Effect latency"
			    x-axis [checkout, search]
			    box [0.01, 0.04, 0.08, 0.2, 1.4]
			    box [0.02, 0.03, 0.05, 0.09, 0.3]
			""");

		text.Should().Contain("Effect latency");
		var rows = Lines(text);
		rows.Should().Contain(r => r.StartsWith("checkout", StringComparison.Ordinal));
		rows.Should().Contain(r => r.StartsWith("search", StringComparison.Ordinal));

		var checkout = rows.Single(r => r.StartsWith("checkout", StringComparison.Ordinal));
		checkout.Should().Contain("┃", "the median is marked inside the box");
		checkout.Should().Contain("━", "the middle half is drawn solid");
		checkout.Should().Contain("├").And.Contain("┤", "the whiskers end at the extremes");

		// the wider distribution reaches further right, which is the whole point of putting them on one scale
		var search = rows.Single(r => r.StartsWith("search", StringComparison.Ordinal));
		checkout.TrimEnd().Length.Should().BeGreaterThan(search.TrimEnd().Length);
	}

	[Test]
	public void A_box_series_survives_an_unsorted_summary()
	{
		var jumbled = MermaidRenderer.RenderAscii("""
			xychart-beta
			    x-axis [one]
			    box [1.4, 0.04, 0.01, 0.2, 0.08]
			""");

		var sorted = MermaidRenderer.RenderAscii("""
			xychart-beta
			    x-axis [one]
			    box [0.01, 0.04, 0.08, 0.2, 1.4]
			""");

		jumbled.Should().Be(sorted, "five numbers are a summary, and which order they arrive in is not information");
	}

	[Test]
	public void Bars_are_plotted_against_an_axis_that_names_what_it_reaches()
	{
		var text = MermaidRenderer.RenderAscii("""
			xychart-beta
			    title "Records a minute"
			    x-axis [mon, tue, wed]
			    bar [120, 300, 90]
			""");

		text.Should().Contain("Records a minute");
		text.Should().Contain("300", "the axis is labelled with the highest value it reaches");
		text.Should().Contain("mon").And.Contain("tue").And.Contain("wed");
		text.Should().Contain("#", "a bar is drawn");
	}

	[Test]
	public void A_diagram_with_no_text_rendering_says_so_rather_than_drawing_nothing()
	{
		var act = () => MermaidRenderer.RenderAscii("""
			pie title Pets
			    "Dogs" : 386
			    "Cats" : 85
			""");

		act.Should().Throw<NotSupportedException>().WithMessage("*RenderSvg*");
	}

	[Test]
	public void A_parallelogram_is_a_shape_of_its_own_rather_than_a_rectangle_with_slashes_in_the_label()
	{
		var leaning = MermaidRenderer.Parse("flowchart LR\n  a[/Volumes/] --> b[end]");
		leaning.Nodes["a"].Label.Should().Be("Volumes", "the slashes are the shape, not the text");
		leaning.Nodes["a"].Shape.Should().Be(NodeShape.Parallelogram);

		var other = MermaidRenderer.Parse(@"flowchart LR
  a[\Volumes\] --> b[end]");
		other.Nodes["a"].Shape.Should().Be(NodeShape.ParallelogramAlt);

		// the two trapezoids are the ones that mix the slashes, and they still are what they were
		MermaidRenderer.Parse(@"flowchart LR
  a[/Volumes\] --> b[end]").Nodes["a"].Shape.Should().Be(NodeShape.Trapezoid);
		MermaidRenderer.Parse(@"flowchart LR
  a[\Volumes/] --> b[end]").Nodes["a"].Shape.Should().Be(NodeShape.TrapezoidAlt);

		MermaidRenderer.RenderSvg("flowchart LR\n  a[/Volumes/] --> b[end]").Should().Contain("<polygon");
	}

	[Test]
	public void A_box_series_renders_in_svg_as_a_box_with_whiskers()
	{
		var svg = MermaidRenderer.RenderSvg("""
			xychart-beta
			    x-axis [checkout]
			    box [1, 2, 3, 4, 10]
			""");

		svg.Should().Contain("<rect", "the middle half is a rectangle");
		svg.Should().Contain("<line", "the whiskers and the median are lines");
	}
}
