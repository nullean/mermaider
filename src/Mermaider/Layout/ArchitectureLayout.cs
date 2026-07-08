using Mermaider.Models;
using Mermaider.Text;

namespace Mermaider.Layout;

/// <summary>
/// Purpose-built grid/port layout for architecture diagrams (Parse → Layout → Render).
/// </summary>
internal static class ArchitectureLayout
{
	internal const double IconTile = 56;

	private const double Margin = 40;
	private const double GroupPad = 28;
	private const double GroupHeaderH = 36;
	private const double ServiceW = 100;
	private const double ServiceH = 92; // icon tile + label
	private const double CellGapX = 48;
	private const double CellGapY = 40;
	private const double GroupGap = 48;
	private const double EmptyGroupMinW = 140;
	private const double EmptyGroupMinH = 100;

	internal sealed class Result
	{
		public double Width { get; init; }
		public double Height { get; init; }
		public required IReadOnlyList<PlacedGroup> Groups { get; init; }
		public required IReadOnlyList<PlacedService> Services { get; init; }
		public required IReadOnlyList<ArchitectureEdge> Edges { get; init; }
		public required IReadOnlyDictionary<string, (double X, double Y, double W, double H)> Bounds { get; init; }
	}

	internal sealed record PlacedGroup(string Id, string Icon, string Label, double X, double Y, double W, double H);
	internal sealed record PlacedService(string Id, string Icon, string Label, double X, double Y, double W, double H);

	internal static Result Layout(ArchitectureDiagram diagram)
	{
		var placedGroups = new List<PlacedGroup>();
		var placedServices = new List<PlacedService>();
		var bounds = new Dictionary<string, (double X, double Y, double W, double H)>(StringComparer.Ordinal);
		var placedGroupIds = new HashSet<string>(StringComparer.Ordinal);
		var placedServiceIds = new HashSet<string>(StringComparer.Ordinal);

		var servicesByParent = diagram.Services
			.GroupBy(s => s.ParentId ?? "", StringComparer.Ordinal)
			.ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
		var childGroupsByParent = diagram.Groups
			.Where(g => g.ParentId is not null)
			.GroupBy(g => g.ParentId!, StringComparer.Ordinal)
			.ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

		var topLevelGroups = diagram.Groups.Where(g => g.ParentId is null).ToList();
		var ungrouped = servicesByParent.GetValueOrDefault("", []);

		var cursorX = Margin;
		var maxBottom = Margin;

		foreach (var group in topLevelGroups)
		{
			var (w, h) = PlaceGroupTree(
				group, cursorX, Margin, diagram.Edges,
				servicesByParent, childGroupsByParent,
				placedGroups, placedServices, bounds,
				placedGroupIds, placedServiceIds);
			cursorX += w + GroupGap;
			maxBottom = Math.Max(maxBottom, Margin + h);
		}

		if (ungrouped.Count > 0)
		{
			var originX = topLevelGroups.Count > 0 ? cursorX : Margin;
			var (uw, uh) = PlaceServiceCluster(
				ungrouped, diagram.Edges, originX, Margin, placedServices, bounds, placedServiceIds);
			cursorX = Math.Max(cursorX, originX + uw + GroupGap);
			maxBottom = Math.Max(maxBottom, Margin + uh);
		}

		// Orphan groups: missing parent, or only reachable via a cycle with no true root.
		// Place at top level so they still render instead of vanishing.
		foreach (var group in diagram.Groups)
		{
			if (placedGroupIds.Contains(group.Id))
				continue;

			var (w, h) = PlaceGroupTree(
				group, cursorX, Margin, diagram.Edges,
				servicesByParent, childGroupsByParent,
				placedGroups, placedServices, bounds,
				placedGroupIds, placedServiceIds);
			cursorX += w + GroupGap;
			maxBottom = Math.Max(maxBottom, Margin + h);
		}

		// Orphan services: parent group id never defined / never placed.
		var remainingServices = diagram.Services
			.Where(s => !placedServiceIds.Contains(s.Id))
			.ToList();
		if (remainingServices.Count > 0)
		{
			var originX = placedGroups.Count > 0 || placedServices.Count > 0 ? cursorX : Margin;
			var (uw, uh) = PlaceServiceCluster(
				remainingServices, diagram.Edges, originX, Margin, placedServices, bounds, placedServiceIds);
			cursorX = Math.Max(cursorX, originX + uw + GroupGap);
			maxBottom = Math.Max(maxBottom, Margin + uh);
		}

		if (placedGroups.Count == 0 && placedServices.Count == 0)
		{
			return new Result
			{
				Width = 200,
				Height = 100,
				Groups = placedGroups,
				Services = placedServices,
				Edges = diagram.Edges,
				Bounds = bounds,
			};
		}

		var width = cursorX - GroupGap + Margin;
		if (width < Margin + 120)
			width = Margin + 120;
		var height = maxBottom + Margin;

		return new Result
		{
			Width = width,
			Height = Math.Max(height, 80),
			Groups = placedGroups,
			Services = placedServices,
			Edges = diagram.Edges,
			Bounds = bounds,
		};
	}

	private static (double W, double H) PlaceGroupTree(
		ArchitectureGroup group,
		double x,
		double y,
		IReadOnlyList<ArchitectureEdge> edges,
		Dictionary<string, List<ArchitectureService>> servicesByParent,
		Dictionary<string, List<ArchitectureGroup>> childGroupsByParent,
		List<PlacedGroup> placedGroups,
		List<PlacedService> placedServices,
		Dictionary<string, (double X, double Y, double W, double H)> bounds,
		HashSet<string> placedGroupIds,
		HashSet<string> placedServiceIds)
	{
		// Cycle / duplicate-id guard: never re-enter a group id already being placed.
		if (!placedGroupIds.Add(group.Id))
			return (0, 0);

		var services = servicesByParent.GetValueOrDefault(group.Id, []);
		var childGroups = childGroupsByParent.GetValueOrDefault(group.Id, []);

		var contentX = x + GroupPad;
		var contentY = y + GroupHeaderH + GroupPad;
		var contentRight = contentX;
		var contentBottom = contentY;

		if (services.Count > 0)
		{
			var (cw, ch) = PlaceServiceCluster(
				services, edges, contentX, contentY, placedServices, bounds, placedServiceIds);
			contentRight = contentX + cw;
			contentBottom = contentY + ch;
		}

		var nestedX = services.Count > 0 ? contentRight + GroupGap : contentX;
		var nestedY = contentY;
		foreach (var child in childGroups)
		{
			var (cw, ch) = PlaceGroupTree(
				child, nestedX, nestedY, edges, servicesByParent, childGroupsByParent,
				placedGroups, placedServices, bounds, placedGroupIds, placedServiceIds);
			if (cw <= 0 && ch <= 0)
				continue;
			contentRight = Math.Max(contentRight, nestedX + cw);
			contentBottom = Math.Max(contentBottom, nestedY + ch);
			nestedX += cw + GroupGap;
		}

		double innerW;
		double innerH;
		if (services.Count == 0 && childGroups.Count == 0)
		{
			innerW = EmptyGroupMinW;
			innerH = EmptyGroupMinH;
		}
		else
		{
			innerW = Math.Max(contentRight - contentX, EmptyGroupMinW);
			innerH = Math.Max(contentBottom - contentY, ServiceH);
		}

		var labelW = TextMetrics.MeasureTextWidth(group.Label, 14, 600) + 48;
		var w = Math.Max(innerW + (GroupPad * 2), labelW);
		var h = GroupHeaderH + GroupPad + innerH + GroupPad;

		placedGroups.Add(new PlacedGroup(group.Id, group.Icon, group.Label, x, y, w, h));
		// First-wins: never overwrite an existing id (group/service collision defense).
		_ = bounds.TryAdd(group.Id, (x, y, w, h));
		return (w, h);
	}

	/// <summary>
	/// Place services on a grid derived from edge port directions
	/// (R→L = left of, T→B = below, etc.) so layout matches mermaid spatial intent.
	/// </summary>
	private static (double W, double H) PlaceServiceCluster(
		List<ArchitectureService> services,
		IReadOnlyList<ArchitectureEdge> allEdges,
		double originX,
		double originY,
		List<PlacedService> placedServices,
		Dictionary<string, (double X, double Y, double W, double H)> bounds,
		HashSet<string> placedServiceIds)
	{
		// Skip already-placed ids (duplicate service declarations).
		var toPlace = services.Where(s => placedServiceIds.Add(s.Id)).ToList();
		if (toPlace.Count == 0)
			return (0, 0);

		var ids = toPlace.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
		var col = new Dictionary<string, int>(StringComparer.Ordinal);
		var row = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (var s in toPlace)
		{
			col[s.Id] = 0;
			row[s.Id] = 0;
		}

		// Local edges only (both ends in this cluster)
		var edges = allEdges
			.Where(e => ids.Contains(e.SourceId) && ids.Contains(e.TargetId))
			.ToList();

		// Relax port-axis inequalities first (stable; no free-axis thrash).
		var iters = Math.Max(4, toPlace.Count * 2);
		for (var iter = 0; iter < iters; iter++)
		{
			foreach (var e in edges)
				ApplyPortAxisInequality(e, col, row);
		}

		// Free-axis preference (same row for horizontal ports, same col for vertical)
		// runs once after inequalities converge so multi-edge clusters don't thrash.
		foreach (var e in edges)
			ApplyPortFreeAxisPreference(e, col, row);

		// Normalize so min col/row is 0
		var minC = col.Values.Min();
		var minR = row.Values.Min();
		foreach (var s in toPlace)
		{
			col[s.Id] -= minC;
			row[s.Id] -= minR;
		}

		// Resolve collisions: if two share a cell, nudge later one down/right
		var occupied = new Dictionary<(int C, int R), string>();
		foreach (var s in toPlace.OrderBy(s => s.Id, StringComparer.Ordinal))
		{
			var c = col[s.Id];
			var r = row[s.Id];
			while (occupied.ContainsKey((c, r)))
			{
				// Prefer push down, then right
				r++;
				if (r > toPlace.Count)
				{
					r = 0;
					c++;
				}
			}
			col[s.Id] = c;
			row[s.Id] = r;
			occupied[(c, r)] = s.Id;
		}

		var maxC = col.Values.Max();
		var maxR = row.Values.Max();
		var cellW = ServiceW + CellGapX;
		var cellH = ServiceH + CellGapY;

		foreach (var s in toPlace)
		{
			var x = originX + (col[s.Id] * cellW);
			var y = originY + (row[s.Id] * cellH);
			// Service bounds for edges: the icon tile (not the full label area)
			var tileX = x + ((ServiceW - IconTile) / 2);
			var tileY = y;
			placedServices.Add(new PlacedService(s.Id, s.Icon, s.Label, x, y, ServiceW, ServiceH));
			// Edge attachment uses icon tile so arrows hit the blue squares.
			// First-wins if a group already claimed this id (should be rare after parse dedupe).
			_ = bounds.TryAdd(s.Id, (tileX, tileY, IconTile, IconTile));
		}

		var w = ((maxC + 1) * cellW) - CellGapX;
		var h = ((maxR + 1) * cellH) - CellGapY;
		return (Math.Max(w, 0), Math.Max(h, 0));
	}

	private static void ApplyPortAxisInequality(
		ArchitectureEdge e,
		Dictionary<string, int> col,
		Dictionary<string, int> row)
	{
		// Primary axis only — source port side implies relative placement.
		// db:R --> L:server  => db left of server
		// disk:T --> B:server => disk below server
		switch (e.SourcePort)
		{
			case ArchitecturePort.Right:
				if (col[e.SourceId] >= col[e.TargetId])
					col[e.TargetId] = col[e.SourceId] + 1;
				break;
			case ArchitecturePort.Left:
				if (col[e.TargetId] >= col[e.SourceId])
					col[e.SourceId] = col[e.TargetId] + 1;
				break;
			case ArchitecturePort.Bottom:
				if (row[e.SourceId] >= row[e.TargetId])
					row[e.TargetId] = row[e.SourceId] + 1;
				break;
			case ArchitecturePort.Top:
				if (row[e.TargetId] >= row[e.SourceId])
					row[e.SourceId] = row[e.TargetId] + 1;
				break;
		}
	}

	private static void ApplyPortFreeAxisPreference(
		ArchitectureEdge e,
		Dictionary<string, int> col,
		Dictionary<string, int> row)
	{
		// Soft same-row/col preference for matched port pairs — applied once after
		// inequalities so multi-edge clusters don't thrash every iteration.
		switch (e.SourcePort)
		{
			case ArchitecturePort.Right when e.TargetPort == ArchitecturePort.Left:
				row[e.TargetId] = row[e.SourceId];
				break;
			case ArchitecturePort.Left when e.TargetPort == ArchitecturePort.Right:
				row[e.SourceId] = row[e.TargetId];
				break;
			case ArchitecturePort.Bottom when e.TargetPort == ArchitecturePort.Top:
				col[e.TargetId] = col[e.SourceId];
				break;
			case ArchitecturePort.Top when e.TargetPort == ArchitecturePort.Bottom:
				col[e.SourceId] = col[e.TargetId];
				break;
		}
	}
}
