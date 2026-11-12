using Mermaider.Models;
using Mermaider.Rendering;
using Mermaider.Text;
using Sugiyama;

namespace Mermaider.Layout;

/// <summary>
/// Class self-relations (<c>A --&gt; A</c>) are kept out of Sugiyama, whose fixed 28px self-loop has no room for a class's
/// two cardinalities and its label. Like ER self-loops (<see cref="LightweightErLayoutEngine"/>), each is a rectangular loop
/// out of a side the layout's edges don't use (right or left in TD/BT, below or above in LR/RL), whichever has room: two
/// straight stubs carry the end marker and the cardinalities (which <see cref="ClassSvgRenderer"/> centres
/// <see cref="ClassSvgRenderer.CardinalityDistance"/> along them), and the label sits on the outer side of the loop, or just
/// past it when the class is too short for that side to show around the label. The loop is sized so its three pills don't
/// overlap, and when a neighbour stands too close a column (row, in LR/RL) of space is opened beside the class.
/// </summary>
/// <remarks>
/// The geometry is written for TD, where loops go right or left; LR/RL layouts are transposed in and out of that frame.
/// </remarks>
internal static class ClassSelfLoops
{
	/// <summary>Clearance between two pills, and between a stub and the box corner.</summary>
	private const double Gap = 4;

	/// <summary>Shortest stub, so a loop without cardinalities still reads as a loop with its marker.</summary>
	private const double MinStub = 36;

	/// <summary>Straight outer side (past the rounded corners) that must show on both ends of a label centred on it.</summary>
	private const double VisibleLine = 16;

	/// <summary>Distance between the outer sides of further loops on the same side of one class.</summary>
	private const double NestStep = 24;

	/// <summary>A placed self-loop: its path, label centre and bounds (pills included).</summary>
	internal sealed record Loop(string ClassId, List<Point> Points, Point? LabelPosition, double MinX, double MinY, double MaxX, double MaxY);

	private sealed class Placement(string classId, List<Point> points, Point? label, double minX, double minY, double maxX, double maxY)
	{
		public string ClassId { get; } = classId;
		public List<Point> Points { get; } = points;
		public Point? Label { get; set; } = label;
		public double MinX { get; set; } = minX;
		public double MaxX { get; set; } = maxX;
		public double MinY { get; } = minY;
		public double MaxY { get; } = maxY;
	}

	/// <summary>
	/// Places every self-relation of <paramref name="diagram"/> around its class in <paramref name="result"/>, keyed by
	/// relationship index. Opening room for a loop moves nodes and edges, so <paramref name="result"/> is replaced.
	/// <paramref name="namespaceMargin"/> is the room a namespace's border and header take around a member's loop.
	/// </summary>
	internal static Dictionary<int, Loop> Place(ref LayoutResult result, ClassDiagram diagram, double padding, double nodeSpacing, double namespaceMargin)
	{
		if (!diagram.Relationships.Any(r => r.From == r.To))
			return [];

		// work in the TD frame: x across the layers, y along them
		var t = diagram.Direction is Direction.LR or Direction.RL;
		var nodes = result.Nodes.Select(n => t ? new LayoutNodeResult(n.Id, n.Y, n.X, n.Height, n.Width) : n).ToList();
		var edges = result.Edges.Select(e => t ? Transpose(e) : e).ToList();
		var width = t ? result.Height : result.Width;
		var loops = new Dictionary<int, Placement>();
		var loopsPerSide = new Dictionary<(string Id, bool Left), int>();

		for (var i = 0; i < diagram.Relationships.Count; i++)
		{
			var rel = diagram.Relationships[i];
			if (rel.From != rel.To)
				continue;
			var n = nodes.FirstOrDefault(x => x.Id == rel.From);
			if (n is null)
				continue;

			// pill sizes as seen in the frame: W across the loop's stubs, H along its outer side
			var from = PillSize(rel.FromCardinality, t);
			var to = PillSize(rel.ToCardinality, t);
			var label = PillSize(rel.Label, t);

			// the stubs sit far enough apart that the two cardinality pills centred on them don't touch, within the box side
			var centreY = n.Y + (n.Height / 2);
			var maxSpan = Math.Max(1, (n.Height / 2) - Gap);
			var wanted = Math.Max(Math.Clamp(n.Height * 0.3, 10, 40), (from.H + to.H + (2 * Gap)) / 4);
			var halfSpan = Math.Min(wanted, maxSpan);
			var exitY = centreY - halfSpan;
			var entryY = centreY + halfSpan;

			// The label sits on the outer side when enough of that side shows around it; on a short class it would hide the
			// whole side, so it sits just past it instead.
			var onLine = 2 * halfSpan >= label.H + (2 * VisibleLine);
			// the outer side clears the cardinalities, and so does a label centred on it
			var cardinalityReach = Math.Max(from.W, to.W) > 0 ? ClassSvgRenderer.CardinalityDistance + (Math.Max(from.W, to.W) / 2) + Gap : 0;
			var stub = Math.Max(MinStub, cardinalityReach) + (onLine ? label.W / 2 : 0);
			var labelOut = label.W == 0 ? 0 : onLine ? label.W / 2 : label.W + Gap;

			var bandTop = Math.Min(Math.Min(n.Y, exitY - (from.H / 2)), centreY - (label.H / 2));
			var bandBottom = Math.Max(Math.Max(n.Y + n.Height, entryY + (to.H / 2)), centreY + (label.H / 2));
			var (freeLeft, freeRight) = FreeRoom(nodes, loops.Values, n, bandTop, bandBottom);

			var usedLeft = loopsPerSide.GetValueOrDefault((n.Id, true));
			var usedRight = loopsPerSide.GetValueOrDefault((n.Id, false));
			var reachRight = stub + (usedRight * NestStep) + labelOut + nodeSpacing;
			var reachLeft = stub + (usedLeft * NestStep) + labelOut + nodeSpacing;
			// further loops of one class alternate sides; otherwise right (mermaid.js's side) unless only the left has room
			var left = usedLeft != usedRight ? usedLeft < usedRight : freeRight < reachRight && (freeLeft >= reachLeft || freeLeft > freeRight);
			var nth = left ? usedLeft : usedRight;
			loopsPerSide[(n.Id, left)] = nth + 1;
			var reach = left ? reachLeft : reachRight;
			var free = left ? freeLeft : freeRight;

			if (free < reach && FindCut(nodes, left ? n.X - free : n.X + n.Width, left ? n.X : n.X + n.Width + free, left) is { } cut)
			{
				// open a column of space beside the class: everything past it moves right, lines crossing it stretch
				InsertColumn(nodes, edges, loops.Values, cut.At, reach - free, cut.IncludeAt);
				width += reach - free;
				n = nodes.First(x => x.Id == rel.From);
			}

			var dir = left ? -1 : 1;
			var sideX = left ? n.X : n.X + n.Width;
			var outerX = sideX + (dir * (stub + (nth * NestStep)));
			var outerReach = outerX + (dir * Math.Max(labelOut, 1));
			Point? labelPos = label.W > 0 ? new Point(onLine ? outerX : outerX + (dir * (Gap + (label.W / 2))), centreY) : null;
			loops[i] = new Placement(
				n.Id,
				[new Point(sideX, exitY), new Point(outerX, exitY), new Point(outerX, entryY), new Point(sideX, entryY)],
				labelPos,
				Math.Min(sideX, outerReach),
				bandTop,
				Math.Max(sideX, outerReach),
				bandBottom
			);
		}

		if (loops.Count == 0)
			return [];

		// keep the canvas padding around loops that reach past the outermost class, and around a namespace grown by one
		var namespaced = diagram.Namespaces.SelectMany(ns => ns.ClassIds).ToHashSet();
		var minX = Math.Min(nodes.Min(x => x.X), loops.Values.Min(l => l.MinX - (namespaced.Contains(l.ClassId) ? namespaceMargin : 0)));
		var maxX = Math.Max(nodes.Max(x => x.X + x.Width), loops.Values.Max(l => l.MaxX));
		var shift = minX < padding ? padding - minX : 0;
		if (shift > 0)
			InsertColumn(nodes, edges, loops.Values, double.MinValue, shift, includeAt: true);
		width = Math.Max(width + shift, maxX + shift + padding);

		result = t
			? result with
			{
				Height = width,
				Nodes = nodes.Select(n => new LayoutNodeResult(n.Id, n.Y, n.X, n.Height, n.Width)).ToList(),
				Edges = edges.Select(Transpose).ToList(),
			}
			: result with { Width = width, Nodes = nodes, Edges = edges };
		return loops.ToDictionary(kv => kv.Key, kv =>
		{
			var l = kv.Value;
			return t
				? new Loop(
					l.ClassId,
					l.Points.Select(p => new Point(p.Y, p.X)).ToList(),
					l.Label is { } lp ? new Point(lp.Y, lp.X) : null,
					l.MinY,
					l.MinX,
					l.MaxY,
					l.MaxX
				)
				: new Loop(l.ClassId, l.Points, l.Label, l.MinX, l.MinY, l.MaxX, l.MaxY);
		});
	}

	private static LayoutEdgeResult Transpose(LayoutEdgeResult e) =>
		e with
		{
			Points = e.Points.Select(p => new LayoutPoint(p.Y, p.X)).ToList(),
			LabelPosition = e.LabelPosition is { } lp ? new LayoutPoint(lp.Y, lp.X) : null,
		};

	private static (double W, double H) PillSize(string? text, bool transposed)
	{
		if (text is not { Length: > 0 })
			return (0, 0);
		// the edge-label pill DesignSystem.AppendEdgeLabel draws; the halo and chip label kinds are no larger
		var m = TextMetrics.MeasureMultiline(text.AsSpan(), DesignSystem.Px(TypeRole.Caption), 500);
		var (w, h) = (DesignSystem.LabelBoxWidth(m.Width), Math.Max(DesignSystem.PillHeight, m.Height + 6));
		return transposed ? (h, w) : (w, h);
	}

	/// <summary>
	/// Free room left and right of <paramref name="n"/> to the nearest node or other class's loop overlapping the band
	/// [top, bottom].
	/// </summary>
	private static (double Left, double Right) FreeRoom(
		List<LayoutNodeResult> nodes,
		IEnumerable<Placement> loops,
		LayoutNodeResult n,
		double top,
		double bottom
	)
	{
		var freeLeft = double.MaxValue;
		var freeRight = double.MaxValue;
		var obstacles = nodes
			.Where(o => o.Id != n.Id)
			.Select(o => (o.X, o.Y, Right: o.X + o.Width, Bottom: o.Y + o.Height))
			.Concat(loops.Where(l => l.ClassId != n.Id).Select(l => (X: l.MinX, Y: l.MinY, Right: l.MaxX, Bottom: l.MaxY)));
		foreach (var o in obstacles)
		{
			if (o.Bottom <= top || o.Y >= bottom)
				continue;
			if (o.Right <= n.X)
				freeLeft = Math.Min(freeLeft, n.X - o.Right);
			else if (o.X >= n.X + n.Width)
				freeRight = Math.Min(freeRight, o.X - (n.X + n.Width));
		}
		return (freeLeft, freeRight);
	}

	/// <summary>
	/// An x in [<paramref name="lo"/>, <paramref name="hi"/>] that cuts through no node in any layer, nearest the class
	/// (<paramref name="hi"/> for a loop on the left, <paramref name="lo"/> on the right). A point on the cut moves with the
	/// node that starts there, not with one that ends there.
	/// </summary>
	private static (double At, bool IncludeAt)? FindCut(List<LayoutNodeResult> nodes, double lo, double hi, bool left)
	{
		const double eps = 0.01;
		var candidates = nodes
			.SelectMany(x => new[] { x.X, x.X + x.Width })
			.Where(x => x >= lo - eps && x <= hi + eps)
			.Append(lo)
			.Append(hi)
			.Distinct()
			.OrderBy(x => left ? -x : x);
		foreach (var at in candidates)
		{
			if (nodes.Any(x => x.X < at - eps && x.X + x.Width > at + eps))
				continue;
			return (at, !nodes.Any(x => Math.Abs(x.X + x.Width - at) <= eps));
		}
		return null;
	}

	/// <summary>
	/// Moves everything right of <paramref name="at"/> (and at it, when <paramref name="includeAt"/>) right by
	/// <paramref name="dx"/>. Orthogonal routes stay orthogonal: a horizontal segment across <paramref name="at"/> stretches.
	/// </summary>
	private static void InsertColumn(
		List<LayoutNodeResult> nodes,
		List<LayoutEdgeResult> edges,
		IEnumerable<Placement> loops,
		double at,
		double dx,
		bool includeAt
	)
	{
		const double eps = 0.01;

		bool Moves(double x) => includeAt ? x >= at - eps : x > at + eps;

		double Shift(double x) => Moves(x) ? x + dx : x;

		for (var i = 0; i < nodes.Count; i++)
		{
			if (nodes[i].X >= at - eps)
				nodes[i] = nodes[i] with { X = nodes[i].X + dx };
		}

		for (var i = 0; i < edges.Count; i++)
		{
			var e = edges[i];
			edges[i] = e with
			{
				Points = e.Points.Select(p => new LayoutPoint(Shift(p.X), p.Y)).ToList(),
				LabelPosition = e.LabelPosition is { } lp ? new LayoutPoint(Shift(lp.X), lp.Y) : null,
			};
		}

		foreach (var loop in loops)
		{
			for (var i = 0; i < loop.Points.Count; i++)
				loop.Points[i] = loop.Points[i] with { X = Shift(loop.Points[i].X) };
			if (loop.Label is { } lp)
				loop.Label = lp with { X = Shift(lp.X) };
			loop.MinX = Shift(loop.MinX);
			loop.MaxX = Shift(loop.MaxX);
		}
	}
}
