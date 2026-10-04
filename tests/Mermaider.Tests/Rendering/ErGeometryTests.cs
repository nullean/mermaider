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

	// ── every ER example in the shared catalog, including the docs-builder diagrams ──
	// (merged into DiagramExamples.All — see examples/Mermaider.Examples/DiagramExamples.DocsBuilderErd.cs)
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

	/// <summary>
	/// Regression: BuildErPath collapsed every multi-waypoint path down to a 2-point S/J
	/// bezier using only the first and last points, regardless of how many waypoints
	/// Sugiyama actually routed through to dodge an intervening entity. A skip-layer edge
	/// (CodexSite→CodexGroup, "groups") was routed with an 8-point detour hugging the far
	/// left of the canvas specifically to avoid the unrelated Registry entity sitting
	/// between them — the simplified curve discarded every one of those waypoints and drew
	/// a straight-ish bezier directly through Registry's box instead. Long waypoint lists
	/// (see MaxWaypointsForCurveSimplification) must now render the full route, so this
	/// checks no point *or midpoint of a segment* on such a path lands inside an entity
	/// other than the edge's own two endpoints.
	/// </summary>
	[Test]
	public void Skip_layer_edge_does_not_pass_through_an_unrelated_entity()
	{
		const string diagram = """
			erDiagram
			  CodexSite ||--|| Registry : "environment = registry"
			  CodexSite ||--o{ CodexGroup : "groups"
			  CodexSite ||--o{ CodexDocumentationSet : "from LinkRegistry"
			  CodexDocumentationSet ||--|| DocumentationSet : "docset"
			  CodexDocumentationSet }o--o| CodexGroup : "codex.group"
			  Registry ||--|| LinkRegistry : "git: elastic/codex-link-index/{registry}"
			""";
		var svg = MermaidRenderer.RenderSvg(diagram);
		var entities = ParseEntityBoxes(svg);

		foreach (Match m in EdgePattern().Matches(svg))
		{
			var entity1 = m.Groups[1].Value;
			var entity2 = m.Groups[2].Value;
			var d = m.Groups[4].Value;
			var points = NumericPairPattern().Matches(d)
				.Select(pm => new Pt(
					double.Parse(pm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
					double.Parse(pm.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)))
				.ToList();
			if (points.Count < 3)
				continue; // simplified 2-point curves have no intermediate waypoints to check

			foreach (var (entityId, box) in entities)
			{
				if (string.Equals(entityId, entity1, StringComparison.Ordinal)) continue;
				if (string.Equals(entityId, entity2, StringComparison.Ordinal)) continue;
				var shrunk = box.Shrink(2.0);

				// Check every waypoint and every segment midpoint — a rectilinear route can
				// clear a box at its corner waypoints while still cutting through it mid-segment.
				for (var i = 0; i < points.Count; i++)
				{
					shrunk.Contains(points[i]).Should().BeFalse(
						$"edge {entity1}->{entity2} waypoint ({points[i].X:F1},{points[i].Y:F1}) passes through unrelated entity '{entityId}' box {box}");
					if (i == 0) continue;
					var mid = new Pt((points[i - 1].X + points[i].X) / 2, (points[i - 1].Y + points[i].Y) / 2);
					shrunk.Contains(mid).Should().BeFalse(
						$"edge {entity1}->{entity2} segment midpoint ({mid.X:F1},{mid.Y:F1}) passes through unrelated entity '{entityId}' box {box}");
				}
			}
		}
	}

	/// <summary>
	/// Regression: BuildErPath's curve-shape heuristics key off whether the edge's start
	/// and end X/Y differ. A self-loop's synthesized 4-point path starts and ends on the
	/// same entity edge (same X), which used to false-match the "cross-row S-curve" branch
	/// and collapse the loop into a zero-width straight line with the "manages"-style label
	/// sitting directly on the entity boundary instead of out at the visible loop.
	/// </summary>
	[Test]
	public void Self_loop_paths_have_a_visible_bulge()
	{
		const string diagram = """
			erDiagram
			  EMPLOYEE ||--o{ EMPLOYEE : "manages"
			""";
		var svg = MermaidRenderer.RenderSvg(diagram);
		var edges = ParseEdges(svg);
		var selfLoop = edges.Should().ContainSingle(e => e.Entity1 == e.Entity2).Subject;

		// The path's own points (not just first/last) must include an X well away from the
		// shared start/end X — otherwise the loop rendered as a degenerate straight line.
		var edgePath = EdgePattern().Match(svg).Groups[4].Value;
		var xs = NumericPairPattern().Matches(edgePath).Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();
		var spread = xs.Max() - xs.Min();
		spread.Should().BeGreaterThan(10.0,
			$"self-loop path 'd={edgePath}' should bulge out visibly, not collapse to a near-vertical line (X spread was {spread:F1}px)");

		// The label must sit out at the bulge, not back on the entity's edge (start.X).
		var labels = ParseLabelPositions(svg);
		var label = labels.Should().ContainSingle(l => l.Text == "manages").Subject;
		Math.Abs(label.Position.X - selfLoop.Start.X).Should().BeGreaterThan(10.0,
			$"self-loop label 'manages' at x={label.Position.X:F1} should be offset from the entity edge at x={selfLoop.Start.X:F1}, not sitting on top of it");
	}

	/// <summary>
	/// Regression: PushOutOfEntityBoxes resolved overlap one entity at a time. When a
	/// label's midpoint lands above a tightly-packed row of several entities, the cheapest
	/// escape from entity A can land inside neighbouring entity B, whose cheapest escape
	/// lands right back inside A — an infinite bounce, since the gaps between row entities
	/// were narrower than the label. Escaping the *combined* bounding box of every
	/// currently-overlapping entity in one move fixes this.
	/// </summary>
	[Test]
	public void Label_escapes_a_packed_row_of_entities_in_one_move()
	{
		const string diagram = """
			erDiagram
			  ApiDeclaration ||--o{ ApiSpecVersion : "renders"
			  OpenApiRepository ||--|| ApiVersionIndex : "index.json"
			  ApiDeclaration }o--|| Product : "product"
			  Repository ||--o{ OpenApiSpec : "publishes"
			""";
		var svg = MermaidRenderer.RenderSvg(diagram);
		var entities = ParseEntityBoxes(svg);
		var labels = ParseLabelPositions(svg);

		foreach (var label in labels)
		{
			foreach (var (entityId, box) in entities)
			{
				var shrunk = box.Shrink(LabelInEntityTolerance);
				shrunk.Contains(label.Position)
					.Should().BeFalse(
						$"label '{label.Text}' at ({label.Position.X:F1},{label.Position.Y:F1}) overlaps entity '{entityId}' box {box}");
			}
		}
	}

	/// <summary>
	/// Regression: label-vs-label pairwise separation needs roughly N-1 full passes to
	/// propagate end-to-end for a chain of N overlapping labels (fixing the outermost pair
	/// can reintroduce a smaller overlap on the pair just inside it). 8 iterations converged
	/// for 2-3 way conflicts but left a 4-way convergence (several edges landing on the same
	/// entity from different sources, all at the same layer) visibly overlapping.
	/// </summary>
	[Test]
	public void Many_labels_converging_on_one_entity_do_not_overlap()
	{
		const string diagram = """
			erDiagram
			  Repository ||--o{ OpenApiSpec : "publishes, per branch"
			  OpenApiRepository ||--o{ OpenApiSpec : "org/repo/branch/spec"
			  OpenApiRepository ||--|| ApiVersionIndex : "index.json"
			  ApiDeclaration }o--|| OpenApiSpec : "repository + spec"
			""";
		var svg = MermaidRenderer.RenderSvg(diagram);
		var labels = ParseLabelPositions(svg);

		for (var i = 0; i < labels.Count; i++)
		{
			for (var j = i + 1; j < labels.Count; j++)
			{
				var a = labels[i];
				var b = labels[j];
				var (aw, ah) = MeasureLabel(a.Text);
				var (bw, bh) = MeasureLabel(b.Text);
				var overlaps = Math.Abs(a.Position.X - b.Position.X) < ((aw + bw) / 2)
					&& Math.Abs(a.Position.Y - b.Position.Y) < ((ah + bh) / 2);
				overlaps.Should().BeFalse(
					$"label '{a.Text}' at ({a.Position.X:F1},{a.Position.Y:F1}) overlaps label '{b.Text}' at ({b.Position.X:F1},{b.Position.Y:F1})");
			}
		}
	}

	private static (double w, double h) MeasureLabel(string text)
	{
		var metrics = Mermaider.Text.TextMetrics.MeasureMultiline(
			text.AsSpan(),
			Mermaider.Rendering.RenderConstants.FontSizes.EdgeLabel,
			Mermaider.Rendering.RenderConstants.FontWeights.EdgeLabel);
		return (metrics.Width + 8, metrics.Height + 6);
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
			// We use 1× the distance between the two entity centers as the max allowed distance.
			var anySane = matchingEdges.Any(edge =>
			{
				if (!entities.TryGetValue(edge.Entity1, out var box1)) return true;
				if (!entities.TryGetValue(edge.Entity2, out var box2)) return true;

				var c1 = box1.Center;
				var c2 = box2.Center;

				// Self-loops are rectangular loops out of the left or right side; the label sits on the outer vertical,
				// 36px plus half the label out from the entity edge.
				if (edge.Entity1 == edge.Entity2)
				{
					var leftMid = new Pt(box1.X, c1.Y);
					var rightMid = new Pt(box1.X + box1.W, c1.Y);
					var selfThreshold = 36 + 150;
					return Math.Min(Distance(label.Position, leftMid), Distance(label.Position, rightMid)) <= selfThreshold;
				}

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

	/// <summary>
	/// No relationship line may run under an entity that is not one of its two ends. (Entities are painted after the lines, so a
	/// line passing through one would silently disappear behind it.) ELK produces zero of these on every example.
	/// </summary>
	[Test]
	[MethodDataSource(nameof(PublicErSlugs))]
	public void Edges_do_not_pass_under_unrelated_entities(string slug)
	{
		var source = DiagramExamples.All.First(d => d.Slug == slug).Source;
		var svg = MermaidRenderer.RenderSvg(source);
		var entities = ParseEntityBoxes(svg);
		const double inset = 2;

		foreach (Match m in EdgePattern().Matches(svg))
		{
			var e1 = m.Groups[1].Value;
			var e2 = m.Groups[2].Value;
			if (e1 == e2)
				continue;
			var pts = NumericPairPattern().Matches(m.Groups[4].Value)
				.Select(p => new Pt(double.Parse(p.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), double.Parse(p.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)))
				.ToList();

			foreach (var (id, box) in entities)
			{
				if (id == e1 || id == e2)
					continue;
				for (var i = 0; i < pts.Count - 1; i++)
				{
					var x0 = Math.Min(pts[i].X, pts[i + 1].X);
					var x1 = Math.Max(pts[i].X, pts[i + 1].X);
					var y0 = Math.Min(pts[i].Y, pts[i + 1].Y);
					var y1 = Math.Max(pts[i].Y, pts[i + 1].Y);
					var crosses = x1 > box.X + inset && x0 < box.X + box.W - inset && y1 > box.Y + inset && y0 < box.Y + box.H - inset;
					crosses.Should().BeFalse(
						$"edge {e1}→{e2} in '{slug}' runs under entity '{id}' (segment ({pts[i].X:F0},{pts[i].Y:F0})→({pts[i + 1].X:F0},{pts[i + 1].Y:F0}))");
				}
			}
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

	/// <summary>
	/// Regression: AlignToConnections's median-pull can ping-pong two siblings that share a
	/// child — the child is pulled toward one sibling, that sibling is pulled back toward
	/// the child's shifted position, overshooting into the other sibling — leaving two
	/// entity boxes in the same layer literally overlapping (e.g. DocumentationSetNavigation
	/// and TocItem in a docs-builder navigation diagram). No two entity boxes in a rendered
	/// diagram should ever intersect.
	/// </summary>
	[Test]
	[MethodDataSource(nameof(PublicErSlugs))]
	public void Entity_boxes_do_not_overlap(string slug)
	{
		var source = DiagramExamples.All.First(d => d.Slug == slug).Source;
		var svg = MermaidRenderer.RenderSvg(source);
		var entities = ParseEntityBoxes(svg).ToList();

		for (var i = 0; i < entities.Count; i++)
		{
			for (var j = i + 1; j < entities.Count; j++)
			{
				var (idA, boxA) = entities[i];
				var (idB, boxB) = entities[j];
				var overlaps = (boxA.X < boxB.X + boxB.W) && (boxA.X + boxA.W > boxB.X)
					&& (boxA.Y < boxB.Y + boxB.H) && (boxA.Y + boxA.H > boxB.Y);
				overlaps.Should().BeFalse(
					$"entity '{idA}' box {boxA} overlaps entity '{idB}' box {boxB} in '{slug}'");
			}
		}
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

	// Edge label text: font-weight 400 with fill="var(--_text)" directly on the element (headers are 700, badges use --_text-sec)
	// Entity headers use fill="var(--_text)" and font-weight="700"; attribute rows use tspan.
	[GeneratedRegex(
		@"<text x=""([^""]+)"" y=""([^""]+)""[^>]*font-weight=""400"" fill=""var\(--_text\)""[^>]*>([^<]+)</text>",
		RegexOptions.None, TimeoutMs)]
	private static partial Regex LabelTextPattern();

	// First M x,y in path d
	[GeneratedRegex(
		@"M\s*([\d.+-]+)[,\s]([\d.+-]+)",
		RegexOptions.None, TimeoutMs)]
	private static partial Regex FirstMovePattern();

	// Any consecutive pair of numbers in a path d string (for last-point extraction)
	[GeneratedRegex(
		@"(-?[\d.]+)[,\s](-?[\d.]+)",
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
