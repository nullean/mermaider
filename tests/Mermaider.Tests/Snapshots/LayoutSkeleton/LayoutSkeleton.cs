using System.Text;

namespace Mermaider.Tests.Snapshots.LayoutSkeleton;

/// <summary>
/// Which side of an entity's bounding box an edge attaches to. Derived purely from geometry
/// (comparing the edge's start/end point against its owning entity's box edges), so it means
/// the same thing whether the box+point pair came from Mermaider's own model or from parsing
/// a rendered mermaid.js SVG.
/// </summary>
internal enum EdgeSide { Top, Bottom, Left, Right }

/// <summary>Returns the mirror of a side along the order axis (Left↔Right for TB/BT layouts,
/// Top↔Bottom for LR/RL). Used by mirror-tolerant comparisons.</summary>
internal static class EdgeSideExtensions
{
	public static EdgeSide MirrorLR(this EdgeSide s) => s switch
	{
		EdgeSide.Left  => EdgeSide.Right,
		EdgeSide.Right => EdgeSide.Left,
		_              => s,
	};
}

/// <summary>
/// One entity, reduced to just the facts that matter for placement/ordering comparisons:
/// which layer (rank) it sits in, and its left-to-right position among siblings in that
/// layer. <see cref="CenterX"/>/<see cref="CenterY"/>/<see cref="Width"/>/<see cref="Height"/>
/// are kept only as the raw geometry <see cref="LayoutSkeletonBuilder"/> derived Layer/
/// OrderInLayer from — comparisons should use the ordinal fields, not these.
/// </summary>
internal sealed record SkeletonNode(
	string Id, int Layer, int OrderInLayer, double CenterX, double CenterY, double Width, double Height);

/// <summary>
/// One relationship, reduced to its endpoints' entity ids, which side of each entity's
/// box it attaches to, and the run-length route signature of the edge polyline. The route
/// signature encodes the dominant direction of each polyline segment: D=down, U=up, R=right,
/// L=left, run-length encoded and space-separated (e.g. "D R D" for a typical ELK orthogonal
/// TB edge). Empty string for self-loops or degenerate paths. Interior waypoints are not
/// stored — use the signature to compare routing strategy between engines.
/// </summary>
internal sealed record SkeletonEdge(
	string From, string To, string Label, EdgeSide FromSide, EdgeSide ToSide, bool IsSelfLoop,
	string RouteSig);

/// <summary>
/// Port attachment order: which edges attach to a particular side of a node, ordered by
/// their attachment coordinate (left-to-right for Top/Bottom, top-to-bottom for Left/Right).
/// EdgeIndices are 0-based indices into the stable-sorted <see cref="LayoutSkeleton.Edges"/>
/// list (same order as <see cref="LayoutSkeleton.Serialize"/> writes them). Only ports with
/// at least two edges are recorded — a single-edge port has no order to compare.
/// </summary>
internal sealed record SkeletonPort(string NodeId, EdgeSide Side, IReadOnlyList<int> EdgeIndices);

/// <summary>
/// One subgraph/cluster, reduced to the same ordinal facts as <see cref="SkeletonNode"/> (which
/// layer it sits in among its siblings, its left-to-right order there) plus the containment
/// structure that makes a group a group: its parent group id (null at the top level) and the
/// ids of the leaf nodes it directly contains (not recursively — a nested child group's members
/// are attributed to that child group, not re-listed here). Layer/OrderInLayer are computed
/// over groups exactly like they are over nodes (same box-overlap clustering, run on group
/// boxes instead of node boxes) so group-vs-group placement is comparable the same way
/// node-vs-node placement is. <see cref="CenterX"/>/<see cref="CenterY"/>/<see cref="Width"/>/
/// <see cref="Height"/> are raw geometry kept only for building Layer/OrderInLayer from.
/// </summary>
internal sealed record SkeletonGroup(
	string Id, string? ParentId, IReadOnlyList<string> MemberNodeIds, int Layer, int OrderInLayer,
	double CenterX, double CenterY, double Width, double Height);

/// <summary>
/// Renderer-agnostic snapshot of a diagram's layout structure: which layer each entity/node is
/// in, its order within that layer, which side each edge attaches to, the route signature of
/// each edge's polyline, port attachment order per (node, side) pair, and (for diagram types
/// that have them) subgraph/cluster containment and placement. Both Mermaider's own model
/// (<see cref="MermaiderSkeletonExtractor"/> for ER, <see cref="MermaiderGraphSkeletonExtractor"/>
/// for flowchart/state) and a real mermaid.js SVG (<see cref="MermaidJsSkeletonExtractor"/>,
/// <see cref="MermaidJsFlowchartSkeletonExtractor"/>, <see cref="MermaidJsStateSkeletonExtractor"/>)
/// project onto this same shape via <see cref="LayoutSkeletonBuilder"/>, so
/// <see cref="SkeletonComparer"/> can compare "did we make the same placement/ordering/
/// containment decisions" without caring about exact pixels or SVG syntax. <see cref="Groups"/>
/// is empty for diagram types without subgraphs (ER). <see cref="Ports"/> is empty when all
/// nodes have at most one edge per side (no attachment ordering to compare).
/// </summary>
internal sealed record LayoutSkeleton(
	IReadOnlyList<SkeletonNode> Nodes, IReadOnlyList<SkeletonEdge> Edges,
	IReadOnlyList<SkeletonGroup> Groups, IReadOnlyList<SkeletonPort> Ports)
{
	/// <summary>
	/// Human-readable layer-by-layer dump: entities per layer in left-to-right order, then
	/// every edge with its label, attachment sides and route signature, then ports. Meant to
	/// be eyeballed directly in a test failure or a Verify snapshot diff — no SVG viewer required.
	/// </summary>
	public string ToAsciiArt()
	{
		var sb = new StringBuilder();
		foreach (var layerGroup in Nodes.GroupBy(n => n.Layer).OrderBy(g => g.Key))
		{
			var ordered = layerGroup.OrderBy(n => n.OrderInLayer).Select(n => n.Id);
			sb.Append("Layer ").Append(layerGroup.Key).Append(": ").AppendLine(string.Join(", ", ordered));
		}

		if (Groups.Count > 0)
		{
			sb.AppendLine("Groups:");
			var byParent = Groups.ToLookup(g => g.ParentId, StringComparer.Ordinal);
			void WriteGroup(SkeletonGroup g, int depth)
			{
				sb.Append(new string(' ', depth * 2)).Append("- ").Append(g.Id)
					.Append(" (layer=").Append(g.Layer).Append(" order=").Append(g.OrderInLayer)
					.Append(") members=[").Append(string.Join(", ", g.MemberNodeIds)).AppendLine("]");
				foreach (var child in byParent[g.Id].OrderBy(c => c.Layer).ThenBy(c => c.OrderInLayer))
					WriteGroup(child, depth + 1);
			}
			foreach (var top in byParent[null].OrderBy(g => g.Layer).ThenBy(g => g.OrderInLayer))
				WriteGroup(top, 0);
		}

		if (Edges.Count > 0)
		{
			sb.AppendLine("Edges:");
			foreach (var e in Edges.OrderBy(e => e.From, StringComparer.Ordinal).ThenBy(e => e.To, StringComparer.Ordinal))
			{
				var loopMark = e.IsSelfLoop ? " (self)" : "";
				var routeMark = e.RouteSig.Length > 0 ? $" [{e.RouteSig}]" : "";
				sb.Append("  ").Append(e.From).Append(" --").Append(e.Label).Append("--> ").Append(e.To)
					.Append(" [").Append(e.FromSide).Append("->").Append(e.ToSide).Append(']').Append(loopMark).Append(routeMark)
					.AppendLine();
			}
		}

		if (Ports.Count > 0)
		{
			sb.AppendLine("Ports:");
			foreach (var p in Ports.OrderBy(p => p.NodeId, StringComparer.Ordinal).ThenBy(p => p.Side.ToString(), StringComparer.Ordinal))
				sb.Append("  ").Append(p.NodeId).Append(' ').Append(p.Side).Append(": ")
					.AppendLine(string.Join(",", p.EdgeIndices.Select(i => $"edge{i}")));
		}

		return sb.ToString();
	}

	// ── Stable text serialization ──────────────────────────────────────────────────────────────
	//
	// Committed `.mjs.ir.txt` files let the skeleton comparison tests run without any SVG or
	// local node/mmdc installation. The format is intentionally simple and line-per-record so
	// git diffs stay legible:
	//
	//   N\t{Id}\t{Layer}\t{OrderInLayer}
	//   E\t{From}\t{To}\t{LabelEscaped}\t{FromSide}\t{ToSide}\t{IsSelfLoop}\t{RouteSig|-}
	//   P\t{NodeId}\t{Side}\t{idx1,idx2,...}
	//   G\t{Id}\t{ParentId|-}\t{Layer}\t{OrderInLayer}\t{Member1,Member2,...}
	//
	// Label escaping: \\ → \\\\, \t → \\t, \r → \\r, \n → \\n. All other chars are literal.
	// RouteSig: "-" represents an empty string (self-loop or degenerate).
	// Records are written in a stable order so committed files produce clean diffs.

	/// <summary>Serializes this skeleton to a compact stable-text format suitable for committing.
	/// Round-trips through <see cref="Parse"/>.</summary>
	public string Serialize()
	{
		var sb = new StringBuilder();
		// Nodes: stable order by layer then order-in-layer then id
		foreach (var n in Nodes.OrderBy(n => n.Layer).ThenBy(n => n.OrderInLayer).ThenBy(n => n.Id, StringComparer.Ordinal))
			sb.Append('N').Append('\t').Append(n.Id).Append('\t').Append(n.Layer).Append('\t').Append(n.OrderInLayer).AppendLine();

		// Edges: stable order by From then To then label
		foreach (var e in Edges.OrderBy(e => e.From, StringComparer.Ordinal).ThenBy(e => e.To, StringComparer.Ordinal).ThenBy(e => e.Label, StringComparer.Ordinal))
		{
			var routeSig = e.RouteSig.Length > 0 ? e.RouteSig : "-";
			sb.Append('E').Append('\t').Append(e.From).Append('\t').Append(e.To).Append('\t')
			  .Append(EscapeLabel(e.Label)).Append('\t')
			  .Append(e.FromSide).Append('\t').Append(e.ToSide).Append('\t').Append(e.IsSelfLoop)
			  .Append('\t').Append(routeSig)
			  .AppendLine();
		}

		// Ports: stable order by NodeId then Side; only ports with ≥2 edges
		foreach (var p in Ports.OrderBy(p => p.NodeId, StringComparer.Ordinal).ThenBy(p => p.Side.ToString(), StringComparer.Ordinal))
		{
			if (p.EdgeIndices.Count < 2) continue;
			sb.Append('P').Append('\t').Append(p.NodeId).Append('\t').Append(p.Side).Append('\t')
			  .Append(string.Join(",", p.EdgeIndices))
			  .AppendLine();
		}

		// Groups: stable order by layer then order-in-layer then id
		foreach (var g in Groups.OrderBy(g => g.Layer).ThenBy(g => g.OrderInLayer).ThenBy(g => g.Id, StringComparer.Ordinal))
		{
			sb.Append('G').Append('\t').Append(g.Id).Append('\t').Append(g.ParentId ?? "-").Append('\t')
			  .Append(g.Layer).Append('\t').Append(g.OrderInLayer).Append('\t')
			  .Append(string.Join(",", g.MemberNodeIds.OrderBy(m => m, StringComparer.Ordinal)))
			  .AppendLine();
		}

		return sb.ToString();
	}

	/// <summary>Parses a skeleton previously produced by <see cref="Serialize"/>.</summary>
	/// <exception cref="FormatException">If the content is malformed.</exception>
	public static LayoutSkeleton Parse(string content)
	{
		var nodes = new List<SkeletonNode>();
		var edges = new List<SkeletonEdge>();
		var ports = new List<SkeletonPort>();
		var groups = new List<SkeletonGroup>();

		var lineNum = 0;
		foreach (var raw in content.Split('\n'))
		{
			lineNum++;
			var line = raw.TrimEnd('\r');
			if (line.Length == 0)
				continue;

			var parts = line.Split('\t');
			try
			{
				switch (parts[0])
				{
					case "N":
						Expect(parts, 4, "N");
						nodes.Add(new SkeletonNode(
							parts[1],
							int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
							int.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture),
							CenterX: 0, CenterY: 0, Width: 0, Height: 0)); // geometry not persisted
						break;

					case "E":
						Expect(parts, 8, "E");
						var routeSig = parts[7] == "-" ? "" : parts[7];
						edges.Add(new SkeletonEdge(
							parts[1], parts[2],
							UnescapeLabel(parts[3]),
							Enum.Parse<EdgeSide>(parts[4]),
							Enum.Parse<EdgeSide>(parts[5]),
							bool.Parse(parts[6]),
							routeSig));
						break;

					case "P":
						Expect(parts, 4, "P");
						var indices = parts[3].Length > 0
							? (IReadOnlyList<int>)parts[3].Split(',').Select(s => int.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToList()
							: [];
						ports.Add(new SkeletonPort(parts[1], Enum.Parse<EdgeSide>(parts[2]), indices));
						break;

					case "G":
						Expect(parts, 6, "G");
						var parentId = parts[2] == "-" ? null : parts[2];
						var members = parts[5].Length > 0
							? (IReadOnlyList<string>)parts[5].Split(',')
							: [];
						groups.Add(new SkeletonGroup(
							parts[1], parentId, members,
							int.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture),
							int.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture),
							CenterX: 0, CenterY: 0, Width: 0, Height: 0)); // geometry not persisted
						break;

					default:
						throw new FormatException($"Unknown record type '{parts[0]}'");
				}
			}
			catch (Exception ex) when (ex is not FormatException)
			{
				throw new FormatException($"Parse error at line {lineNum}: {line}", ex);
			}
		}

		return new LayoutSkeleton(nodes, edges, groups, ports);
	}

	private static void Expect(string[] parts, int count, string type)
	{
		if (parts.Length != count)
			throw new FormatException($"{type} record expects {count} tab-separated fields, got {parts.Length}");
	}

	private static string EscapeLabel(string s)
	{
		if (s.Length == 0)
			return s;
		var sb = new StringBuilder(s.Length + 4);
		foreach (var c in s)
		{
			switch (c)
			{
				case '\\': sb.Append(@"\\"); break;
				case '\t': sb.Append(@"\t"); break;
				case '\r': sb.Append(@"\r"); break;
				case '\n': sb.Append(@"\n"); break;
				default:   sb.Append(c);    break;
			}
		}
		return sb.ToString();
	}

	private static string UnescapeLabel(string s)
	{
		if (!s.Contains('\\'))
			return s;
		var sb = new StringBuilder(s.Length);
		var i = 0;
		while (i < s.Length)
		{
			if (s[i] == '\\' && i + 1 < s.Length)
			{
				sb.Append(s[i + 1] switch { '\\' => '\\', 't' => '\t', 'r' => '\r', 'n' => '\n', var c => c });
				i += 2;
			}
			else
			{
				sb.Append(s[i++]);
			}
		}
		return sb.ToString();
	}
}
