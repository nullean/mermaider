using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mermaider.Models;
using Mermaider.Parsing;

namespace Mermaider.Tests.Snapshots.LayoutSkeleton;

/// <summary>
/// Builds a <see cref="LayoutSkeleton"/> from a real mermaid.js-rendered flowchart SVG
/// (produced by <c>mmdc</c> — see <see cref="Mermaider.Tests.MmdcRenderer"/>).
///
/// Node ids follow <c>my-svg-flowchart-{Name}-{idx}</c>; cluster (subgraph) ids follow
/// <c>my-svg-{SubgraphId}</c> exactly, with no index suffix. Subgraph *membership* (which
/// nodes/child-subgraphs belong to which subgraph) is not re-derived from the SVG at all — like
/// <see cref="MermaidJsSkeletonExtractor"/> does for ER relationship identity, it's a static
/// property of the diagram source (both engines parse the same input), so it comes from
/// <see cref="FlowchartParser"/> directly; only each cluster's rendered bounding box comes from
/// the SVG.
///
/// mermaid.js renders a node's shape as one of four markup forms depending on the flowchart
/// shape syntax used, and this extractor must recognize all four to avoid the exact "silent
/// empty extraction" bug already found and fixed for ER (see MermaidJsSkeletonExtractor):
///  - rect (square/round-edge `[...]`, stadium `(...)`, etc.): plain
///    <c>&lt;rect class="...label-container..." x="-hw" y="-hh" width="w" height="h"/&gt;</c>.
///  - outer-path (subroutine, hexagon and other multi-segment outlines): a
///    <c>&lt;g class="...outer-path"&gt;&lt;path d="M-hw -hh ..."&gt;</c>, optionally with its
///    own extra <c>transform="translate(dx,dy)"</c> on the inner &lt;g&gt; (some irregular
///    shapes aren't centered on the node's own translate).
///  - polygon (diamond `{...}`, trapezoid, parallelogram): a
///    <c>&lt;polygon points="x1,y1 x2,y2 ..." transform="translate(dx,dy)"/&gt;</c> whose point
///    set is in a local frame that itself may be offset from the node center.
///  - circle (small circle shapes): <c>&lt;circle r="radius" cx="cx" cy="cy"/&gt;</c>.
/// </summary>
internal static partial class MermaidJsFlowchartSkeletonExtractor
{
	private const int TimeoutMs = 2000;

	public static LayoutSkeleton Extract(string svg, string diagramSource)
	{
		var (cleaned, _) = DiagramPreprocessor.Process(diagramSource);
		var lines = MermaidRenderer.PreprocessLines(cleaned);
		var graph = FlowchartParser.Parse(lines);

		var boxes = ExtractNodeBoxes(svg).Where(b => graph.Nodes.ContainsKey(b.Id)).ToList();
		if (boxes.Count != graph.Nodes.Count)
		{
			var found = boxes.Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
			var missing = graph.Nodes.Keys.Where(id => !found.Contains(id));
			throw new InvalidOperationException(
				$"mermaid.js flowchart SVG node extraction found {boxes.Count} box(es) but the diagram source has " +
				$"{graph.Nodes.Count} nodes. Missing: {string.Join(", ", missing)}.");
		}

		var subgraphIds = new HashSet<string>(StringComparer.Ordinal);
		CollectSubgraphIds(graph.Subgraphs, subgraphIds);
		var clusterBoxById = ExtractClusterBoxes(svg)
			.Where(g => subgraphIds.Contains(g.Id))
			.ToDictionary(g => g.Id, StringComparer.Ordinal);
		if (clusterBoxById.Count != subgraphIds.Count)
		{
			var missing = subgraphIds.Where(id => !clusterBoxById.ContainsKey(id));
			throw new InvalidOperationException(
				$"mermaid.js flowchart SVG cluster extraction found {clusterBoxById.Count} cluster(s) but the " +
				$"diagram source has {subgraphIds.Count} subgraphs. Missing: {string.Join(", ", missing)}.");
		}

		var groups = new List<(string Id, string? ParentId, IReadOnlyList<string> MemberNodeIds, double X, double Y, double W, double H)>();
		CollectGroups(graph.Subgraphs, clusterBoxById, parentId: null, groups);

		var edgePointsById = ExtractEdgePoints(svg);
		var edges = new List<(string From, string To, string Label, Point Start, Point End)>();
		var dupIndex = new Dictionary<(string, string), int>();
		for (var ei = 0; ei < graph.Edges.Count; ei++)
		{
			var e = graph.Edges[ei];
			if (e.Style == EdgeStyle.Invisible)
				continue; // not rendered as a path in the SVG at all — nothing to compare

			// An edge whose source/target in the diagram source names a *subgraph* (e.g.
			// `router --> subnet1`) gets redirected by FlowchartParser to a representative
			// child node (e.Source/e.Target below) so Mermaider's own layout has a concrete
			// node to attach to. mermaid.js's rendered SVG does the opposite: dagre-d3's
			// compound graph keeps the edge attached to the *subgraph itself* for id purposes
			// (data-id="L_router_subnet1_0", not "L_router_compute1_0") since the cluster, not
			// any specific child, is the actual endpoint. The svg-lookup key must use the
			// original subgraph id; the skeleton's own From/To keep Mermaider's redirected
			// child id so they line up with the node ids MermaiderGraphSkeletonExtractor uses.
			var hasRedirect = graph.SubgraphEdgeRedirections.TryGetValue(ei, out var redir);
			var lookupSource = hasRedirect && redir.SourceSubgraph is { } srcSg ? srcSg : e.Source;
			var lookupTarget = hasRedirect && redir.TargetSubgraph is { } tgtSg ? tgtSg : e.Target;

			var key = (lookupSource, lookupTarget);
			var idx = dupIndex.TryGetValue(key, out var v) ? v : 0;
			dupIndex[key] = idx + 1;
			var dataId = $"L_{lookupSource}_{lookupTarget}_{idx}";

			if (!edgePointsById.TryGetValue(dataId, out var pts))
			{
				throw new InvalidOperationException(
					$"mermaid.js flowchart SVG has no edge with data-id '{dataId}' — expected one for declaration-order " +
					$"edge {e.Source} -> {e.Target} (duplicate index {idx}).");
			}
			edges.Add((e.Source, e.Target, e.Label ?? "", pts.Start, pts.End));
		}

		return LayoutSkeletonBuilder.Build(boxes, edges, groups, graph.Direction);
	}

	private static void CollectSubgraphIds(IReadOnlyList<MermaidSubgraph> subgraphs, HashSet<string> result)
	{
		foreach (var sg in subgraphs)
		{
			result.Add(sg.Id);
			CollectSubgraphIds(sg.Children, result);
		}
	}

	private static void CollectGroups(
		IReadOnlyList<MermaidSubgraph> subgraphs,
		Dictionary<string, (string Id, double X, double Y, double W, double H)> clusterBoxById,
		string? parentId,
		List<(string Id, string? ParentId, IReadOnlyList<string> MemberNodeIds, double X, double Y, double W, double H)> result)
	{
		foreach (var sg in subgraphs)
		{
			var box = clusterBoxById[sg.Id];
			result.Add((sg.Id, parentId, sg.NodeIds, box.X, box.Y, box.W, box.H));
			CollectGroups(sg.Children, clusterBoxById, sg.Id, result);
		}
	}

	private static double ParseDouble(string s) => double.Parse(s, CultureInfo.InvariantCulture);

	private static List<(string Id, double X, double Y, double W, double H)> ExtractNodeBoxes(string svg)
	{
		var boxes = new List<(string Id, double X, double Y, double W, double H)>();
		foreach (Match header in NodeHeaderPattern().Matches(svg))
		{
			var id = header.Groups[1].Value;
			var cx = ParseDouble(header.Groups[2].Value);
			var cy = ParseDouble(header.Groups[3].Value);

			var windowStart = header.Index + header.Length;
			var window = svg.Substring(windowStart, Math.Min(600, svg.Length - windowStart));

			if (TryMatchShape(window, cx, cy, out var box))
				boxes.Add((id, box.X, box.Y, box.W, box.H));
		}
		return boxes;
	}

	// Internal (not private) so MermaidJsStateSkeletonExtractor can reuse the same shape-bbox
	// detection — mermaid.js renders state-diagram leaf nodes with the identical rect/
	// outer-path/polygon/circle/bare-path markup family flowcharts use; only the id and cluster
	// conventions differ between the two diagram types.
	internal static bool TryMatchShapeForReuse(string window, double cx, double cy, out (double X, double Y, double W, double H) box) =>
		TryMatchShape(window, cx, cy, out box);

	private static bool TryMatchShape(string window, double cx, double cy, out (double X, double Y, double W, double H) box)
	{
		var rect = RectPattern().Match(window);
		if (rect.Success)
		{
			var hw = Math.Abs(ParseDouble(rect.Groups[1].Value));
			var hh = Math.Abs(ParseDouble(rect.Groups[2].Value));
			var w = ParseDouble(rect.Groups[3].Value);
			var h = ParseDouble(rect.Groups[4].Value);
			box = (cx - hw, cy - hh, w, h);
			return true;
		}

		var outerPath = OuterPathPattern().Match(window);
		if (outerPath.Success)
		{
			var dx = outerPath.Groups[1].Success ? ParseDouble(outerPath.Groups[1].Value) : 0;
			var dy = outerPath.Groups[2].Success ? ParseDouble(outerPath.Groups[2].Value) : 0;
			var hw = Math.Abs(ParseDouble(outerPath.Groups[3].Value));
			var hh = Math.Abs(ParseDouble(outerPath.Groups[4].Value));
			box = (cx + dx - hw, cy + dy - hh, hw * 2, hh * 2);
			return true;
		}

		var polygon = PolygonPattern().Match(window);
		if (polygon.Success)
		{
			var points = polygon.Groups[1].Value
				.Split(' ', StringSplitOptions.RemoveEmptyEntries)
				.Select(p => p.Split(','))
				.Select(xy => (X: ParseDouble(xy[0]), Y: ParseDouble(xy[1])))
				.ToList();
			var minX = points.Min(p => p.X);
			var maxX = points.Max(p => p.X);
			var minY = points.Min(p => p.Y);
			var maxY = points.Max(p => p.Y);
			var dx = polygon.Groups[2].Success ? ParseDouble(polygon.Groups[2].Value) : 0;
			var dy = polygon.Groups[3].Success ? ParseDouble(polygon.Groups[3].Value) : 0;
			box = (cx + dx + minX, cy + dy + minY, maxX - minX, maxY - minY);
			return true;
		}

		var circle = CirclePattern().Match(window);
		if (circle.Success)
		{
			var r = ParseDouble(circle.Groups[1].Value);
			var ccx = circle.Groups[2].Success ? ParseDouble(circle.Groups[2].Value) : 0;
			var ccy = circle.Groups[3].Success ? ParseDouble(circle.Groups[3].Value) : 0;
			box = (cx + ccx - r, cy + ccy - r, r * 2, r * 2);
			return true;
		}

		// Cylinder (database `[(...)]`) and similar shapes render as a bare <path> (no wrapping
		// <g class="outer-path">) whose own `transform="translate(tx,ty)"` *is* the centering
		// offset — unlike OuterPathPattern's convention where the path's first M coordinate
		// gives the half-extents, here the path's `d` starts at an arbitrary point on the
		// outline (elliptical arcs aren't a simple "top-left corner" start) and mermaid.js's
		// template instead emits tx/ty exactly equal to -halfWidth/-halfHeight directly on the
		// path's own transform.
		var barePath = BarePathPattern().Match(window);
		if (barePath.Success)
		{
			var tx = ParseDouble(barePath.Groups[1].Value);
			var ty = ParseDouble(barePath.Groups[2].Value);
			box = (cx + tx, cy + ty, Math.Abs(tx) * 2, Math.Abs(ty) * 2);
			return true;
		}

		// Last-resort generic fallback for shapes with no class anywhere to anchor a more
		// specific pattern to — state-diagram pseudostates (choice diamonds, fork/join bars)
		// render as a bare, unclassed <g><path d="..."> with no transform of their own at all;
		// the path's own M/C/L coordinates ARE already relative to the node's translate. Rather
		// than modeling each pseudostate shape's exact geometry, take the bounding box of every
		// numeric coordinate pair appearing in `d` (including bezier control points, which for
		// these shapes sit only marginally outside the true outline) — close enough for
		// placement/order comparison, which only needs a "roughly right" box.
		var barePlainPath = BarePlainPathPattern().Match(window);
		if (barePlainPath.Success)
		{
			var coords = CoordPairPattern().Matches(barePlainPath.Groups[1].Value)
				.Select(m => (X: ParseDouble(m.Groups[1].Value), Y: ParseDouble(m.Groups[2].Value)))
				.ToList();
			if (coords.Count > 0)
			{
				var minX = coords.Min(c => c.X);
				var maxX = coords.Max(c => c.X);
				var minY = coords.Min(c => c.Y);
				var maxY = coords.Max(c => c.Y);
				box = (cx + minX, cy + minY, maxX - minX, maxY - minY);
				return true;
			}
		}

		box = default;
		return false;
	}

	private static List<(string Id, double X, double Y, double W, double H)> ExtractClusterBoxes(string svg)
	{
		var boxes = new List<(string Id, double X, double Y, double W, double H)>();
		foreach (Match m in ClusterPattern().Matches(svg))
		{
			var id = m.Groups[1].Value;
			var x = ParseDouble(m.Groups[2].Value);
			var y = ParseDouble(m.Groups[3].Value);
			var w = ParseDouble(m.Groups[4].Value);
			var h = ParseDouble(m.Groups[5].Value);
			boxes.Add((id, x, y, w, h));
		}
		return boxes;
	}

	private static Dictionary<string, (Point Start, Point End)> ExtractEdgePoints(string svg)
	{
		var result = new Dictionary<string, (Point Start, Point End)>(StringComparer.Ordinal);
		foreach (Match m in EdgePattern().Matches(svg))
		{
			var dataId = m.Groups[1].Value;
			var points = DecodePoints(m.Groups[2].Value);
			if (points.Count < 2)
				continue;
			result[dataId] = (points[0], points[^1]);
		}
		return result;
	}

	private static List<Point> DecodePoints(string base64)
	{
		var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64));
		using var doc = JsonDocument.Parse(json);
		var result = new List<Point>();
		foreach (var el in doc.RootElement.EnumerateArray())
			result.Add(new Point(el.GetProperty("x").GetDouble(), el.GetProperty("y").GetDouble()));
		return result;
	}

	[GeneratedRegex(
		@"<g class=""node[^""]*"" id=""my-svg-flowchart-([A-Za-z0-9_]+)-\d+""[^>]*transform=""translate\((-?[\d.]+),\s*(-?[\d.]+)\)""[^>]*>",
		RegexOptions.None, TimeoutMs)]
	private static partial Regex NodeHeaderPattern();

	[GeneratedRegex(
		@"\A<rect class=""[^""]*label-container[^""]*""[^>]*?\sx=""(-?[\d.]+)""[^>]*?\sy=""(-?[\d.]+)""[^>]*?\swidth=""([\d.]+)""[^>]*?\sheight=""([\d.]+)""",
		RegexOptions.None, TimeoutMs)]
	private static partial Regex RectPattern();

	[GeneratedRegex(
		@"\A<g class=""[^""]*outer-path""(?:\s+transform=""translate\((-?[\d.]+),\s*(-?[\d.]+)\)"")?[^>]*><path d=""M(-?[\d.]+)\s(-?[\d.]+)\s",
		RegexOptions.None, TimeoutMs)]
	private static partial Regex OuterPathPattern();

	[GeneratedRegex(
		@"\A<polygon points=""([^""]+)""[^>]*?(?:transform=""translate\((-?[\d.]+),\s*(-?[\d.]+)\)"")?",
		RegexOptions.None, TimeoutMs)]
	private static partial Regex PolygonPattern();

	[GeneratedRegex(
		@"\A<circle class=""[^""]*label-container[^""]*""[^>]*?\sr=""([\d.]+)""(?:[^>]*?\scx=""(-?[\d.]+)"")?(?:[^>]*?\scy=""(-?[\d.]+)"")?",
		RegexOptions.None, TimeoutMs)]
	private static partial Regex CirclePattern();

	[GeneratedRegex(
		@"\A<path d=""M[^""]*""\s*class=""[^""]*outer-path[^""]*""[^>]*?\stransform=""translate\((-?[\d.]+),\s*(-?[\d.]+)\)""",
		RegexOptions.None, TimeoutMs)]
	private static partial Regex BarePathPattern();

	[GeneratedRegex(@"\A<g>\s*<path d=""([^""]+)""", RegexOptions.None, TimeoutMs)]
	private static partial Regex BarePlainPathPattern();

	[GeneratedRegex(@"(-?\d+(?:\.\d+)?)[ ,](-?\d+(?:\.\d+)?)", RegexOptions.None, TimeoutMs)]
	private static partial Regex CoordPairPattern();

	[GeneratedRegex(
		@"<g class=""cluster[^""]*"" id=""my-svg-([A-Za-z0-9_]+)""[^>]*>\s*<rect[^>]*?\sx=""(-?[\d.]+)""[^>]*?\sy=""(-?[\d.]+)""[^>]*?\swidth=""([\d.]+)""[^>]*?\sheight=""([\d.]+)""",
		RegexOptions.None, TimeoutMs)]
	private static partial Regex ClusterPattern();

	[GeneratedRegex(
		@"<path[^>]*class=""[^""]*flowchart-link[^""]*""[^>]*data-id=""(L_[^""]+)""[^>]*data-points=""([^""]+)""",
		RegexOptions.None, TimeoutMs)]
	private static partial Regex EdgePattern();
}
