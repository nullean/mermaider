using AwesomeAssertions;
using Mermaider.Examples;
using Mermaider.Layout;
using Mermaider.Models;
using Mermaider.Parsing;

namespace Mermaider.Tests.Snapshots.LayoutSkeleton;

// Absolute geometric invariants for every flowchart example (compound layout + obstacle-aware router).
public class FlowchartGeometryInvariantTests
{
	private static PositionedGraph Layout(string slug)
	{
		var example = DiagramExamples.All.Single(e => e.Slug == slug);
		var (cleaned, _) = DiagramPreprocessor.Process(example.Source);
		var graph = FlowchartParser.Parse(MermaidRenderer.PreprocessLines(cleaned));
		return LightweightLayoutEngine.Layout(graph);
	}

	public static IEnumerable<string> Slugs() =>
		DiagramExamples.All
			.Where(d => d.Category == DiagramCategory.Flowchart && !d.Source.Contains("stateDiagram", StringComparison.Ordinal))
			.Select(d => d.Slug);

	private static IEnumerable<PositionedGroup> Flatten(IEnumerable<PositionedGroup> gs) =>
		gs.SelectMany(g => new[] { g }.Concat(Flatten(g.Children)));

	private static bool Overlap(double ax, double ay, double aw, double ah, double bx, double by, double bw, double bh) =>
		ax < bx + bw - 1 && ax + aw > bx + 1 && ay < by + bh - 1 && ay + ah > by + 1;

	private static bool Contains(PositionedGroup a, PositionedGroup b) =>
		a.X <= b.X + 1 && a.Y <= b.Y + 1 && a.X + a.Width >= b.X + b.Width - 1 && a.Y + a.Height >= b.Y + b.Height - 1;

	[Test]
	[MethodDataSource(nameof(Slugs))]
	public void Subgraphs_never_partially_overlap(string slug)
	{
		var all = Flatten(Layout(slug).Groups).ToList();
		for (var i = 0; i < all.Count; i++)
		{
			for (var j = i + 1; j < all.Count; j++)
			{
				var a = all[i];
				var b = all[j];
				if (Overlap(a.X, a.Y, a.Width, a.Height, b.X, b.Y, b.Width, b.Height))
					(Contains(a, b) || Contains(b, a)).Should().BeTrue($"'{slug}': subgraphs {a.Id} and {b.Id} overlap");
			}
		}
	}

	[Test]
	[MethodDataSource(nameof(Slugs))]
	public void Edges_never_run_through_other_nodes(string slug)
	{
		var layout = Layout(slug);
		foreach (var e in layout.Edges)
		{
			foreach (var n in layout.Nodes.Where(n => n.Id != e.Source && n.Id != e.Target))
			{
				for (var i = 0; i < e.Points.Count - 1; i++)
				{
					var p = e.Points[i];
					var q = e.Points[i + 1];
					var x0 = Math.Min(p.X, q.X);
					var y0 = Math.Min(p.Y, q.Y);
					Overlap(x0, y0, Math.Abs(p.X - q.X), Math.Abs(p.Y - q.Y), n.X + 2, n.Y + 2, n.Width - 4, n.Height - 4)
						.Should().BeFalse($"'{slug}': edge {e.Source}->{e.Target} passes through node {n.Id}");
				}
			}
		}
	}

	[Test]
	[MethodDataSource(nameof(Slugs))]
	public void Edge_ends_touch_their_nodes(string slug)
	{
		var layout = Layout(slug);
		var nodes = layout.Nodes.ToDictionary(n => n.Id);
		foreach (var e in layout.Edges.Where(e => e.Style != EdgeStyle.Invisible && e.Points.Count >= 2))
		{
			if (!nodes.TryGetValue(e.Target, out var t))
				continue;
			var end = e.Points[^1];
			// edges addressed to a subgraph (`a --> subnet1`) end on the subgraph border instead of the inner node
			var onNode = end.X >= t.X - 2 && end.X <= t.X + t.Width + 2 && end.Y >= t.Y - 2 && end.Y <= t.Y + t.Height + 2;
			var onGroup = Flatten(layout.Groups).Any(g => end.X >= g.X - 2 && end.X <= g.X + g.Width + 2 && end.Y >= g.Y - 2 && end.Y <= g.Y + g.Height + 2);
			(onNode || onGroup)
				.Should().BeTrue($"'{slug}': edge {e.Source}->{e.Target} ends off its target node");
		}
	}
}
