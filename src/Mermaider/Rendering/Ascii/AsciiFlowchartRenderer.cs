using Mermaider.Models;
using Sugiyama;

namespace Mermaider.Rendering.Ascii;

/// <summary>
/// A flowchart drawn in characters. The same Sugiyama layout the SVG renderer uses, run with sizes measured
/// in character cells rather than pixels, so what comes back is already a grid and nothing has to be divided
/// by a font metric to land on one.
/// </summary>
internal static class AsciiFlowchartRenderer
{
	/// <summary>A box is a border, a space and the label: two cells of padding each side.</summary>
	private const int Padding = 4;

	private const int BoxHeight = 3;

	internal static string Render(MermaidGraph graph, AsciiOptions options)
	{
		var order = graph.NodeOrder.Count > 0 ? graph.NodeOrder : [.. graph.Nodes.Keys];
		var widest = Math.Max(8, (options.Width / 3) - Padding);
		var labels = new Dictionary<string, string>(StringComparer.Ordinal);
		var nodes = new List<LayoutNode>(order.Count);
		foreach (var id in order)
		{
			if (!graph.Nodes.TryGetValue(id, out var node))
				continue;
			var label = AsciiCanvas.Fit(Flatten(node.Label ?? id), widest);
			labels[id] = label;
			nodes.Add(new LayoutNode(id, label.Length + Padding, BoxHeight));
		}

		if (nodes.Count == 0)
			return string.Empty;

		var edges = new List<LayoutEdge>(graph.Edges.Count);
		var original = new List<int>(graph.Edges.Count);
		for (var i = 0; i < graph.Edges.Count; i++)
		{
			var edge = graph.Edges[i];
			if (edge.Style == EdgeStyle.Invisible || !labels.ContainsKey(edge.Source) || !labels.ContainsKey(edge.Target))
				continue;
			original.Add(i);
			edges.Add(new LayoutEdge(edge.Source, edge.Target));
		}

		var direction = graph.Direction switch
		{
			Direction.LR => LayoutDirection.LR,
			Direction.RL => LayoutDirection.RL,
			Direction.BT => LayoutDirection.BT,
			_ => LayoutDirection.TD,
		};

		var across = direction is LayoutDirection.LR or LayoutDirection.RL;

		// no subgraphs go into the layout. Their rectangles are padded for a canvas measured in pixels, and on
		// a grid where a node is three cells tall that padding is most of the drawing; frames are instead put
		// tight around where the members landed, and only when nothing else landed inside one
		var layout = SugiyamaLayout.Compute(
			new LayoutGraph(direction, nodes, edges, []),
			new LayoutOptions
			{
				Padding = 2,
				NodeSpacing = across ? 1 : 4,
				LayerSpacing = across ? 10 : 4,
				SeparateComponents = true,
			});

		var placed = layout.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
		var boxes = layout.Nodes
			.Select(n => ((int)Math.Round(n.X), (int)Math.Round(n.Y), (int)Math.Round(n.Width), BoxHeight))
			.ToList();
		var canvas = new AsciiCanvas((int)Math.Ceiling(layout.Width) + 2, (int)Math.Ceiling(layout.Height) + 2, options.Ascii);
		foreach (var node in layout.Nodes)
		{
			var x = (int)Math.Round(node.X);
			var y = (int)Math.Round(node.Y);
			var width = (int)Math.Round(node.Width);
			canvas.Box(x, y, width, BoxHeight);
			canvas.Centred(x + 1, width - 2, y + 1, labels.GetValueOrDefault(node.Id, node.Id));
		}

		foreach (var routed in layout.Edges)
		{
			if (routed.OriginalIndex < 0 || routed.OriginalIndex >= original.Count)
				continue;
			var edge = graph.Edges[original[routed.OriginalIndex]];
			Draw(canvas, routed, edge, across, options, placed, boxes);
		}

		if (options.Groups)
		{
			foreach (var subgraph in graph.Subgraphs)
				Draw(canvas, subgraph, placed, options);
		}

		return canvas.ToString();
	}

	/// <summary>
	/// A frame around where a subgraph's members landed, drawn only when nothing that is not a member landed
	/// inside it. A frame that enclosed a stranger would be a lie about the diagram, and leaving it out costs
	/// only the grouping line.
	/// </summary>
	private static void Draw(AsciiCanvas canvas, MermaidSubgraph subgraph, Dictionary<string, LayoutNodeResult> placed, AsciiOptions options)
	{
		foreach (var child in subgraph.Children)
			Draw(canvas, child, placed, options);

		var members = subgraph.NodeIds.Where(placed.ContainsKey).Select(id => placed[id]).ToList();
		if (members.Count == 0)
			return;

		var left = (int)Math.Round(members.Min(m => m.X)) - 2;
		var right = (int)Math.Round(members.Max(m => m.X + m.Width)) + 1;
		var top = (int)Math.Round(members.Min(m => m.Y)) - 1;
		var bottom = (int)Math.Round(members.Max(m => m.Y + m.Height)) + 1;

		var strangers = placed.Values.Where(n => !subgraph.NodeIds.Contains(n.Id));
		foreach (var stranger in strangers)
		{
			if (stranger.X + stranger.Width > left && stranger.X < right && stranger.Y + stranger.Height > top && stranger.Y < bottom)
				return;
		}

		canvas.Frame(left, top, right - left + 1, bottom - top + 1, options.Ascii ? '-' : '┄', options.Ascii ? ':' : '┆');
		if (subgraph.Label is { Length: > 0 } label)
			canvas.Text(left + 2, top, $" {AsciiCanvas.Fit(Flatten(label), Math.Max(0, right - left - 5))} ");
	}

	/// <summary>
	/// One edge, as orthogonal runs. Each segment of the routed polyline becomes a dogleg — out, across,
	/// back on — which keeps the path the layout chose around other nodes while landing every character on
	/// the grid.
	/// </summary>
	private static void Draw(
		AsciiCanvas canvas,
		LayoutEdgeResult routed,
		MermaidEdge edge,
		bool across,
		AsciiOptions options,
		Dictionary<string, LayoutNodeResult> nodes,
		IReadOnlyList<(int X, int Y, int Width, int Height)> boxes)
	{
		if (routed.Points.Count < 2)
			return;

		var points = routed.Points.Select(p => (X: (int)Math.Round(p.X), Y: (int)Math.Round(p.Y))).ToList();

		// the ends are snapped to the border cell and the middle row of the boxes they join. A node is three
		// cells tall, so its centre falls on a half cell and rounding it lands on the border rather than on the
		// row the label sits on, which is what puts a line through a box instead of up to it
		if (nodes.TryGetValue(edge.Source, out var source))
			points[0] = Attach(source, points[0], across);
		if (nodes.TryGetValue(edge.Target, out var target))
			points[^1] = Attach(target, points[^1], across);

		// a routed polyline starts at the node's centre and can bend again before it is out of the box. Those
		// points are inside something once the ends have been snapped to its border, and drawing through them
		// is what puts a line across a label
		for (var i = points.Count - 2; i > 0; i--)
		{
			foreach (var box in boxes)
			{
				if (points[i].X >= box.X && points[i].X <= box.X + box.Width - 1 && points[i].Y >= box.Y && points[i].Y <= box.Y + box.Height - 1)
				{
					points.RemoveAt(i);
					break;
				}
			}
		}

		// a bend that doubles back is a curve the routing wanted and the grid cannot have: on characters it
		// reads as a second line going the wrong way, so an edge that is not monotone along the flow is drawn
		// as one dogleg from end to end instead
		var forward = across
			? points[^1].X >= points[0].X
			: points[^1].Y >= points[0].Y;
		for (var i = 1; i < points.Count; i++)
		{
			var back = across
				? forward ? points[i].X < points[i - 1].X : points[i].X > points[i - 1].X
				: forward ? points[i].Y < points[i - 1].Y : points[i].Y > points[i - 1].Y;
			if (!back)
				continue;
			points = [points[0], points[^1]];
			break;
		}
		for (var i = 1; i < points.Count; i++)
		{
			var (fromX, fromY) = points[i - 1];
			var (toX, toY) = points[i];
			// the turn is taken two cells after leaving rather than half way along, which keeps the crossing
			// run inside the gap between two layers instead of putting it through whatever box the midpoint
			// happens to land in. Where even that is blocked, the turn is nudged along until it is clear
			if (across)
			{
				var mid = Clear(Turn(fromX, toX), fromY, toY, boxes, column: true);
				canvas.Horizontal(fromX, mid, fromY);
				canvas.Vertical(mid, fromY, toY);
				canvas.Horizontal(mid, toX, toY);
			}
			else
			{
				var mid = Clear(Turn(fromY, toY), fromX, toX, boxes, column: false);
				canvas.Vertical(fromX, fromY, mid);
				canvas.Horizontal(fromX, toX, mid);
				canvas.Vertical(toX, mid, toY);
			}
		}

		Head(canvas, points, across, edge);
		if (options.EdgeLabels && edge.Label is { Length: > 0 } label && routed.LabelPosition is { } at)
		{
			// in the gap the edge leaves through, on its own row, so it reads as part of the arrow. The gap is
			// the one place on the line that is guaranteed to be clear of a box
			Label(canvas, points, across, Flatten(label), boxes);
		}
	}

	/// <summary>The column or row a dogleg turns at: just clear of where it left, and never past where it is going.</summary>
	private static int Turn(int from, int to) => from <= to ? Math.Min(from + 2, to) : Math.Max(from - 2, to);

	/// <summary>
	/// A turn that does not cross a box. The crossing run is the one part of a dogleg that travels against the
	/// grain of the layout, so it is the one that can end up inside something; this walks it outwards until it
	/// is clear, and gives up after a few tries rather than wandering off.
	/// </summary>
	private static int Clear(int at, int from, int to, IReadOnlyList<(int X, int Y, int Width, int Height)> boxes, bool column)
	{
		var (low, high) = from <= to ? (from, to) : (to, from);
		for (var nudge = 0; nudge <= 6; nudge++)
		{
			foreach (var candidate in nudge == 0 ? (ReadOnlySpan<int>)[at] : [at + nudge, at - nudge])
			{
				var blocked = false;
				foreach (var box in boxes)
				{
					var crosses = column
						? candidate > box.X && candidate < box.X + box.Width - 1 && high > box.Y && low < box.Y + box.Height - 1
						: candidate > box.Y && candidate < box.Y + box.Height - 1 && high > box.X && low < box.X + box.Width - 1;
					if (crosses)
					{
						blocked = true;
						break;
					}
				}

				if (!blocked)
					return candidate;
			}
		}

		return at;
	}

	/// <summary>
	/// An edge's label, written into the gap between the source and whatever is next along the line. Anything
	/// that does not fit in that gap is truncated rather than allowed to run over a box.
	/// </summary>
	private static void Label(AsciiCanvas canvas, List<(int X, int Y)> points, bool across, string label, IReadOnlyList<(int X, int Y, int Width, int Height)> boxes)
	{
		var (fromX, fromY) = points[0];
		var (toX, toY) = points[1];
		var (x, y, room) = across
			? (Math.Min(fromX, toX) + 2, fromY, Math.Abs(toX - fromX) - 3)
			: (fromX + 2, Turn(fromY, toY), 24);
		if (room < 3 || (!across && Math.Abs(toY - fromY) < 2))
			return;

		var text = AsciiCanvas.Fit(label, room);
		foreach (var box in boxes)
		{
			if (y >= box.Y && y < box.Y + box.Height && x + text.Length > box.X && x <= box.X + box.Width)
				return;
		}

		canvas.Text(x, y, text);
	}

	/// <summary>
	/// Where an edge meets a box: the border cell on whichever side the layout put the endpoint, on the box's
	/// middle row. The side needs no argument — the layout has already decided it, and all this does is move
	/// the point onto the grid without letting it land inside the box.
	/// </summary>
	private static (int X, int Y) Attach(LayoutNodeResult node, (int X, int Y) point, bool across)
	{
		var x = (int)Math.Round(node.X);
		var y = (int)Math.Round(node.Y);
		var width = (int)Math.Round(node.Width);
		return across
			? (point.X >= x + (width / 2) ? x + width - 1 : x, y + 1)
			: (x + (width / 2), point.Y >= y + 1 ? y + BoxHeight - 1 : y);
	}

	/// <summary>
	/// The arrowhead, one cell short of the border it points at, so it reads as arriving rather than
	/// replacing a piece of the box it arrives at.
	/// </summary>
	private static void Head(AsciiCanvas canvas, List<(int X, int Y)> points, bool across, MermaidEdge edge)
	{
		if (!edge.HasArrowEnd)
			return;

		var (x, y) = points[^1];
		var (previousX, previousY) = points[^2];
		if (across)
		{
			var forward = x >= previousX;
			canvas.Glyph(forward ? x - 1 : x + 1, y, canvas.Arrow(forward ? '>' : '<'));
			return;
		}

		var down = y >= previousY;
		canvas.Glyph(x, down ? y - 1 : y + 1, canvas.Arrow(down ? 'v' : '^'));
	}

	/// <summary>A label is one line here: a break in a box three rows tall has nowhere to go.</summary>
	private static string Flatten(string label) =>
		label.Replace("<br/>", " ", StringComparison.OrdinalIgnoreCase)
			.Replace("<br>", " ", StringComparison.OrdinalIgnoreCase)
			.Replace('\n', ' ')
			.Trim();
}
