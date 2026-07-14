using Mermaider.Models;

namespace Mermaider.Layout;

/// <summary>
/// Places architecture-diagram services, junctions, and groups on a directional grid inferred
/// from L/R/T/B edge sides, then converts grid cells to pixel coordinates and routes edges as
/// orthogonal (rectilinear) paths.
/// <para>
/// This is a from-scratch heuristic, not a port of Mermaid's cytoscape+fcose engine — it aims for
/// a correct, readable, non-overlapping layout rather than pixel-identical output.
/// </para>
/// </summary>
internal static class ArchitectureLayout
{
	private const double ServiceSize = 80;
	private const double JunctionSize = 12;
	// Gap between adjacent rows/cols must clear the worst case of two different groups landing in
	// directly adjacent rows: the upper group's bottom padding (GroupPadding) plus the lower
	// group's top padding+header (GroupPadding + GroupHeaderHeight) = 90px reach into the gap.
	private const double CellGap = 100;
	private const double CellW = ServiceSize + CellGap;
	private const double CellH = ServiceSize + CellGap;
	private const double GroupPadding = 28;
	private const double GroupHeaderHeight = 34;
	private const double Margin = 24;

	internal static PositionedArchitectureDiagram Layout(ArchitectureDiagram diagram)
	{
		var gridIds = new HashSet<string>(diagram.Services.Select(s => s.Id));
		foreach (var j in diagram.Junctions)
			_ = gridIds.Add(j.Id);

		var cells = AssignGridCells(gridIds, diagram.Edges);

		var minCol = cells.Count > 0 ? cells.Values.Min(c => c.Col) : 0;
		var minRow = cells.Count > 0 ? cells.Values.Min(c => c.Row) : 0;

		double PixelX(int col) => Margin + ((col - minCol) * CellW);
		double PixelY(int row) => Margin + ((row - minRow) * CellH);

		var services = diagram.Services.Select(s =>
		{
			var (col, row) = cells[s.Id];
			return new PositionedArchitectureService
			{
				Id = s.Id,
				Icon = s.Icon,
				Title = s.Title,
				X = PixelX(col),
				Y = PixelY(row),
				Width = ServiceSize,
				Height = ServiceSize,
			};
		}).ToList();

		var junctions = diagram.Junctions.Select(j =>
		{
			var (col, row) = cells[j.Id];
			return new PositionedArchitectureJunction
			{
				Id = j.Id,
				X = PixelX(col) + ((ServiceSize - JunctionSize) / 2),
				Y = PixelY(row) + ((ServiceSize - JunctionSize) / 2),
			};
		}).ToList();

		var boxes = BuildBoxLookup(services, junctions);
		var groups = BuildGroupBoxes(diagram, boxes);

		// Group boxes extend outward (padding + header) from their members' bbox, which can push
		// the top-left corner above/left of the page origin. Shift everything so the smallest
		// extent — across services, junctions, and groups — lands exactly at Margin.
		var minX = boxes.Count > 0 ? boxes.Values.Min(b => b.X) : 0;
		var minY = boxes.Count > 0 ? boxes.Values.Min(b => b.Y) : 0;
		if (groups.Count > 0)
		{
			minX = Math.Min(minX, groups.Min(g => g.X));
			minY = Math.Min(minY, groups.Min(g => g.Y));
		}
		var shiftX = Margin - minX;
		var shiftY = Margin - minY;

		services = services.Select(s => s with { X = s.X + shiftX, Y = s.Y + shiftY }).ToList();
		junctions = junctions.Select(j => j with { X = j.X + shiftX, Y = j.Y + shiftY }).ToList();
		groups = groups.Select(g => g with { X = g.X + shiftX, Y = g.Y + shiftY }).ToList();
		boxes = boxes.ToDictionary(kv => kv.Key, kv => kv.Value with { X = kv.Value.X + shiftX, Y = kv.Value.Y + shiftY });

		var edges = diagram.Edges
			.Where(e => boxes.ContainsKey(e.SourceId) && boxes.ContainsKey(e.TargetId))
			.Select(e => RouteEdge(e, boxes[e.SourceId], boxes[e.TargetId]))
			.ToList();

		var maxX = Margin;
		var maxY = Margin;
		foreach (var box in boxes.Values)
		{
			maxX = Math.Max(maxX, box.X + box.Width);
			maxY = Math.Max(maxY, box.Y + box.Height);
		}
		foreach (var g in groups)
		{
			maxX = Math.Max(maxX, g.X + g.Width);
			maxY = Math.Max(maxY, g.Y + g.Height);
		}

		return new PositionedArchitectureDiagram
		{
			Width = maxX + Margin,
			Height = maxY + Margin,
			Groups = groups,
			Services = services,
			Junctions = junctions,
			Edges = edges,
		};
	}

	// ========================================================================
	// Grid placement
	// ========================================================================

	private readonly record struct Cell(int Col, int Row);
	private readonly record struct Box(double X, double Y, double Width, double Height);

	// Disconnected components are packed 2-per-row (like flex-wrap), then next row down — reads
	// far better than either one very wide row or one column stacked arbitrarily tall. Mermaid's
	// own architecture-beta grammar has no notion of "direction" to defer to here; this is purely
	// our own layout heuristic.
	private const int ComponentGridCols = 2;

	/// <summary>
	/// Assigns each grid node an integer (col,row). Edges with a directional side propagate an
	/// offset via BFS. Each connected component is laid out independently in its own local
	/// coordinate space — collisions within a component are resolved by shifting to the nearest
	/// free row at the same column, but components never interfere with each other's placement.
	/// Components are then packed into a <see cref="ComponentGridCols"/>-wide grid (table-style:
	/// each column is as wide as its widest component, each row as tall as its tallest).
	/// </summary>
	private static Dictionary<string, Cell> AssignGridCells(HashSet<string> ids, IReadOnlyList<ArchitectureEdge> edges)
	{
		var adjacency = new Dictionary<string, List<(string Neighbor, int Dx, int Dy)>>();
		void AddEdge(string a, string b, int dx, int dy)
		{
			if (!ids.Contains(a) || !ids.Contains(b))
				return;
			(adjacency.TryGetValue(a, out var listA) ? listA : adjacency[a] = []).Add((b, dx, dy));
			(adjacency.TryGetValue(b, out var listB) ? listB : adjacency[b] = []).Add((a, -dx, -dy));
		}

		foreach (var edge in edges)
		{
			var (dx, dy) = OffsetForSide(edge.SourceSide, edge.TargetSide);
			AddEdge(edge.SourceId, edge.TargetId, dx, dy);
		}

		var visited = new HashSet<string>();
		var components = new List<Dictionary<string, Cell>>();

		foreach (var id in ids)
		{
			if (!visited.Add(id))
				continue;

			// BFS this component in its own private coordinate space so it can never collide
			// with — or be distorted by — any other component's cells.
			var local = new Dictionary<string, Cell>();
			var occupied = new HashSet<Cell>();

			void Place(string nodeId, Cell cell)
			{
				while (occupied.Contains(cell))
					cell = cell with { Row = cell.Row + 1 };
				local[nodeId] = cell;
				_ = occupied.Add(cell);
			}

			Place(id, new Cell(0, 0));
			var queue = new Queue<string>();
			queue.Enqueue(id);
			while (queue.Count > 0)
			{
				var current = queue.Dequeue();
				if (adjacency.TryGetValue(current, out var neighbors))
				{
					var origin = local[current];
					foreach (var (neighbor, dx, dy) in neighbors)
					{
						if (local.ContainsKey(neighbor))
							continue;
						Place(neighbor, new Cell(origin.Col + dx, origin.Row + dy));
						_ = visited.Add(neighbor);
						queue.Enqueue(neighbor);
					}
				}
			}

			// Normalize this component to start at (0,0) in its own local space.
			var minCol = local.Values.Min(c => c.Col);
			var minRow = local.Values.Min(c => c.Row);
			components.Add(local.ToDictionary(kv => kv.Key, kv => new Cell(kv.Value.Col - minCol, kv.Value.Row - minRow)));
		}

		return PackComponentGrid(components);
	}

	private static Dictionary<string, Cell> PackComponentGrid(List<Dictionary<string, Cell>> components)
	{
		var cells = new Dictionary<string, Cell>();
		if (components.Count == 0)
			return cells;

		var gridCols = Math.Min(ComponentGridCols, components.Count);
		var gridRows = (components.Count + gridCols - 1) / gridCols;

		var spans = components.Select(c => (
			ColSpan: c.Values.Max(v => v.Col) + 1,
			RowSpan: c.Values.Max(v => v.Row) + 1)).ToList();

		var colWidths = new int[gridCols];
		var rowHeights = new int[gridRows];
		for (var i = 0; i < components.Count; i++)
		{
			var (row, col) = (i / gridCols, i % gridCols);
			colWidths[col] = Math.Max(colWidths[col], spans[i].ColSpan);
			rowHeights[row] = Math.Max(rowHeights[row], spans[i].RowSpan);
		}

		var colStart = new int[gridCols];
		for (var c = 1; c < gridCols; c++)
			colStart[c] = colStart[c - 1] + colWidths[c - 1];

		var rowStart = new int[gridRows];
		for (var r = 1; r < gridRows; r++)
			rowStart[r] = rowStart[r - 1] + rowHeights[r - 1];

		for (var i = 0; i < components.Count; i++)
		{
			var (row, col) = (i / gridCols, i % gridCols);
			foreach (var (nodeId, cell) in components[i])
				cells[nodeId] = new Cell(cell.Col + colStart[col], cell.Row + rowStart[row]);
		}

		return cells;
	}

	private static (int Dx, int Dy) OffsetForSide(ArchitectureSide? sourceSide, ArchitectureSide? targetSide)
	{
		if (sourceSide is { } s)
			return SideOffset(s);
		if (targetSide is { } t)
		{
			var (dx, dy) = SideOffset(t);
			return (-dx, -dy);
		}
		return (1, 0);
	}

	private static (int Dx, int Dy) SideOffset(ArchitectureSide side) => side switch
	{
		ArchitectureSide.Left => (-1, 0),
		ArchitectureSide.Right => (1, 0),
		ArchitectureSide.Top => (0, -1),
		ArchitectureSide.Bottom => (0, 1),
		_ => (1, 0),
	};

	// ========================================================================
	// Group bounding boxes
	// ========================================================================

	private static Dictionary<string, Box> BuildBoxLookup(
		IReadOnlyList<PositionedArchitectureService> services,
		IReadOnlyList<PositionedArchitectureJunction> junctions)
	{
		var boxes = new Dictionary<string, Box>();
		foreach (var s in services)
			boxes[s.Id] = new Box(s.X, s.Y, s.Width, s.Height);
		foreach (var j in junctions)
			boxes[j.Id] = new Box(j.X, j.Y, JunctionSize, JunctionSize);
		return boxes;
	}

	private static List<PositionedArchitectureGroup> BuildGroupBoxes(ArchitectureDiagram diagram, Dictionary<string, Box> memberBoxes)
	{
		var childGroupsOf = diagram.Groups
			.Where(g => g.ParentId is not null)
			.GroupBy(g => g.ParentId!)
			.ToDictionary(g => g.Key, g => g.ToList());
		var directMemberIdsOf = diagram.Services
			.Where(s => s.GroupId is not null)
			.Select(s => (s.GroupId!, s.Id))
			.Concat(diagram.Junctions.Where(j => j.GroupId is not null).Select(j => (j.GroupId!, j.Id)))
			.GroupBy(x => x.Item1)
			.ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());

		var computed = new Dictionary<string, Box>();

		Box ComputeBox(ArchitectureGroup group, HashSet<string> visiting)
		{
			if (computed.TryGetValue(group.Id, out var cached))
				return cached;
			if (!visiting.Add(group.Id))
				return new Box(0, 0, 0, 0); // cycle guard — shouldn't happen with valid input

			var memberBoxList = new List<Box>();

			if (directMemberIdsOf.TryGetValue(group.Id, out var directIds))
				foreach (var id in directIds)
					if (memberBoxes.TryGetValue(id, out var box))
						memberBoxList.Add(box);

			if (childGroupsOf.TryGetValue(group.Id, out var childGroups))
				foreach (var child in childGroups)
					memberBoxList.Add(ComputeBox(child, visiting));

			if (memberBoxList.Count == 0)
			{
				var empty = new Box(0, 0, 0, 0);
				computed[group.Id] = empty;
				return empty;
			}

			var minX = memberBoxList.Min(b => b.X);
			var minY = memberBoxList.Min(b => b.Y);
			var maxX = memberBoxList.Max(b => b.X + b.Width);
			var maxY = memberBoxList.Max(b => b.Y + b.Height);

			var result = new Box(
				minX - GroupPadding,
				minY - GroupPadding - GroupHeaderHeight,
				maxX - minX + (2 * GroupPadding),
				maxY - minY + (2 * GroupPadding) + GroupHeaderHeight);

			computed[group.Id] = result;
			return result;
		}

		var result = new List<PositionedArchitectureGroup>(diagram.Groups.Count);
		foreach (var group in diagram.Groups)
		{
			var box = ComputeBox(group, []);
			result.Add(new PositionedArchitectureGroup
			{
				Id = group.Id,
				Icon = group.Icon,
				Title = group.Title,
				X = box.X,
				Y = box.Y,
				Width = box.Width,
				Height = box.Height,
			});
		}

		return result;
	}

	private static PositionedArchitectureEdge RouteEdge(ArchitectureEdge edge, Box source, Box target)
	{
		var points = new List<Point> { AnchorPoint(source, edge.SourceSide) };
		var sourceAnchor = points[0];
		var targetAnchor = AnchorPoint(target, edge.TargetSide);

		var sourceHorizontal = edge.SourceSide is ArchitectureSide.Left or ArchitectureSide.Right or null;
		if (Math.Abs(sourceAnchor.X - targetAnchor.X) > 0.5 && Math.Abs(sourceAnchor.Y - targetAnchor.Y) > 0.5)
		{
			var bend = sourceHorizontal
				? new Point(targetAnchor.X, sourceAnchor.Y)
				: new Point(sourceAnchor.X, targetAnchor.Y);
			points.Add(bend);
		}

		points.Add(targetAnchor);

		return new PositionedArchitectureEdge
		{
			SourceId = edge.SourceId,
			TargetId = edge.TargetId,
			SourceArrow = edge.SourceArrow,
			TargetArrow = edge.TargetArrow,
			Points = points,
		};
	}

	private static Point AnchorPoint(Box box, ArchitectureSide? side) => side switch
	{
		ArchitectureSide.Left => new Point(box.X, box.Y + (box.Height / 2)),
		ArchitectureSide.Right => new Point(box.X + box.Width, box.Y + (box.Height / 2)),
		ArchitectureSide.Top => new Point(box.X + (box.Width / 2), box.Y),
		ArchitectureSide.Bottom => new Point(box.X + (box.Width / 2), box.Y + box.Height),
		_ => new Point(box.X + (box.Width / 2), box.Y + (box.Height / 2)),
	};
}
