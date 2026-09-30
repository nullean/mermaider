using System.Text.RegularExpressions;
using AwesomeAssertions;
using Mermaider.Examples;

namespace Mermaider.Tests.Rendering;

/// <summary>
/// Geometric invariants every rendered ER diagram must satisfy:
///  1. Edge start/end points connect to their named entities (within tolerance).
///  2. Labels don't overlap entity bounding boxes.
///  3. Labels are positioned between their two entity centers (not floating off elsewhere).
/// </summary>
public partial class ErGeometryTests
{
	private const int TimeoutMs = 2000;
	private const double SnapTolerance = 30.0;  // px — edge endpoints must be within this of entity edge
	private const double LabelInEntityTolerance = 4.0; // px — label center must be outside entity box by at least this

	// ── public ER examples only (never private db-erd diagrams) ──────────────────
	public static IEnumerable<string> PublicErSlugs() =>
		DiagramExamples.All
			.Where(d => d.Category == DiagramCategory.Er)
			.Select(d => d.Slug);

	/// <summary>
	/// Regression: SpreadConvergentPorts was pulling source exit points outside the source
	/// entity box when spreading convergent top-entry ports on a narrow target entity.
	/// "exported" (Page→DocDoc) started at x=338 while Page's box spanned x=[349,469].
	/// </summary>
	[Test]
	public void Convergent_port_spread_does_not_disconnect_source_edges()
	{
		const string diagram = """
			erDiagram
			  A ||--|| C : "first"
			  B ||--|| C : "second"
			""";
		var svg = MermaidRenderer.RenderSvg(diagram);
		var entities = ParseEntityBoxes(svg);
		var edges = ParseEdges(svg);

		foreach (var edge in edges)
		{
			if (!entities.TryGetValue(edge.Entity1, out var box1)) continue;
			if (!entities.TryGetValue(edge.Entity2, out var box2)) continue;
			// Use a tight tolerance: start point must touch entity1 box (± 4px)
			box1.Expand(4).Contains(edge.Start)
				.Should().BeTrue(
					$"edge start ({edge.Start.X:F1},{edge.Start.Y:F1}) for {edge.Entity1}→{edge.Entity2} must touch entity '{edge.Entity1}' box {box1} after port spreading");
			box2.Expand(4).Contains(edge.End)
				.Should().BeTrue(
					$"edge end ({edge.End.X:F1},{edge.End.Y:F1}) for {edge.Entity1}→{edge.Entity2} must touch entity '{edge.Entity2}' box {box2} after port spreading");
		}
	}

	[Test]
	[MethodDataSource(nameof(PublicErSlugs))]
	public void Edge_endpoints_connect_to_entity_boxes(string slug)
	{
		var source = DiagramExamples.All.First(d => d.Slug == slug).Source;
		var svg = MermaidRenderer.RenderSvg(source);
		var entities = ParseEntityBoxes(svg);
		var edges = ParseEdges(svg);

		foreach (var edge in edges)
		{
			if (!entities.TryGetValue(edge.Entity1, out var box1))
				continue; // self-loop or missing — skip
			if (!entities.TryGetValue(edge.Entity2, out var box2))
				continue;

			var expandedBox1 = box1.Expand(SnapTolerance);
			var expandedBox2 = box2.Expand(SnapTolerance);

			expandedBox1.Contains(edge.Start)
				.Should().BeTrue(
					$"edge start ({edge.Start.X:F1},{edge.Start.Y:F1}) for {edge.Entity1}→{edge.Entity2} in '{slug}' should be near entity '{edge.Entity1}' box {box1}");

			expandedBox2.Contains(edge.End)
				.Should().BeTrue(
					$"edge end ({edge.End.X:F1},{edge.End.Y:F1}) for {edge.Entity1}→{edge.Entity2} in '{slug}' should be near entity '{edge.Entity2}' box {box2}");
		}
	}

	[Test]
	[MethodDataSource(nameof(PublicErSlugs))]
	public void Labels_do_not_overlap_entity_boxes(string slug)
	{
		var source = DiagramExamples.All.First(d => d.Slug == slug).Source;
		var svg = MermaidRenderer.RenderSvg(source);
		var entities = ParseEntityBoxes(svg);
		var labels = ParseLabelPositions(svg);

		foreach (var label in labels)
		{
			foreach (var (entityId, box) in entities)
			{
				var shrunk = box.Shrink(LabelInEntityTolerance);
				shrunk.Contains(label.Position)
					.Should().BeFalse(
						$"label '{label.Text}' at ({label.Position.X:F1},{label.Position.Y:F1}) in '{slug}' overlaps entity '{entityId}' box {box}");
			}
		}
	}

	[Test]
	[MethodDataSource(nameof(PublicErSlugs))]
	public void Labels_are_positioned_near_their_edges(string slug)
	{
		var source = DiagramExamples.All.First(d => d.Slug == slug).Source;
		var svg = MermaidRenderer.RenderSvg(source);
		var entities = ParseEntityBoxes(svg);
		var edges = ParseEdges(svg);
		var labels = ParseLabelPositions(svg);

		// Build a lookup: label text → edges that carry that label
		var edgesByLabel = new Dictionary<string, List<ParsedEdge>>(StringComparer.Ordinal);
		foreach (var edge in edges)
		{
			if (string.IsNullOrEmpty(edge.Label)) continue;
			if (!edgesByLabel.TryGetValue(edge.Label, out var list))
				edgesByLabel[edge.Label] = list = [];
			list.Add(edge);
		}

		foreach (var label in labels)
		{
			if (!edgesByLabel.TryGetValue(label.Text, out var matchingEdges))
				continue; // label text appears nowhere as an edge label — skip

			// At least one matching edge must have its midpoint within a generous radius of the label.
			// We use 2× the distance between the two entity centers as the max allowed distance.
			var anySane = matchingEdges.Any(edge =>
			{
				if (!entities.TryGetValue(edge.Entity1, out var box1)) return true;
				if (!entities.TryGetValue(edge.Entity2, out var box2)) return true;

				var c1 = box1.Center;
				var c2 = box2.Center;
				var edgeMid = new Pt((c1.X + c2.X) / 2, (c1.Y + c2.Y) / 2);
				var entityDist = Distance(c1, c2);
				var labelDist = Distance(label.Position, edgeMid);
				// Label should not be further than 1× the entity-to-entity distance from the midpoint
				return labelDist <= Math.Max(entityDist, 80);
			});

			anySane.Should().BeTrue(
				$"label '{label.Text}' at ({label.Position.X:F1},{label.Position.Y:F1}) in '{slug}' appears to be floating — not near any edge that carries it");
		}
	}

	// ── SVG parsers ──────────────────────────────────────────────────────────────

	private static Dictionary<string, BBox> ParseEntityBoxes(string svg)
	{
		var result = new Dictionary<string, BBox>(StringComparer.Ordinal);
		foreach (Match gm in EntityGroupPattern().Matches(svg))
		{
			var id = gm.Groups[1].Value;
			var rest = gm.Groups[2].Value;
			var rm = FirstRectPattern().Match(rest);
			if (!rm.Success) continue;
			if (!double.TryParse(rm.Groups[1].Value, out var x)) continue;
			if (!double.TryParse(rm.Groups[2].Value, out var y)) continue;
			if (!double.TryParse(rm.Groups[3].Value, out var w)) continue;
			if (!double.TryParse(rm.Groups[4].Value, out var h)) continue;
			result[id] = new BBox(x, y, w, h);
		}
		return result;
	}

	private static List<ParsedEdge> ParseEdges(string svg)
	{
		var result = new List<ParsedEdge>();
		foreach (Match m in EdgePattern().Matches(svg))
		{
			var e1 = m.Groups[1].Value;
			var e2 = m.Groups[2].Value;
			var label = m.Groups[3].Value;
			var d = m.Groups[4].Value;
			var start = ExtractFirstPoint(d);
			var end = ExtractLastPoint(d);
			if (start is null || end is null) continue;
			result.Add(new ParsedEdge(e1, e2, label, start.Value, end.Value));
		}
		return result;
	}

	private static List<ParsedLabel> ParseLabelPositions(string svg)
	{
		var result = new List<ParsedLabel>();
		foreach (Match m in LabelTextPattern().Matches(svg))
		{
			if (!double.TryParse(m.Groups[1].Value, out var x)) continue;
			if (!double.TryParse(m.Groups[2].Value, out var y)) continue;
			var text = m.Groups[3].Value.Trim();
			if (string.IsNullOrEmpty(text)) continue;
			result.Add(new ParsedLabel(new Pt(x, y), text));
		}
		return result;
	}

	// ── path point extraction ────────────────────────────────────────────────────

	private static Pt? ExtractFirstPoint(string d)
	{
		var m = FirstMovePattern().Match(d);
		if (!m.Success) return null;
		if (!double.TryParse(m.Groups[1].Value, out var x)) return null;
		if (!double.TryParse(m.Groups[2].Value, out var y)) return null;
		return new Pt(x, y);
	}

	private static Pt? ExtractLastPoint(string d)
	{
		// The last endpoint in any SVG path is the last consecutive pair of numbers.
		// We scan all numeric pairs and return the last one.
		Pt? last = null;
		foreach (Match m in NumericPairPattern().Matches(d))
		{
			if (!double.TryParse(m.Groups[1].Value, out var x)) continue;
			if (!double.TryParse(m.Groups[2].Value, out var y)) continue;
			last = new Pt(x, y);
		}
		return last;
	}

	private static double Distance(Pt a, Pt b)
	{
		var dx = a.X - b.X;
		var dy = a.Y - b.Y;
		return Math.Sqrt(dx * dx + dy * dy);
	}

	[Test]
	[MethodDataSource(nameof(PublicErSlugs))]
	public void Aspect_ratio_is_not_excessively_tall(string slug)
	{
		var source = DiagramExamples.All.First(d => d.Slug == slug).Source;
		var svg = MermaidRenderer.RenderSvg(source);
		var vbMatch = ViewBoxPattern().Match(svg);
		if (!vbMatch.Success) return;
		if (!double.TryParse(vbMatch.Groups[1].Value, out var w)) return;
		if (!double.TryParse(vbMatch.Groups[2].Value, out var h)) return;
		if (w <= 0) return;
		(h / w).Should().BeLessThan(4.0,
			$"diagram '{slug}' aspect ratio h/w={(h / w):F1} is too tall — suggests many long spanning edges");
	}

	// ── regex patterns ────────────────────────────────────────────────────────────

	[GeneratedRegex(
		@"viewBox=""[^""]*?\s([\d.]+)\s([\d.]+)""",
		RegexOptions.None, TimeoutMs)]
	private static partial Regex ViewBoxPattern();

	// Matches <g class="entity" data-id="X" ...> and captures up to the next </g>
	// We capture id and a chunk of the group body to find the first rect.
	[GeneratedRegex(
		@"<g class=""entity"" data-id=""([^""]+)""[^>]*>([\s\S]*?)</g>",
		RegexOptions.None, TimeoutMs)]
	private static partial Regex EntityGroupPattern();

	// First rect inside an entity group: x, y, width, height
	[GeneratedRegex(
		@"<rect x=""([^""]+)"" y=""([^""]+)"" width=""([^""]+)"" height=""([^""]+)""",
		RegexOptions.None, TimeoutMs)]
	private static partial Regex FirstRectPattern();

	// ER relationship path
	[GeneratedRegex(
		@"<path class=""er-relationship"" data-entity1=""([^""]+)"" data-entity2=""([^""]+)""[^>]*data-label=""([^""]*)""[^>]*d=""([^""]+)""",
		RegexOptions.None, TimeoutMs)]
	private static partial Regex EdgePattern();

	// Edge label text: has fill="var(--_text-sec)" directly on the element (not inside a tspan)
	// Entity headers use fill="var(--_text)" and font-weight="700"; attribute rows use tspan.
	[GeneratedRegex(
		@"<text x=""([^""]+)"" y=""([^""]+)""[^>]*fill=""var\(--_text-sec\)""[^>]*>([^<]+)</text>",
		RegexOptions.None, TimeoutMs)]
	private static partial Regex LabelTextPattern();

	// First M x,y in path d
	[GeneratedRegex(
		@"M\s*([\d.+-]+)[,\s]([\d.+-]+)",
		RegexOptions.None, TimeoutMs)]
	private static partial Regex FirstMovePattern();

	// Any consecutive pair of numbers in a path d string (for last-point extraction)
	[GeneratedRegex(
		@"([\d.]+)[,\s]([\d.]+)",
		RegexOptions.None, TimeoutMs)]
	private static partial Regex NumericPairPattern();

	// ── value types ──────────────────────────────────────────────────────────────

	private readonly record struct Pt(double X, double Y);

	private readonly record struct BBox(double X, double Y, double W, double H)
	{
		public Pt Center => new(X + W / 2, Y + H / 2);
		public bool Contains(Pt p) => p.X >= X && p.X <= X + W && p.Y >= Y && p.Y <= Y + H;
		public BBox Expand(double d) => new(X - d, Y - d, W + d * 2, H + d * 2);
		public BBox Shrink(double d) => new(X + d, Y + d, W - d * 2, H - d * 2);
		public override string ToString() => $"[{X:F0},{Y:F0} {W:F0}×{H:F0}]";
	}

	private readonly record struct ParsedEdge(string Entity1, string Entity2, string Label, Pt Start, Pt End);
	private readonly record struct ParsedLabel(Pt Position, string Text);
}
