using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mermaider.Models;
using Mermaider.Parsing;

namespace Mermaider.Tests.Snapshots.LayoutSkeleton;

/// <summary>
/// Builds a <see cref="LayoutSkeleton"/> from a real mermaid.js-rendered state diagram SVG
/// (produced by <c>mmdc</c>). Node/cluster markup is the same "rect / outer-path / polygon /
/// circle" shape family <see cref="MermaidJsFlowchartSkeletonExtractor"/> already handles, so
/// this extractor reuses that shape-detection logic; only the id conventions and edge
/// identification differ:
///  - leaf state ids: <c>my-svg-state-{Name}-{idx}</c>.
///  - composite (nested) state clusters: class <c>statediagram-state statediagram-cluster</c>,
///    with a <c>data-id="{Name}"</c> attribute giving the clean id directly (no suffix to
///    strip, unlike flowchart clusters' bare <c>my-svg-{Id}</c> or leaf states' `-idx` suffix).
///  - edges: <c>id="my-svg-edge{N}"</c>, a single global declaration-order counter — simpler
///    than flowchart's per-(source,target) duplicate-index scheme, and notably *not* sensitive
///    to subgraph-edge-redirection (state edges are matched by pure index, not by an id string
///    built from source/target names), so no redirection lookup is needed here.
///
/// <b>Start/end pseudostates are deliberately excluded from the comparison.</b> Mermaider's
/// <see cref="StateParser"/> names them with global counters (<c>_start</c>, <c>_end</c>,
/// <c>_start2</c>, <c>_end2</c>, ...) scoped to the whole diagram, while mermaid.js's rendered
/// SVG names them per-scope (<c>root_start</c>, <c>First_start</c>, <c>First_end</c>,
/// <c>Second_start</c>, ...) — the two naming schemes don't correspond string-for-string, so
/// id-based matching (the same trick every other extractor in this directory relies on) simply
/// doesn't apply to them. Comparing real, named states is the actual object of interest; a
/// [*] circle's exact rank relative to its sibling circles is not.
/// </summary>
internal static partial class MermaidJsStateSkeletonExtractor
{
	private const int TimeoutMs = 2000;

	public static LayoutSkeleton Extract(string svg, string diagramSource)
	{
		var (cleaned, _) = DiagramPreprocessor.Process(diagramSource);
		var lines = MermaidRenderer.PreprocessLines(cleaned);
		var graph = StateParser.Parse(lines);

		var realNodeIds = graph.Nodes
			.Where(kv => kv.Value.Shape is not (NodeShape.StateStart or NodeShape.StateEnd))
			.Select(kv => kv.Key)
			.ToHashSet(StringComparer.Ordinal);

		var boxes = ExtractNodeBoxes(svg).Where(b => realNodeIds.Contains(b.Id)).ToList();
		if (boxes.Count != realNodeIds.Count)
		{
			var found = boxes.Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
			var missing = realNodeIds.Where(id => !found.Contains(id));
			throw new InvalidOperationException(
				$"mermaid.js state SVG node extraction found {boxes.Count} box(es) but the diagram source has " +
				$"{realNodeIds.Count} real (non pseudostate) states. Missing: {string.Join(", ", missing)}.");
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
				$"mermaid.js state SVG cluster extraction found {clusterBoxById.Count} cluster(s) but the diagram " +
				$"source has {subgraphIds.Count} composite states. Missing: {string.Join(", ", missing)}.");
		}

		var groups = new List<(string Id, string? ParentId, IReadOnlyList<string> MemberNodeIds, double X, double Y, double W, double H)>();
		CollectGroups(graph.Subgraphs, clusterBoxById, parentId: null, groups);

		var edgePointsByIndex = ExtractEdgePointsByIndex(svg);
		var svgIndexOfEdge = MapEdgesToCombinedSvgIndex(lines, graph.Edges.Count);
		var edges = new List<(string From, string To, string Label, Point Start, Point End)>();
		for (var i = 0; i < graph.Edges.Count; i++)
		{
			var e = graph.Edges[i];
			if (e.Style == EdgeStyle.Invisible)
				continue;

			var svgIndex = svgIndexOfEdge[i];
			if (!edgePointsByIndex.TryGetValue(svgIndex, out var pts))
			{
				throw new InvalidOperationException(
					$"mermaid.js state SVG has no edge{svgIndex} — expected one for declaration-order edge " +
					$"{e.Source} -> {e.Target} (transition #{i}).");
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

	// mermaid.js numbers its rendered `id="my-svg-edgeN"` paths with a *single counter shared
	// between real transitions and note connector lines*, assigned in source declaration
	// order — e.g. a diagram with 4 transitions and 2 notes interleaved after the 4th
	// transition renders transitions 0-3 as edge0-3, then the two notes consume edge4 and
	// edge5, so the 5th (final) transition becomes edge6, not edge4. graph.Edges only contains
	// real transitions, so its plain declaration index (0,1,2,3,4) undercounts once any note
	// precedes a later transition in the source. This walks the preprocessed source lines once,
	// incrementing one combined counter for every note *and* every transition line in file
	// order, to recover the SVG index each real transition actually got.
	private static int[] MapEdgesToCombinedSvgIndex(string[] lines, int edgeCount)
	{
		var map = new int[edgeCount];
		var combined = 0;
		var edgeCursor = 0;
		foreach (var raw in lines)
		{
			var line = raw.Trim();
			if (edgeCursor >= edgeCount)
				break;
			if (NoteStartPattern().IsMatch(line))
			{
				combined++;
			}
			else if (line.Contains("-->", StringComparison.Ordinal))
			{
				map[edgeCursor] = combined;
				edgeCursor++;
				combined++;
			}
		}
		return map;
	}

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

			if (MermaidJsFlowchartSkeletonExtractor.TryMatchShapeForReuse(window, cx, cy, out var box))
				boxes.Add((id, box.X, box.Y, box.W, box.H));
		}
		return boxes;
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

	private static Dictionary<int, (Point Start, Point End)> ExtractEdgePointsByIndex(string svg)
	{
		var result = new Dictionary<int, (Point Start, Point End)>();
		foreach (Match m in EdgePattern().Matches(svg))
		{
			var index = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
			var points = DecodePoints(m.Groups[2].Value);
			if (points.Count < 2)
				continue;
			result[index] = (points[0], points[^1]);
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
		@"<g class=""node\s*[^""]*"" id=""my-svg-state-([A-Za-z0-9_]+)-\d+""[^>]*transform=""translate\((-?[\d.]+),\s*(-?[\d.]+)\)""[^>]*>",
		RegexOptions.None, TimeoutMs)]
	private static partial Regex NodeHeaderPattern();

	[GeneratedRegex(
		@"<g class=""statediagram-state statediagram-cluster"" id=""[^""]*"" data-id=""([^""]+)""[^>]*>\s*<g>\s*<rect[^>]*?\sx=""(-?[\d.]+)""[^>]*?\sy=""(-?[\d.]+)""[^>]*?\swidth=""([\d.]+)""[^>]*?\sheight=""([\d.]+)""",
		RegexOptions.None, TimeoutMs)]
	private static partial Regex ClusterPattern();

	[GeneratedRegex(
		@"<path[^>]*id=""my-svg-edge(\d+)""[^>]*data-points=""([^""]+)""",
		RegexOptions.None, TimeoutMs)]
	private static partial Regex EdgePattern();

	[GeneratedRegex(@"^note\s+(left|right)\s+of\s+", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex NoteStartPattern();
}
