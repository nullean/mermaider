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
