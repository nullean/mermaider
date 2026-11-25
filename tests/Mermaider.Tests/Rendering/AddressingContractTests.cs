using System.Globalization;
using System.Xml.Linq;
using AwesomeAssertions;
using Mermaider.Examples;

namespace Mermaider.Tests.Rendering;

/// <summary>
/// The addressing contract (DESIGN.md §10): every element a host may want to point at carries a stable hook, and every
/// connector names the elements it joins. Checked against every example of each covered diagram type.
/// </summary>
public class AddressingContractTests
{
	private static IEnumerable<XElement> Render(DiagramCategory category) =>
		DiagramExamples.All.Where(e => e.Category == category).SelectMany(e => XDocument.Parse(MermaidRenderer.RenderSvg(e.Source)).Descendants());

	private static IEnumerable<XDocument> Documents(DiagramCategory category) =>
		DiagramExamples.All.Where(e => e.Category == category).Select(e => XDocument.Parse(MermaidRenderer.RenderSvg(e.Source)));

	private static bool HasClass(XElement e, string cls) => ((string?)e.Attribute("class") ?? "").Split(' ').Contains(cls);

	private static List<int> Indices(IEnumerable<XElement> elements) =>
		elements.Select(e => int.Parse((string)e.Attribute("data-index")!, CultureInfo.InvariantCulture)).ToList();

	[Test]
	public void Gantt_tasks_carry_their_order_and_the_dates_they_span()
	{
		foreach (var doc in Documents(DiagramCategory.Gantt))
		{
			var tasks = doc.Descendants().Where(e => HasClass(e, "gantt-task")).ToList();
			tasks.Should().NotBeEmpty();
			Indices(tasks).Should().Equal(Enumerable.Range(1, tasks.Count));
			foreach (var task in tasks)
			{
				var start = DateTime.Parse((string)task.Attribute("data-start")!, CultureInfo.InvariantCulture);
				var end = DateTime.Parse((string)task.Attribute("data-end")!, CultureInfo.InvariantCulture);
				end.Should().BeOnOrAfter(start);
			}
		}
	}

	[Test]
	public void Timeline_periods_are_numbered_and_everything_of_a_period_names_it()
	{
		foreach (var doc in Documents(DiagramCategory.Timeline))
		{
			var periods = doc.Descendants().Where(e => HasClass(e, "timeline-period")).ToList();
			Indices(periods).Should().Equal(Enumerable.Range(1, periods.Count));
			var known = periods.Select(p => (string)p.Attribute("data-index")!).ToHashSet();
			var parts = doc.Descendants().Where(e => HasClass(e, "timeline-event") || HasClass(e, "timeline-moment") || HasClass(e, "timeline-drop")).ToList();
			parts.Should().NotBeEmpty();
			parts.Should().OnlyContain(e => known.Contains((string?)e.Attribute("data-period") ?? ""));
			doc.Descendants().Count(e => HasClass(e, "timeline-moment")).Should().Be(periods.Count);
		}
	}

	[Test]
	public void Git_commits_are_numbered_with_their_branch_and_links_join_them()
	{
		foreach (var doc in Documents(DiagramCategory.GitGraph))
		{
			var commits = doc.Descendants().Where(e => HasClass(e, "commit")).ToList();
			Indices(commits).Should().Equal(Enumerable.Range(1, commits.Count));
			commits.Should().OnlyContain(c => c.Attribute("data-branch") != null);
			var known = commits.Select(c => (string)c.Attribute("data-index")!).ToHashSet();
			var links = doc.Descendants().Where(e => HasClass(e, "gitgraph-link")).ToList();
			links.Should().OnlyContain(l => known.Contains((string)l.Attribute("data-from")!) && known.Contains((string)l.Attribute("data-to")!));
		}
	}

	[Test]
	public void Kanban_cards_name_their_column()
	{
		foreach (var doc in Documents(DiagramCategory.Kanban))
		{
			var columns = doc.Descendants().Where(e => HasClass(e, "kanban-column")).Select(c => (string)c.Attribute("data-id")!).ToHashSet();
			var cards = doc.Descendants().Where(e => HasClass(e, "kanban-card")).ToList();
			cards.Should().NotBeEmpty();
			cards.Should().OnlyContain(c => columns.Contains((string)c.Attribute("data-column")!));
		}
	}

	[Test]
	[Arguments(DiagramCategory.Mindmap, "mindmap-node")]
	[Arguments(DiagramCategory.TreeView, "treeview-node")]
	public void Tree_nodes_know_their_parent_and_links_join_parent_to_child(DiagramCategory category, string nodeClass)
	{
		foreach (var doc in Documents(category))
		{
			var nodes = doc.Descendants().Where(e => HasClass(e, nodeClass)).ToList();
			Indices(nodes).Should().Equal(Enumerable.Range(1, nodes.Count));
			var parentOf = nodes.Where(n => n.Attribute("data-parent") != null).ToDictionary(
				n => (string)n.Attribute("data-index")!,
				n => (string)n.Attribute("data-parent")!
			);
			parentOf.Values.Should().OnlyContain(p => nodes.Any(n => (string)n.Attribute("data-index")! == p));
			// exactly the roots lack a parent
			nodes.Count(n => n.Attribute("data-parent") == null).Should().BeGreaterThan(0);

			var links = doc.Descendants().Where(e => e.Attribute("data-from") != null && e.Attribute("data-to") != null).ToList();
			links.Should().HaveCount(parentOf.Count);
			links.Should().OnlyContain(l => parentOf[(string)l.Attribute("data-to")!] == (string)l.Attribute("data-from")!);
		}
	}

	[Test]
	public void Xychart_bars_carry_their_category_and_value()
	{
		foreach (var doc in Documents(DiagramCategory.XyChart))
		{
			var bars = doc.Descendants().Where(e => HasClass(e, "xy-bar")).ToList();
			bars.Should().OnlyContain(b => b.Parent != null && HasClass(b.Parent, "xy-bars"));
			foreach (var series in doc.Descendants().Where(e => HasClass(e, "xy-bars")))
				Indices(series.Elements().Where(e => HasClass(e, "xy-bar")))
					.Should()
					.Equal(Enumerable.Range(1, series.Elements().Count(e => HasClass(e, "xy-bar"))));
			bars
				.Select(
					b => double.TryParse((string?)b.Attribute("data-value"), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
						? value
						: double.NaN
				)
				.Should()
				.NotContain(double.NaN);
		}
	}

	[Test]
	public void Pie_slices_carry_value_angles_and_their_circle()
	{
		foreach (var doc in Documents(DiagramCategory.Pie))
		{
			var slices = doc.Descendants().Where(e => HasClass(e, "pie-slice")).ToList();
			Indices(slices).Should().Equal(Enumerable.Range(1, slices.Count));

			double D(XElement e, string name) => double.Parse((string)e.Attribute(name)!, CultureInfo.InvariantCulture);

			for (var i = 1; i < slices.Count; i++)
				D(slices[i], "data-start").Should().BeApproximately(D(slices[i - 1], "data-end"), 0.01, "slices follow each other round the circle");
			if (slices.Count > 0)
				(D(slices[^1], "data-end") - D(slices[0], "data-start")).Should().BeApproximately(360, 0.05);
			slices
				.Select(s => (string?)s.Attribute("data-cx") + "," + (string?)s.Attribute("data-cy") + "," + (string?)s.Attribute("data-r"))
				.Distinct()
				.Should()
				.HaveCount(Math.Min(1, slices.Count));
		}
	}

	[Test]
	public void Radar_curves_are_numbered_and_share_a_centre()
	{
		foreach (var doc in Documents(DiagramCategory.Radar))
		{
			var curves = doc.Descendants().Where(e => HasClass(e, "radar-curve")).ToList();
			Indices(curves).Should().Equal(Enumerable.Range(1, curves.Count));
			curves
				.Select(c => (string?)c.Attribute("data-cx") + "," + (string?)c.Attribute("data-cy"))
				.Distinct()
				.Should()
				.HaveCount(Math.Min(1, curves.Count));
		}
	}

	[Test]
	public void Sankey_links_join_nodes_that_exist_and_carry_their_value()
	{
		foreach (var doc in Documents(DiagramCategory.Sankey))
		{
			var nodes = doc.Descendants().Where(e => HasClass(e, "sankey-node")).Select(n => (string)n.Attribute("data-id")!).ToHashSet();
			var links = doc.Descendants().Where(e => HasClass(e, "sankey-link")).ToList();
			links.Should().NotBeEmpty();
			links.Should().OnlyContain(l => nodes.Contains((string)l.Attribute("data-from")!) && nodes.Contains((string)l.Attribute("data-to")!));
			links.Should().OnlyContain(l => double.Parse((string)l.Attribute("data-value")!, CultureInfo.InvariantCulture) > 0);
		}
	}

	[Test]
	[Arguments(DiagramCategory.Quadrant, "quadrant-point")]
	[Arguments(DiagramCategory.Venn, "venn-set")]
	[Arguments(DiagramCategory.Packet, "packet-field")]
	[Arguments(DiagramCategory.Journey, "journey-task")]
	public void Marks_are_numbered_in_order(DiagramCategory category, string markClass)
	{
		foreach (var doc in Documents(category))
		{
			var marks = doc.Descendants().Where(e => HasClass(e, markClass)).ToList();
			marks.Should().NotBeEmpty();
			Indices(marks).Should().Equal(Enumerable.Range(1, marks.Count));
		}
	}

	[Test]
	public void Quadrant_points_carry_their_label_and_position()
	{
		foreach (var doc in Documents(DiagramCategory.Quadrant))
			foreach (var point in doc.Descendants().Where(e => HasClass(e, "quadrant-point")))
			{
				point.Attribute("data-label").Should().NotBeNull();
				var x = double.Parse((string)point.Attribute("data-x")!, CultureInfo.InvariantCulture);
				var y = double.Parse((string)point.Attribute("data-y")!, CultureInfo.InvariantCulture);
				x.Should().BeInRange(0, 1);
				y.Should().BeInRange(0, 1);
			}
	}

	[Test]
	public void Treemap_sections_and_tiles_are_numbered_and_name_their_section()
	{
		foreach (var doc in Documents(DiagramCategory.Treemap))
		{
			var all = doc.Descendants().Where(e => HasClass(e, "treemap-section") || HasClass(e, "treemap-tile")).ToList();
			Indices(all).Order().Should().Equal(Enumerable.Range(1, all.Count));
			var sections = doc.Descendants().Where(e => HasClass(e, "treemap-section")).Select(e => (string)e.Attribute("data-index")!).ToHashSet();
			all.Where(e => e.Attribute("data-parent") != null).Should().OnlyContain(e => sections.Contains((string)e.Attribute("data-parent")!));
			doc.Descendants().Where(e => HasClass(e, "treemap-tile")).Should().OnlyContain(t => t.Attribute("data-parent") != null || !sections.Any());
		}
	}

	[Test]
	public void Venn_overlap_labels_name_sets_that_exist()
	{
		var labelled = 0;
		foreach (var doc in Documents(DiagramCategory.Venn))
		{
			var sets = doc.Descendants().Where(e => HasClass(e, "venn-set")).Select(e => (string)e.Attribute("data-set")!).ToHashSet();
			foreach (var union in doc.Descendants().Where(e => HasClass(e, "venn-union")))
			{
				labelled++;
				var members = ((string)union.Attribute("data-sets")!).Split(',');
				members.Should().HaveCountGreaterThanOrEqualTo(2).And.OnlyContain(m => sets.Contains(m));
				union.Descendants().Should().Contain(e => e.Name.LocalName == "text", "the label is inside its union");
			}
		}
		labelled.Should().BePositive("some venn example labels an overlap");
	}

	[Test]
	public void Requirement_relations_join_nodes_that_exist()
	{
		foreach (var doc in Documents(DiagramCategory.Requirement))
		{
			var ids = doc.Descendants().Select(e => (string?)e.Attribute("data-id")).Where(id => id != null).ToHashSet();
			var relations = doc.Descendants().Where(e => HasClass(e, "req-relation")).ToList();
			relations.Should().OnlyContain(r => ids.Contains((string)r.Attribute("data-from")!) && ids.Contains((string)r.Attribute("data-to")!));
		}
	}

	[Test]
	[Arguments(DiagramCategory.Flowchart)]
	[Arguments(DiagramCategory.State)]
	[Arguments(DiagramCategory.Block)]
	public void Connectors_name_elements_that_exist(DiagramCategory category)
	{
		foreach (var doc in Documents(category))
		{
			var ids = doc.Descendants().Select(e => (string?)e.Attribute("data-id")).Where(id => id != null).ToHashSet();
			var connectors = doc.Descendants().Where(e => e.Name.LocalName == "path" && e.Attribute("data-from") != null).ToList();
			connectors.Should().OnlyContain(c => ids.Contains((string)c.Attribute("data-from")!) && ids.Contains((string)c.Attribute("data-to")!));
		}
	}
}
