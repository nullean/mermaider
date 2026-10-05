namespace Sugiyama.Internal;

/// <summary>
/// Obstacle-aware orthogonal router for compound flowcharts. Works on the final absolute node/subgraph boxes:
/// ports are spread along the side facing the other end, a short stub leaves each port, and the stub ends are joined by
/// an A* search on a grid built from box borders (cost: length, bends, crossings, overlap with earlier routes, and a very
/// heavy cost for crossing subgraphs the edge does not belong to).
/// </summary>
internal static class FlowEdgeRouter
{
	internal sealed record Box(string Id, double X, double Y, double W, double H, PortOutline Outline, string? Member = null)
	{
		/// <summary>Node whose subgraph membership decides which subgraphs the edge may cross (differs from <see cref="Id"/> for subgraph borders).</summary>
		internal string MemberId => Member ?? Id;
		internal double Right => X + W;
		internal double Bottom => Y + H;
		internal double Cx => X + (W / 2);
		internal double Cy => Y + (H / 2);
	}

	internal sealed record GroupBox(string Id, double X, double Y, double W, double H, HashSet<string> NodeIds, double LabelW = 0);

	internal sealed record RouteEdge(int Index, string Source, string Target, double LabelW, double LabelH, string? SourceGroup = null, string? TargetGroup = null)
	{
		/// <summary>Route produced by the level layout (ports aligned to label columns); used when it is collision-free.</summary>
		internal IReadOnlyList<LayoutPoint>? Hint { get; init; }

		internal LayoutPoint? HintLabel { get; init; }
	}

	private const double Stub = 22;
	private const double Margin = 8;
	private const double BendCost = 45;
	private const double CrossCost = 140;
	private const double OverlapCost = 40;
	private const double ForeignGroupCost = 450;
	private const double LeaveGroupCost = 45;

	// parallel runs closer than this read as one thick line
	private const double CrowdedGap = 7;

	// the search keeps new runs a little further than that from earlier ones, so a label pill fits between neighbours
	private const double RouteGap = 10;

	// 0 = right, 1 = down, 2 = left, 3 = up
	private static readonly int[] Dx = [1, 0, -1, 0];
	private static readonly int[] Dy = [0, 1, 0, -1];

	private sealed class Seg(double x0, double y0, double x1, double y1)
	{
		internal double X0 = x0, Y0 = y0, X1 = x1, Y1 = y1;
		internal bool Horizontal => Math.Abs(Y0 - Y1) < 0.01;
	}

	private sealed class Plan
	{
		internal RouteEdge Edge = null!;
		internal Box S = null!;
		internal Box T = null!;
		internal int SSide;
		internal int TSide;
		internal LayoutPoint SPort;
		internal LayoutPoint TPort;
		internal double SStub = Stub;
		internal List<LayoutPoint>? Fixed;
		internal bool NoHint;
		internal bool Back => SSide == TSide;
		internal double TStub = Stub;

		// the last grid search and the routed segments it ran against: a rip-up pass that leaves the others unchanged
		// asks the same question again, so the answer is reused
		internal double[]? SearchedAgainst;
		internal List<LayoutPoint>? SearchResult;
	}

	internal static List<LayoutEdgeResult> Route(
		IReadOnlyList<Box> boxes, IReadOnlyList<GroupBox> groups, IReadOnlyList<RouteEdge> edges,
		LayoutDirection direction)
	{
		var byId = boxes.ToDictionary(b => b.Id, StringComparer.Ordinal);
		var results = new LayoutEdgeResult?[edges.Count];
		var plans = new List<Plan>();
		var vertical = direction is LayoutDirection.TD or LayoutDirection.BT;

		foreach (var e in edges)
		{
			if (!byId.TryGetValue(e.Source, out var s) || !byId.TryGetValue(e.Target, out var t))
			{
				results[e.Index] = new LayoutEdgeResult(e.Index, [new LayoutPoint(0, 0), new LayoutPoint(0, 0)], null);
				continue;
			}

			s = GroupEnd(s, e.SourceGroup, e.Target, groups);
			t = GroupEnd(t, e.TargetGroup, e.Source, groups);
			if (ReferenceEquals(s, t))
			{
				results[e.Index] = SelfLoop(e, s);
				continue;
			}

			var (ss, ts) = ChooseSides(s, t, vertical, boxes);
			plans.Add(new Plan { Edge = e, S = s, T = t, SSide = ss, TSide = ts });
		}

		AngleIntoSmallTargets(plans);
		AssignPorts(plans, groups, boxes);

		foreach (var p in plans)
			ShrinkStubs(p);

		foreach (var p in plans.Where(p => p.Edge.Hint is not null && !p.NoHint))
		{
			if (AcceptHint(p, boxes, groups) is { } fixedPts)
			{
				p.Fixed = fixedPts;
				p.SPort = fixedPts[0];
				p.TPort = fixedPts[^1];
			}
		}

		// the grid is only built once an edge needs a search: most edges take their hint or canonical route
		Grid? grid = null;
		var routed = new List<Seg>();
		var placedLabels = new List<(double X0, double Y0, double X1, double Y1)>();
		var polylines = new Dictionary<int, List<LayoutPoint>>();

		foreach (var p in plans.Where(p => p.Fixed is not null))
		{
			polylines[p.Edge.Index] = p.Fixed!;
			for (var i = 0; i < p.Fixed!.Count - 1; i++)
				routed.Add(new Seg(p.Fixed[i].X, p.Fixed[i].Y, p.Fixed[i + 1].X, p.Fixed[i + 1].Y));
		}

		var ordered = plans.Where(p => p.Fixed is null).OrderBy(p => Math.Abs(p.SPort.X - p.TPort.X) + Math.Abs(p.SPort.Y - p.TPort.Y)).ToList();
		var segsOf = new Dictionary<int, List<Seg>>();
		// pass 0 routes greedily; later passes rip each edge up and re-route it against all the others
		for (var pass = 0; pass < 3; pass++)
		{
			foreach (var p in ordered)
			{
				if (segsOf.TryGetValue(p.Edge.Index, out var old))
					_ = routed.RemoveAll(old.Contains);
				var pts = Canonical(p, boxes, groups, routed) ?? (grid ??= new Grid(boxes, groups, plans)).Find(p, groups, routed) ?? Fallback(p);
				pts = Straighten(Simplify(pts), p, plans);
				polylines[p.Edge.Index] = pts;
				var mine = new List<Seg>();
				for (var i = 0; i < pts.Count - 1; i++)
					mine.Add(new Seg(pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y));
				segsOf[p.Edge.Index] = mine;
				routed.AddRange(mine);
			}
			if (plans.Count > 60)
				break;
		}
		grid?.Release();

		foreach (var p in plans)
			polylines[p.Edge.Index] = ReduceBends(polylines[p.Edge.Index], p, boxes, groups);
		foreach (var p in plans.Where(p => p.Fixed is not null))
			polylines[p.Edge.Index] = Straighten(polylines[p.Edge.Index], p, plans);
		UniformJogs(plans, polylines);

		foreach (var p in plans)
		{
			var pts = polylines[p.Edge.Index];
			var label = p.Edge.LabelW > 0 ? PlaceLabel(pts, p.Edge, boxes, groups, placedLabels, routed, p.Fixed is not null ? p.Edge.HintLabel : null) : null;
			results[p.Edge.Index] = new LayoutEdgeResult(p.Edge.Index, pts, label);
		}

		return results.Where(r => r is not null).Select(r => r!).ToList();
	}

	// An edge written against a subgraph attaches to the subgraph's border rather than to the inner node it was mapped to.
	// (An edge between a subgraph and one of its own members keeps the inner node: there is no border to cross.)
	private static Box GroupEnd(Box node, string? groupId, string otherEnd, IReadOnlyList<GroupBox> groups)
	{
		if (groupId is null)
			return node;
		var g = groups.FirstOrDefault(x => x.Id == groupId);
		return g is null || g.NodeIds.Contains(otherEnd) ? node : new Box("\u0002" + groupId, g.X, g.Y, g.W, g.H, PortOutline.Rectangle, node.Id);
	}

	private static (int SSide, int TSide) ChooseSides(Box s, Box t, bool vertical, IReadOnlyList<Box> boxes)
	{
		// A back edge (target above source in a top-down flow) goes straight up when the way is free; otherwise it leaves
		// and enters through the side facing the nearer outside edge, so it never shares ports with the forward edges.
		if (vertical && t.Cy < s.Cy - 1)
		{
			var lo = Math.Max(s.X, t.X);
			var hi = Math.Min(s.Right, t.Right);
			var blocked = hi - lo < 10 || boxes.Any(b => b.Id != s.MemberId && b.Id != t.MemberId && b.Cy > t.Cy && b.Cy < s.Cy && b.Right > lo && b.X < hi);
			if (blocked)
			{
				var mid = (boxes.Min(b => b.X) + boxes.Max(b => b.Right)) / 2;
				var side = (s.Cx + t.Cx) / 2 >= mid ? 0 : 2;
				return (side, side);
			}
		}

		var dx = t.Cx - s.Cx;
		var dy = t.Cy - s.Cy;
		var gapY = Math.Max(t.Y - s.Bottom, s.Y - t.Bottom);
		var gapX = Math.Max(t.X - s.Right, s.X - t.Right);
		var useVertical = vertical ? gapY >= 0 || gapX < 0 : gapX < 0 && gapY >= 0;
		return useVertical
			? (dy >= 0 ? 1 : 3, dy >= 0 ? 3 : 1)
			: (dx >= 0 ? 0 : 2, dx >= 0 ? 2 : 0);
	}

	// Several arrows into a tiny node (an end state) would pile onto a few pixels: the outer ones come in from the sides instead.
	private static void AngleIntoSmallTargets(List<Plan> plans)
	{
		foreach (var grp in plans.Where(p => p.T.W < 40 && p.T.H < 40 && p.TSide == 3 && p.SSide == 1).GroupBy(p => p.T.Id))
		{
			var list = grp.OrderBy(p => p.S.Cx).ToList();
			if (list.Count < 2)
				continue;
			var first = list[0];
			var last = list[^1];
			if (first.S.Cx < first.T.Cx - 4)
			{
				first.TSide = 2;
				first.NoHint = true;
			}

			if (last.S.Cx > last.T.Cx + 4)
			{
				last.TSide = 0;
				last.NoHint = true;
			}
		}
	}

	// Ports sit where the edge can run straight: at the middle of the stretch where both boxes overlap along the side's axis
	// (else at the other end's centre), clamped to the usable part of the side and kept 14px apart.
	private static void AssignPorts(List<Plan> plans, IReadOnlyList<GroupBox> subgraphs, IReadOnlyList<Box> allBoxes)
	{
		var groups = new Dictionary<(string, int), List<(Plan Plan, bool IsSource, double Key)>>();
		var boxes = new Dictionary<string, Box>(StringComparer.Ordinal);
		void Add(Box b, int side, Plan p, bool src, Box other)
		{
			boxes[b.Id] = b;
			var alongX = side is 1 or 3;
			var (lo, hi) = Usable(b, alongX);
			var (olo, ohi) = Usable(other, alongX);
			var from = Math.Max(lo, olo);
			var to = Math.Min(hi, ohi);
			var key = from <= to ? (from + to) / 2 : alongX ? other.Cx : other.Cy;
			// An arrow that arrives side-on, or loops back, attaches at the middle of its side; so does an arrow to a
			// subgraph box (it points at the box as a whole, not at wherever the other end happens to be).
			var facing = (p.SSide + 2) % 4 == p.TSide;
			if (!facing || b.Id.StartsWith('\u0002'))
				key = alongX ? b.Cx : b.Cy;
			if (side == 3)
				key = BesideTitle(b, other, key, subgraphs, allBoxes);
			if (!groups.TryGetValue((b.Id, side), out var l))
				groups[(b.Id, side)] = l = [];
			l.Add((p, src, key));
		}

		foreach (var p in plans)
		{
			Add(p.S, p.SSide, p, true, p.T);
			Add(p.T, p.TSide, p, false, p.S);
		}

		foreach (var ((id, side), list) in groups)
		{
			var b = boxes[id];
			var alongX = side is 1 or 3;
			var (lo, hi) = Usable(b, alongX);
			var sorted = list.OrderBy(i => i.Key).ThenBy(i => i.Plan.Edge.Index).ToList();
			var pos = sorted.Select(i => b.Outline == PortOutline.Centre ? (lo + hi) / 2 : Math.Clamp(i.Key, lo, hi)).ToArray();
			if (b.Outline != PortOutline.Centre)
				Separate(pos, lo, hi, 14);
			var origin = alongX ? b.X : b.Y;
			var size = alongX ? b.W : b.H;
			for (var i = 0; i < sorted.Count; i++)
			{
				var pt = PortOn(b, side, (pos[i] - origin) / size);
				if (sorted[i].IsSource)
					sorted[i].Plan.SPort = pt;
				else
					sorted[i].Plan.TPort = pt;
			}
		}
	}

	// A port on the top of a node in the first row of a subgraph, for an edge coming from outside that subgraph, runs
	// straight up through the subgraph's header: keep it clear of the title text when the side is wide enough.
	private static double BesideTitle(Box b, Box other, double key, IReadOnlyList<GroupBox> subgraphs, IReadOnlyList<Box> allBoxes)
	{
		if (b.Id.StartsWith('\u0002'))
			return key;
		var (lo, hi) = Usable(b, true);
		foreach (var g in subgraphs)
		{
			if (g.LabelW <= 0 || !g.NodeIds.Contains(b.MemberId) || g.NodeIds.Contains(other.MemberId))
				continue;
			var titleEnd = g.X + g.LabelW + Margin;
			if (key >= titleEnd || key < g.X || titleEnd > hi)
				continue;
			// only the top row: anything of the subgraph above this node would sit between the port and the header
			if (allBoxes.Any(o => o.Id != b.Id && g.NodeIds.Contains(o.Id) && o.Bottom <= b.Y && o.Right > titleEnd - g.LabelW && o.X < titleEnd))
				continue;
			key = Math.Max(titleEnd, lo);
		}

		return key;
	}

	private static (double Lo, double Hi) Usable(Box b, bool alongX)
	{
		var origin = alongX ? b.X : b.Y;
		var size = alongX ? b.W : b.H;
		var margin = b.Outline is PortOutline.Diamond or PortOutline.Ellipse ? size * 0.22 : Math.Min(10, size / 4);
		return (origin + margin, origin + size - margin);
	}

	private static void Separate(double[] pos, double lo, double hi, double gap)
	{
		if (pos.Length > 1 && (hi - lo) < gap * (pos.Length - 1))
		{
			for (var i = 0; i < pos.Length; i++)
				pos[i] = lo + ((hi - lo) * i / (pos.Length - 1));
			return;
		}

		for (var i = 1; i < pos.Length; i++)
			pos[i] = Math.Max(pos[i], pos[i - 1] + gap);
		for (var i = pos.Length - 2; i >= 0; i--)
		{
			if (pos[^1] > hi)
				pos[i] = Math.Min(pos[i], pos[i + 1] - gap);
		}

		if (pos.Length > 0 && pos[^1] > hi)
		{
			pos[^1] = hi;
			for (var i = pos.Length - 2; i >= 0; i--)
				pos[i] = Math.Min(pos[i], pos[i + 1] - gap);
		}
	}

	// Facing ports closer than two stubs apart share the gap instead of overshooting each other.
	private static void ShrinkStubs(Plan p)
	{
		var facing = (p.SSide + 2) % 4 == p.TSide;
		if (!facing)
			return;
		var d = p.SSide is 1 or 3 ? Math.Abs(p.TPort.Y - p.SPort.Y) : Math.Abs(p.TPort.X - p.SPort.X);
		if (d >= (2 * Stub) + 2)
			return;
		var half = Math.Max(4, Math.Floor(d / 2 * 2) / 2);
		p.SStub = half;
		p.TStub = Math.Max(4, d - half);
		if (p.SStub + p.TStub > d)
			p.TStub = d - p.SStub;
	}

	// Port at fraction f along a bounding-box side, pulled onto the node outline for diamonds and ellipses.
	private static LayoutPoint PortOn(Box b, int side, double f)
	{
		var x = side is 1 or 3 ? b.X + (b.W * f) : (side == 0 ? b.Right : b.X);
		var y = side is 0 or 2 ? b.Y + (b.H * f) : (side == 1 ? b.Bottom : b.Y);
		if (b.Outline is PortOutline.Diamond or PortOutline.Ellipse)
		{
			if (side is 1 or 3)
			{
				var t = Math.Min(1, Math.Abs(x - b.Cx) / (b.W / 2));
				var depth = b.Outline == PortOutline.Diamond ? 1 - t : Math.Sqrt(Math.Max(0, 1 - (t * t)));
				y = side == 1 ? b.Cy + (b.H / 2 * depth) : b.Cy - (b.H / 2 * depth);
			}
			else
			{
				var t = Math.Min(1, Math.Abs(y - b.Cy) / (b.H / 2));
				var depth = b.Outline == PortOutline.Diamond ? 1 - t : Math.Sqrt(Math.Max(0, 1 - (t * t)));
				x = side == 0 ? b.Cx + (b.W / 2 * depth) : b.Cx - (b.W / 2 * depth);
			}
		}
		return new LayoutPoint(Round(x), Round(y));
	}

	private static double Round(double v) => Math.Round(v * 2) / 2;

	private static LayoutPoint Out(LayoutPoint p, int side, double d) => new(p.X + (Dx[side] * d), p.Y + (Dy[side] * d));

	private static LayoutEdgeResult SelfLoop(RouteEdge e, Box b)
	{
		var q = Math.Min(b.H / 4, 12);
		var x = b.Right + 28;
		var pts = new List<LayoutPoint>
		{
			new(b.Right, b.Cy - q), new(x, b.Cy - q), new(x, b.Cy + q), new(b.Right, b.Cy + q),
		};
		var label = e.LabelW > 0 ? new LayoutPoint(x + (e.LabelW / 2) + 4, b.Cy) : (LayoutPoint?)null;
		return new LayoutEdgeResult(e.Index, pts, label);
	}

	private static List<LayoutPoint> Fallback(Plan p)
	{
		var a = Out(p.SPort, p.SSide, p.SStub);
		var b = Out(p.TPort, p.TSide, p.TStub);
		var pts = new List<LayoutPoint> { p.SPort, a };
		if (p.SSide is 1 or 3)
		{
			var my = (a.Y + b.Y) / 2;
			pts.Add(new LayoutPoint(a.X, my));
			pts.Add(new LayoutPoint(b.X, my));
		}
		else
		{
			var mx = (a.X + b.X) / 2;
			pts.Add(new LayoutPoint(mx, a.Y));
			pts.Add(new LayoutPoint(mx, b.Y));
		}
		pts.Add(b);
		pts.Add(p.TPort);
		return pts;
	}

	// A short sideways jog between two parallel runs (ports a few px apart) becomes one straight line when the
	// other node's side can take the port at the same coordinate.
	private static List<LayoutPoint> Straighten(List<LayoutPoint> pts, Plan p, List<Plan> plans)
	{
		if (pts.Count != 4)
			return pts;
		var vertical = Math.Abs(pts[0].X - pts[1].X) < 0.01 && Math.Abs(pts[2].X - pts[3].X) < 0.01 && Math.Abs(pts[1].Y - pts[2].Y) < 0.01;
		var horizontal = Math.Abs(pts[0].Y - pts[1].Y) < 0.01 && Math.Abs(pts[2].Y - pts[3].Y) < 0.01 && Math.Abs(pts[1].X - pts[2].X) < 0.01;
		if (!vertical && !horizontal)
			return pts;
		var jog = vertical ? Math.Abs(pts[0].X - pts[3].X) : Math.Abs(pts[0].Y - pts[3].Y);
		if (jog is < 0.5 or > 16)
			return pts;
		// the target's port moves onto the source's line; failing that, the source's port moves onto the target's line
		if (vertical)
		{
			var x = pts[0].X;
			if (x >= p.T.X + 8 && x <= p.T.Right - 8 && !PortTaken(plans, p, p.T, p.TSide, x, pts[3].Y))
				return [pts[0], new LayoutPoint(x, pts[1].Y), new LayoutPoint(x, pts[2].Y), new LayoutPoint(x, pts[3].Y)];
			x = pts[3].X;
			if (p.Fixed is null && p.S.Outline == PortOutline.Rectangle && x >= p.S.X + 8 && x <= p.S.Right - 8 && !PortTaken(plans, p, p.S, p.SSide, x, pts[0].Y))
				return [new LayoutPoint(x, pts[0].Y), new LayoutPoint(x, pts[1].Y), new LayoutPoint(x, pts[2].Y), pts[3]];
			return pts;
		}

		var y = pts[0].Y;
		if (y >= p.T.Y + 8 && y <= p.T.Bottom - 8 && !PortTaken(plans, p, p.T, p.TSide, pts[3].X, y))
			return [pts[0], new LayoutPoint(pts[1].X, y), new LayoutPoint(pts[2].X, y), new LayoutPoint(pts[3].X, y)];
		y = pts[3].Y;
		if (p.Fixed is null && p.S.Outline == PortOutline.Rectangle && y >= p.S.Y + 8 && y <= p.S.Bottom - 8 && !PortTaken(plans, p, p.S, p.SSide, pts[0].X, y))
			return [new LayoutPoint(pts[0].X, y), new LayoutPoint(pts[1].X, y), new LayoutPoint(pts[2].X, y), pts[3]];
		return pts;
	}

	// The layout's own route (ports aligned to label columns, uniform jogs) is used when nothing is in its way.
	private static List<LayoutPoint>? AcceptHint(Plan p, IReadOnlyList<Box> boxes, IReadOnlyList<GroupBox> groups) =>
		ValidatePath(p, Simplify(p.Edge.Hint!.ToList()), boxes, groups);

	private static List<LayoutPoint>? ValidatePath(Plan p, List<LayoutPoint> pts, IReadOnlyList<Box> boxes, IReadOnlyList<GroupBox> groups)
	{
		if (pts.Count < 2)
			return null;
		static bool On(Box b, LayoutPoint q) => q.X >= b.X - 1 && q.X <= b.Right + 1 && q.Y >= b.Y - 1 && q.Y <= b.Bottom + 1;
		if (!On(p.S, pts[0]) || !On(p.T, pts[^1]))
			return null;

		var sId = p.S.MemberId;
		var tId = p.T.MemberId;
		for (var i = 0; i < pts.Count - 1; i++)
		{
			var x0 = Math.Min(pts[i].X, pts[i + 1].X);
			var x1 = Math.Max(pts[i].X, pts[i + 1].X);
			var y0 = Math.Min(pts[i].Y, pts[i + 1].Y);
			var y1 = Math.Max(pts[i].Y, pts[i + 1].Y);
			foreach (var b in boxes)
			{
				var inset = b.Id == sId || b.Id == tId ? 2 : -1;
				if (x1 > b.X + inset && x0 < b.Right - inset && y1 > b.Y + inset && y0 < b.Bottom - inset)
					return null;
			}

			var mx = (x0 + x1) / 2;
			var my = (y0 + y1) / 2;
			foreach (var g in groups)
			{
				var hasS = g.NodeIds.Contains(sId);
				var hasT = g.NodeIds.Contains(tId);
				if (!hasS && !hasT && mx > g.X && mx < g.X + g.W && my > g.Y && my < g.Y + g.H)
					return null;
				if (g.LabelW > 0 && x1 > g.X + 2 && x0 < g.X + g.LabelW && y1 > g.Y + 2 && y0 < g.Y + 28)
					return null;
			}
		}

		// ports on diamonds/ellipses slide onto the outline (the first/last run is vertical in a top-down layout)
		pts[0] = OntoOutline(p.S, pts[0]);
		pts[^1] = OntoOutline(p.T, pts[^1]);
		return pts;
	}

	// Straight / single-Z candidate between facing ports (jog in the middle of the gap): same shape for every such edge,
	// accepted only when it is free of obstacles and neither overlaps nor crosses what is routed already.
	private static List<LayoutPoint>? Canonical(Plan p, IReadOnlyList<Box> boxes, IReadOnlyList<GroupBox> groups, List<Seg> routed)
	{
		// A back edge that leaves through a side loops around the whole drawing: out to the outermost lane, along it, and back in.
		if (p.Back && p.SSide is 0 or 2)
		{
			var left = p.SSide == 2;
			var edgeX = left
				? Math.Min(boxes.Min(b => b.X), groups.Count > 0 ? groups.Min(g => g.X) : double.MaxValue) - 24
				: Math.Max(boxes.Max(b => b.Right), groups.Count > 0 ? groups.Max(g => g.X + g.W) : double.MinValue) + 24;
			List<LayoutPoint> loop = [p.SPort, new LayoutPoint(edgeX, p.SPort.Y), new LayoutPoint(edgeX, p.TPort.Y), p.TPort];
			return AcceptWithoutOverlap(p, Simplify(loop), boxes, groups, routed);
		}

		var verticalFlow = p.SSide is 1 && p.TSide is 3;
		var horizontalFlow = p.SSide is 0 && p.TSide is 2;
		if (!verticalFlow && !horizontalFlow)
			return null;
		var a = p.SPort;
		var b = p.TPort;
		List<LayoutPoint> pts;
		if (verticalFlow)
		{
			if (Math.Abs(a.X - b.X) < 0.5)
			{
				pts = [a, b];
			}
			else
			{
				var lo = a.Y + 12;
				var hi = b.Y - 22;
				if (lo > hi)
					return null;
				var y = p.Edge.LabelW > 0 ? lo : (lo + hi) / 2;
				pts = [a, new LayoutPoint(a.X, y), new LayoutPoint(b.X, y), b];
			}
		}
		else if (Math.Abs(a.Y - b.Y) < 0.5)
		{
			pts = [a, b];
		}
		else
		{
			var lo = a.X + 12;
			var hi = b.X - 22;
			if (lo > hi)
				return null;
			var x = p.Edge.LabelW > 0 ? lo : (lo + hi) / 2;
			pts = [a, new LayoutPoint(x, a.Y), new LayoutPoint(x, b.Y), b];
		}

		return AcceptWithoutOverlap(p, Simplify(pts), boxes, groups, routed);
	}

	private static List<LayoutPoint>? AcceptWithoutOverlap(Plan p, List<LayoutPoint> pts, IReadOnlyList<Box> boxes, IReadOnlyList<GroupBox> groups, List<Seg> routed)
	{
		if (ValidatePath(p, pts, boxes, groups) is not { } ok)
			return null;
		for (var i = 0; i < ok.Count - 1; i++)
		{
			var seg = new Seg(ok[i].X, ok[i].Y, ok[i + 1].X, ok[i + 1].Y);
			foreach (var r in routed)
			{
				if (seg.Horizontal == r.Horizontal)
				{
					if (seg.Horizontal && Math.Abs(seg.Y0 - r.Y0) < CrowdedGap && Math.Min(Math.Max(seg.X0, seg.X1), Math.Max(r.X0, r.X1)) - Math.Max(Math.Min(seg.X0, seg.X1), Math.Min(r.X0, r.X1)) > 6)
						return null;
					if (!seg.Horizontal && Math.Abs(seg.X0 - r.X0) < CrowdedGap && Math.Min(Math.Max(seg.Y0, seg.Y1), Math.Max(r.Y0, r.Y1)) - Math.Max(Math.Min(seg.Y0, seg.Y1), Math.Min(r.Y0, r.Y1)) > 6)
						return null;
				}
				else if (SegmentsCross(seg, r))
				{
					return null;
				}
			}
		}

		return ok;
	}

	private static bool SegmentsCross(Seg h, Seg v)
	{
		if (!h.Horizontal)
			(h, v) = (v, h);
		var hx0 = Math.Min(h.X0, h.X1);
		var hx1 = Math.Max(h.X0, h.X1);
		var vy0 = Math.Min(v.Y0, v.Y1);
		var vy1 = Math.Max(v.Y0, v.Y1);
		return v.X0 > hx0 + 0.5 && v.X0 < hx1 - 0.5 && h.Y0 > vy0 + 0.5 && h.Y0 < vy1 - 0.5;
	}

	private static LayoutPoint OntoOutline(Box b, LayoutPoint q)
	{
		if (b.Outline is not (PortOutline.Diamond or PortOutline.Ellipse))
			return q;
		var onTopBottom = Math.Abs(q.Y - b.Y) < 1.5 || Math.Abs(q.Y - b.Bottom) < 1.5;
		if (onTopBottom)
		{
			var projected = PortOn(b, q.Y >= b.Cy ? 1 : 3, Math.Clamp((q.X - b.X) / b.W, 0, 1));
			return new LayoutPoint(q.X, projected.Y);
		}

		var side = PortOn(b, q.X >= b.Cx ? 0 : 2, Math.Clamp((q.Y - b.Y) / b.H, 0, 1));
		return new LayoutPoint(side.X, q.Y);
	}

	// jog → column → jog (four bends) becomes one jog next to the source and a long straight run into the target
	// (the shape mermaid.js draws), when that stays clear of obstacles.
	private static List<LayoutPoint> ReduceBends(List<LayoutPoint> pts, Plan p, IReadOnlyList<Box> boxes, IReadOnlyList<GroupBox> groups)
	{
		// labelled edges keep their label column: it is what keeps neighbouring pills apart
		if (pts.Count != 6 || p.Edge.LabelW > 0)
			return pts;
		var vertical = Math.Abs(pts[0].X - pts[1].X) < 0.01 && Math.Abs(pts[1].Y - pts[2].Y) < 0.01 && Math.Abs(pts[2].X - pts[3].X) < 0.01
			&& Math.Abs(pts[3].Y - pts[4].Y) < 0.01 && Math.Abs(pts[4].X - pts[5].X) < 0.01;
		var horizontal = Math.Abs(pts[0].Y - pts[1].Y) < 0.01 && Math.Abs(pts[1].X - pts[2].X) < 0.01 && Math.Abs(pts[2].Y - pts[3].Y) < 0.01
			&& Math.Abs(pts[3].X - pts[4].X) < 0.01 && Math.Abs(pts[4].Y - pts[5].Y) < 0.01;
		if (!vertical && !horizontal)
			return pts;
		// forward only: the long run must continue in the flow direction
		if (vertical ? !(pts[5].Y > pts[0].Y && pts[1].Y > pts[0].Y) : !(pts[5].X > pts[0].X && pts[1].X > pts[0].X))
			return pts;
		List<LayoutPoint> candidate = vertical
			? [pts[0], pts[1], new LayoutPoint(pts[4].X, pts[1].Y), pts[5]]
			: [pts[0], pts[1], new LayoutPoint(pts[1].X, pts[4].Y), pts[5]];
		return ValidatePath(p, Simplify(candidate), boxes, groups) ?? pts;
	}

	// Z-bends between the same two layers jog at one shared coordinate (a bus line in the gap), so fans and merges look
	// uniform and mirror-symmetric instead of each jogging at its own height.
	private static void UniformJogs(List<Plan> plans, Dictionary<int, List<LayoutPoint>> polylines)
	{
		var zs = new List<(Plan Plan, bool Vertical)>();
		foreach (var p in plans)
		{
			var pts = polylines[p.Edge.Index];
			if (pts.Count != 4)
				continue;
			var vertical = Math.Abs(pts[0].X - pts[1].X) < 0.01 && Math.Abs(pts[1].Y - pts[2].Y) < 0.01 && Math.Abs(pts[2].X - pts[3].X) < 0.01;
			var horizontal = Math.Abs(pts[0].Y - pts[1].Y) < 0.01 && Math.Abs(pts[1].X - pts[2].X) < 0.01 && Math.Abs(pts[2].Y - pts[3].Y) < 0.01;
			if (vertical && p.T.Cy > p.S.Cy + 1)
				zs.Add((p, true));
			else if (horizontal && p.T.Cx > p.S.Cx + 1)
				zs.Add((p, false));
		}

		foreach (var grp in zs.GroupBy(z => (z.Vertical, Math.Round((z.Vertical ? z.Plan.S.Cy : z.Plan.S.Cx) / 8), Math.Round((z.Vertical ? z.Plan.T.Cy : z.Plan.T.Cx) / 8))))
		{
			var list = grp.ToList();
			if (list.Count < 2)
				continue;
			var vertical = grp.Key.Vertical;
			var lo = list.Max(z => vertical ? z.Plan.S.Bottom : z.Plan.S.Right) + 12;
			var hi = list.Min(z => vertical ? z.Plan.T.Y : z.Plan.T.X) - 22;
			if (lo > hi)
				continue;
			// labelled gaps jog right under the source so the pill has the long run; plain gaps jog at the middle
			var target = list.Any(z => z.Plan.Edge.LabelW > 0) ? lo : (lo + hi) / 2;

			// jogs whose runs overlap (a fan crossing a fan) get their own lane next to each other instead of sharing one line
			(double A, double B) Span(Plan pl)
			{
				var pts = polylines[pl.Edge.Index];
				var a = vertical ? pts[1].X : pts[1].Y;
				var b = vertical ? pts[2].X : pts[2].Y;
				return (Math.Min(a, b), Math.Max(a, b));
			}

			var lane = list.ToDictionary(z => z.Plan.Edge.Index, _ => target);
			var sorted = list.OrderBy(z => Span(z.Plan).A).ToList();
			var clusters = new List<List<(Plan Plan, bool Vertical)>>();
			foreach (var z in sorted)
			{
				var (a, b) = Span(z.Plan);
				var home = clusters.FirstOrDefault(c => c.Any(o => { var (oa, ob) = Span(o.Plan); return a < ob - 2 && b > oa + 2; }));
				if (home is null)
					clusters.Add([z]);
				else
					home.Add(z);
			}

			foreach (var c in clusters.Where(c => c.Count > 1))
			{
				var ordered = c.OrderBy(z => vertical ? z.Plan.S.Cx : z.Plan.S.Cy).ToList();
				for (var i = 0; i < ordered.Count; i++)
					lane[ordered[i].Plan.Edge.Index] = Math.Clamp(target + ((i - ((ordered.Count - 1) / 2.0)) * 14), lo, hi);
			}

			foreach (var (plan, _) in list)
			{
				var pts = polylines[plan.Edge.Index];
				var at = lane[plan.Edge.Index];
				polylines[plan.Edge.Index] = vertical
					? [pts[0], new LayoutPoint(pts[1].X, at), new LayoutPoint(pts[2].X, at), pts[3]]
					: [pts[0], new LayoutPoint(at, pts[1].Y), new LayoutPoint(at, pts[2].Y), pts[3]];
			}

			// Two jogs on neighbouring lanes whose end run of the upper one and start run of the lower one would sit on the
			// same line: slide the lower one's start port aside so the two cross cleanly instead of merging into one rail.
			foreach (var c in clusters.Where(c => c.Count > 1))
			{
				var byLane = c.OrderBy(z => lane[z.Plan.Edge.Index]).ToList();
				for (var i = 0; i < byLane.Count; i++)
				{
					for (var j = i + 1; j < byLane.Count; j++)
					{
						var upper = polylines[byLane[i].Plan.Edge.Index];
						var lowerPlan = byLane[j].Plan;
						var lower = polylines[lowerPlan.Edge.Index];
						var upperEnd = vertical ? upper[3].X : upper[3].Y;
						var lowerStart = vertical ? lower[0].X : lower[0].Y;
						if (Math.Abs(upperEnd - lowerStart) > 2)
							continue;
						var lo2 = vertical ? lowerPlan.S.X + 6 : lowerPlan.S.Y + 6;
						var hi2 = vertical ? lowerPlan.S.Right - 6 : lowerPlan.S.Bottom - 6;
						var shifted = lowerStart + 8 <= hi2 ? lowerStart + 8 : lowerStart - 8;
						if (shifted < lo2 || shifted > hi2)
							continue;
						polylines[lowerPlan.Edge.Index] = vertical
							? [new LayoutPoint(shifted, lower[0].Y), new LayoutPoint(shifted, lower[1].Y), lower[2], lower[3]]
							: [new LayoutPoint(lower[0].X, shifted), new LayoutPoint(lower[1].X, shifted), lower[2], lower[3]];
					}
				}
			}
		}
	}

	private static bool PortTaken(List<Plan> plans, Plan self, Box box, int side, double x, double y)
	{
		foreach (var q in plans)
		{
			if (ReferenceEquals(q, self))
				continue;
			foreach (var (b, sd, port) in new[] { (q.S, q.SSide, q.SPort), (q.T, q.TSide, q.TPort) })
			{
				if (b.Id == box.Id && sd == side && Math.Abs(port.X - x) + Math.Abs(port.Y - y) < 12)
					return true;
			}
		}
		return false;
	}

	private static List<LayoutPoint> Simplify(List<LayoutPoint> pts)
	{
		var r = new List<LayoutPoint>();
		foreach (var p in pts)
		{
			if (r.Count > 0 && Math.Abs(r[^1].X - p.X) < 0.01 && Math.Abs(r[^1].Y - p.Y) < 0.01)
				continue;
			while (r.Count >= 2)
			{
				var a = r[^2];
				var b = r[^1];
				var sameX = Math.Abs(a.X - b.X) < 0.01 && Math.Abs(b.X - p.X) < 0.01;
				var sameY = Math.Abs(a.Y - b.Y) < 0.01 && Math.Abs(b.Y - p.Y) < 0.01;
				if (!sameX && !sameY)
					break;
				r.RemoveAt(r.Count - 1);
			}
			r.Add(p);
		}
		return r;
	}

	// ---- label placement ------------------------------------------------------------------------------------

	private static LayoutPoint? PlaceLabel(
		List<LayoutPoint> pts, RouteEdge e, IReadOnlyList<Box> boxes, IReadOnlyList<GroupBox> groups,
		List<(double X0, double Y0, double X1, double Y1)> placed, List<Seg> routed, LayoutPoint? preferred = null)
	{
		var segs = new List<(int I, double Len)>();
		for (var i = 0; i < pts.Count - 1; i++)
		{
			var len = Math.Abs(pts[i + 1].X - pts[i].X) + Math.Abs(pts[i + 1].Y - pts[i].Y);
			segs.Add((i, len));
		}

		LayoutPoint? best = null;
		var bestCost = double.MaxValue;
		var hw = (e.LabelW / 2) + 2;
		var hh = (e.LabelH / 2) + 2;
		var maxLen = Math.Max(1, segs.Max(s => s.Len));
		var candidates = new List<(double X, double Y, double Cost)>();
		foreach (var (i, len) in segs.OrderByDescending(s => s.Len).Take(3))
		{
			foreach (var f in new[] { 0.5, 0.35, 0.65, 0.25, 0.75, 0.15, 0.85 })
			{
				candidates.Add((
					pts[i].X + ((pts[i + 1].X - pts[i].X) * f),
					pts[i].Y + ((pts[i + 1].Y - pts[i].Y) * f),
					((1 - (len / maxLen)) * 30) + (Math.Abs(f - 0.5) * 20)));
			}
		}

		if (preferred is { } pref)
		{
			// the route may have been reshaped since the hint was made: keep the preferred spot on the line
			double bx = pts[0].X, by = pts[0].Y, bd = double.MaxValue;
			for (var i = 0; i < pts.Count - 1; i++)
			{
				var x = Math.Clamp(pref.X, Math.Min(pts[i].X, pts[i + 1].X), Math.Max(pts[i].X, pts[i + 1].X));
				var y = Math.Clamp(pref.Y, Math.Min(pts[i].Y, pts[i + 1].Y), Math.Max(pts[i].Y, pts[i + 1].Y));
				var d = Math.Abs(x - pref.X) + Math.Abs(y - pref.Y);
				if (d < bd)
				{
					bd = d;
					bx = x;
					by = y;
				}
			}

			candidates.Add((bx, by, -25));
		}

		// Sibling labels line up when anchored just before the arrowhead stub of the final run (like mermaid.js).
		var last = pts.Count - 2;
		var lastLen = segs[last].Len;
		var along = Math.Abs(pts[last + 1].X - pts[last].X) > Math.Abs(pts[last + 1].Y - pts[last].Y) ? e.LabelW : e.LabelH;
		var back = Stub + (along / 2) + 6;
		if (lastLen >= back + (along / 2) + 6)
		{
			var t = back / lastLen;
			candidates.Add((pts[last + 1].X + ((pts[last].X - pts[last + 1].X) * t), pts[last + 1].Y + ((pts[last].Y - pts[last + 1].Y) * t), -10));
		}

		foreach (var (cx, cy, baseCost) in candidates)
		{
			{
				var cost = baseCost;
				var r = (X0: cx - hw, Y0: cy - hh, X1: cx + hw, Y1: cy + hh);
				foreach (var b in boxes)
				{
					if (r.X0 < b.Right && r.X1 > b.X && r.Y0 < b.Bottom && r.Y1 > b.Y)
						cost += 1000;
				}
				foreach (var g in groups)
				{
					// a label sitting on a subgraph border/header reads as belonging to neither side
					var overlaps = r.X0 < g.X + g.W && r.X1 > g.X && r.Y0 < g.Y + g.H && r.Y1 > g.Y;
					var inside = r.X0 >= g.X && r.X1 <= g.X + g.W && r.Y0 >= g.Y + 36 && r.Y1 <= g.Y + g.H;
					if (overlaps && !inside && !(r.X0 <= g.X && r.X1 >= g.X + g.W))
						cost += 90;
				}
				foreach (var l in placed)
				{
					if (r.X0 < l.X1 && r.X1 > l.X0 && r.Y0 < l.Y1 && r.Y1 > l.Y0)
						cost += 800;
				}
				foreach (var s in routed)
				{
					if (SegHitsRect(s, r) && !OnPath(s, pts))
						cost += 120;
				}
				if (cost < bestCost)
				{
					bestCost = cost;
					best = new LayoutPoint(cx, cy);
				}
			}
		}

		if (best is { } bp)
			placed.Add((bp.X - hw, bp.Y - hh, bp.X + hw, bp.Y + hh));
		return best;
	}

	private static bool OnPath(Seg s, List<LayoutPoint> pts)
	{
		for (var i = 0; i < pts.Count - 1; i++)
		{
			if (Math.Abs(pts[i].X - s.X0) < 0.01 && Math.Abs(pts[i].Y - s.Y0) < 0.01
				&& Math.Abs(pts[i + 1].X - s.X1) < 0.01 && Math.Abs(pts[i + 1].Y - s.Y1) < 0.01)
				return true;
		}
		return false;
	}

	private static bool SegHitsRect(Seg s, (double X0, double Y0, double X1, double Y1) r)
	{
		var x0 = Math.Min(s.X0, s.X1);
		var x1 = Math.Max(s.X0, s.X1);
		var y0 = Math.Min(s.Y0, s.Y1);
		var y1 = Math.Max(s.Y0, s.Y1);
		return x1 > r.X0 && x0 < r.X1 && y1 > r.Y0 && y0 < r.Y1;
	}

	// ---- grid + A* ------------------------------------------------------------------------------------------

	private sealed class Grid
	{
		private readonly double[] _xs;
		private readonly double[] _ys;
		private readonly int[] _owner; // node index whose expanded interior contains the point, else -1
		private SearchSpace _space;
		private readonly double[] _hx;
		private readonly double[] _hy;
		private double _crossCost = CrossCost;
		private bool _countGridCrossings;
		private double _lengthWeight = 1;
		private readonly GroupBox[] _allGroups;
		private readonly MinHeap _pq = new();

		// routed segments bucketed by the grid lines/intervals a step could interact with; buckets keep routed order so the
		// step cost sums its terms exactly as a scan over every routed segment would
		private readonly SegBuckets _parallelOnRow; // horizontal segments within RouteGap of row y
		private readonly SegBuckets _crossingColumn; // vertical segments whose x falls in column interval [x, x+1]
		private readonly SegBuckets _parallelOnColumn; // vertical segments within RouteGap of column x
		private readonly SegBuckets _crossingRow; // horizontal segments whose y falls in row interval [y, y+1]
		private List<Seg> _routed = null!;

		internal Grid(IReadOnlyList<Box> boxes, IReadOnlyList<GroupBox> groups, List<Plan> plans)
		{
			_allGroups = [.. groups];
			var xs = new List<double>();
			var ys = new List<double>();
			foreach (var b in boxes)
			{
				xs.Add(b.X - Margin);
				xs.Add(b.X);
				xs.Add(b.Right);
				xs.Add(b.Right + Margin);
				ys.Add(b.Y - Margin);
				ys.Add(b.Y);
				ys.Add(b.Bottom);
				ys.Add(b.Bottom + Margin);
			}

			foreach (var g in groups)
			{
				xs.Add(g.X - Margin);
				xs.Add(g.X + g.W + Margin);
				ys.Add(g.Y - Margin);
				ys.Add(g.Y + g.H + Margin);
			}

			foreach (var p in plans)
			{
				foreach (var (port, side, stub) in new[] { (p.SPort, p.SSide, p.SStub), (p.TPort, p.TSide, p.TStub) })
				{
					var o = Out(port, side, stub);
					xs.Add(port.X);
					xs.Add(o.X);
					ys.Add(port.Y);
					ys.Add(o.Y);
				}
			}

			_xs = Dedupe(AddMidlines(xs));
			_ys = Dedupe(AddMidlines(ys));
			var n = _xs.Length * _ys.Length;
			_owner = new int[n];
			Array.Fill(_owner, -1);
			for (var bi = 0; bi < boxes.Count; bi++)
			{
				// grid lines strictly inside the box's margin, found by binary search on the sorted lines
				var b = boxes[bi];
				var x1 = FirstAtOrAbove(_xs, b.Right + Margin - 0.01);
				var y0 = FirstAbove(_ys, b.Y - Margin + 0.01);
				var y1 = FirstAtOrAbove(_ys, b.Bottom + Margin - 0.01);
				for (var ix = FirstAbove(_xs, b.X - Margin + 0.01); ix < x1; ix++)
				{
					for (var iy = y0; iy < y1; iy++)
						_owner[(ix * _ys.Length) + iy] = bi;
				}
			}

			_space = SearchSpace.Rent(n);
			_hx = new double[_xs.Length];
			_hy = new double[_ys.Length];
			_parallelOnRow = new SegBuckets(_ys.Length);
			_crossingColumn = new SegBuckets(_xs.Length);
			_parallelOnColumn = new SegBuckets(_xs.Length);
			_crossingRow = new SegBuckets(_ys.Length);
		}

		internal void Release()
		{
			SearchSpace.Return(_space);
			_space = null!;
		}

		private static List<double> AddMidlines(List<double> v)
		{
			var s = Dedupe(v);
			var r = new List<double>(s);
			for (var i = 0; i + 1 < s.Length; i++)
			{
				var gap = s[i + 1] - s[i];
				if (gap < 18)
					continue;
				var mid = (s[i] + s[i + 1]) / 2;
				r.Add(mid);
				// wide gaps get a lane every 8px so bundles of parallel edges can each take their own line
				var lanes = Math.Min(10, (int)((gap - 24) / 8));
				for (var k = 1; k <= lanes / 2; k++)
				{
					r.Add(mid - (8 * k));
					r.Add(mid + (8 * k));
				}
			}
			return r;
		}

		// lines snapped to the half pixel, without repeats, ascending (the first of +0/-0 is kept, as Distinct would)
		private static double[] Dedupe(List<double> v)
		{
			var seen = new HashSet<double>(v.Count);
			var r = new List<double>(v.Count);
			foreach (var x in v)
			{
				var snapped = Math.Round(x * 2) / 2;
				if (seen.Add(snapped))
					r.Add(snapped);
			}
			r.Sort();
			return [.. r];
		}

		private int IndexOf(double[] a, double v)
		{
			var i = Array.BinarySearch(a, Math.Round(v * 2) / 2);
			return i >= 0 ? i : -1;
		}

		internal List<LayoutPoint>? Find(Plan p, IReadOnlyList<GroupBox> groups, List<Seg> routed)
		{
			if (p.SearchedAgainst is { } seen && SameSegments(seen, routed))
				return p.SearchResult is null ? null : [.. p.SearchResult];
			var path = Search(p, groups, routed);
			p.SearchedAgainst = Snapshot(routed);
			p.SearchResult = path is null ? null : [.. path];
			return path;
		}

		private static double[] Snapshot(List<Seg> routed)
		{
			var a = new double[routed.Count * 4];
			for (var i = 0; i < routed.Count; i++)
			{
				var s = routed[i];
				a[i * 4] = s.X0;
				a[(i * 4) + 1] = s.Y0;
				a[(i * 4) + 2] = s.X1;
				a[(i * 4) + 3] = s.Y1;
			}
			return a;
		}

		private static bool SameSegments(double[] seen, List<Seg> routed)
		{
			if (seen.Length != routed.Count * 4)
				return false;
			for (var i = 0; i < routed.Count; i++)
			{
				var s = routed[i];
				// bitwise equality: the search must see exactly the same numbers to give exactly the same answer
				if (!seen[i * 4].Equals(s.X0) || !seen[(i * 4) + 1].Equals(s.Y0) || !seen[(i * 4) + 2].Equals(s.X1) || !seen[(i * 4) + 3].Equals(s.Y1))
					return false;
			}
			return true;
		}

		private void IndexRouted(List<Seg> routed)
		{
			_routed = routed;
			_parallelOnRow.Clear();
			_crossingColumn.Clear();
			_parallelOnColumn.Clear();
			_crossingRow.Clear();
			// bucket bounds are a little looser than the tests in StepCost: a bucket may hold a segment that adds nothing,
			// never miss one that adds something
			const double slack = 1;
			for (var i = 0; i < routed.Count; i++)
			{
				var s = routed[i];
				if (s.Horizontal)
				{
					AddLines(_parallelOnRow, _ys, i, s.Y0 - RouteGap - slack, s.Y0 + RouteGap + slack);
					AddIntervals(_crossingRow, _ys, i, s.Y0 - slack, s.Y0 + slack);
				}
				else
				{
					AddLines(_parallelOnColumn, _xs, i, s.X0 - RouteGap - slack, s.X0 + RouteGap + slack);
					AddIntervals(_crossingColumn, _xs, i, s.X0 - slack, s.X0 + slack);
				}
			}
			_parallelOnRow.Build();
			_crossingColumn.Build();
			_parallelOnColumn.Build();
			_crossingRow.Build();
		}

		// grid lines strictly inside (lo, hi)
		private static void AddLines(SegBuckets b, double[] lines, int seg, double lo, double hi)
		{
			var first = FirstAbove(lines, lo);
			var last = FirstAbove(lines, hi) - 1;
			if (last >= 0 && lines[last] >= hi)
				last--;
			b.Add(seg, first, Math.Min(last, lines.Length - 1));
		}

		// intervals [lines[k], lines[k+1]] that overlap (lo, hi)
		private static void AddIntervals(SegBuckets b, double[] lines, int seg, double lo, double hi)
		{
			var first = Math.Max(0, FirstAbove(lines, lo) - 1);
			var last = Math.Min(lines.Length - 2, FirstAbove(lines, hi) - 1);
			b.Add(seg, first, last);
		}

		// index of the first grid line at or above v (lines.Length when none is)
		private static int FirstAtOrAbove(double[] lines, double v)
		{
			int lo = 0, hi = lines.Length;
			while (lo < hi)
			{
				var mid = (lo + hi) >>> 1;
				if (lines[mid] >= v)
					hi = mid;
				else
					lo = mid + 1;
			}
			return lo;
		}

		// index of the first grid line greater than v (lines.Length when none is)
		private static int FirstAbove(double[] lines, double v)
		{
			int lo = 0, hi = lines.Length;
			while (lo < hi)
			{
				var mid = (lo + hi) >>> 1;
				if (lines[mid] > v)
					hi = mid;
				else
					lo = mid + 1;
			}
			return lo;
		}

		private List<LayoutPoint>? Search(Plan p, IReadOnlyList<GroupBox> groups, List<Seg> routed)
		{
			var a = Out(p.SPort, p.SSide, p.SStub);
			var b = Out(p.TPort, p.TSide, p.TStub);
			var sx = IndexOf(_xs, a.X);
			var sy = IndexOf(_ys, a.Y);
			var tx = IndexOf(_xs, b.X);
			var ty = IndexOf(_ys, b.Y);
			if (sx < 0 || sy < 0 || tx < 0 || ty < 0)
				return null;

			var foreign = new List<GroupBox>();
			var common = new List<GroupBox>();
			foreach (var g in groups)
			{
				var hasS = g.NodeIds.Contains(p.S.MemberId);
				var hasT = g.NodeIds.Contains(p.T.MemberId);
				if (hasS && hasT)
					common.Add(g);
				else if (!hasS && !hasT)
					foreign.Add(g);
			}

			var version = _space.NextVersion();
			var states = _space.States;
			var steps = _space.Steps;
			// a loop around the outside is worth a long detour: crossing the forward edges is what makes it unreadable
			_crossCost = p.Back ? CrossCost * 3 : CrossCost;
			_countGridCrossings = p.Back;
			_lengthWeight = p.Back ? 0.6 : 1;
			IndexRouted(routed);
			var xs = _xs;
			var ys = _ys;
			var owner = _owner;
			var nxs = xs.Length;
			var ny = ys.Length;
			// the heuristic split per axis, summed exactly as Heuristic() sums it
			var hx = _hx;
			var hy = _hy;
			for (var i = 0; i < nxs; i++)
				hx[i] = Math.Abs(xs[i] - xs[tx]);
			for (var i = 0; i < ny; i++)
				hy[i] = Math.Abs(ys[i] - ys[ty]);
			var pq = _pq;
			pq.Clear();
			var startDir = p.SSide;
			var start = (((sx * ny) + sy) * 5) + startDir;
			states[start] = new SearchState(0, version, -1);
			pq.Enqueue(start, Heuristic(sx, sy, tx, ty));
			var endDir = (p.TSide + 2) % 4; // direction of the last move into the end stub
			var pops = 0;
			var goal = -1;
			while (pq.TryDequeue(out var cur, out var queuedAt))
			{
				if (++pops > 400_000)
					break;
				var dir = cur % 5;
				var cell = cur / 5;
				var cx = cell / ny;
				var cy = cell % ny;
				var cd = states[cur].Dist;
				if (cx == tx && cy == ty)
				{
					goal = cur;
					break;
				}

				// a stale entry (queued before this state's distance improved) pops after the entry for the improved distance,
				// whose expansion already gave every neighbour a distance at least as good: expanding again changes nothing
				if (queuedAt != cd + (hx[cx] + hy[cy]))
					continue;

				for (var nd = 0; nd < 4; nd++)
				{
					if (dir < 4 && nd == (dir + 2) % 4)
						continue;
					var nx = cx + Dx[nd];
					var nyy = cy + Dy[nd];
					if ((uint)nx >= (uint)nxs || (uint)nyy >= (uint)ny)
						continue;
					var own = owner[(nx * ny) + nyy];
					if (own >= 0 && !(nx == tx && nyy == ty))
						continue;
					// a step's cost depends on the step and on whether it bends, not on how the search got here
					var bent = dir < 4 && dir != nd;
					var key = (((cell * 4) + nd) * 2) + (bent ? 1 : 0);
					double cost;
					ref var step = ref steps[key];
					if (step.Stamp == version)
						cost = step.Cost;
					else
					{
						cost = StepCost(cx, cy, nx, nyy, nd, dir, foreign, common);
						step = new StepEntry(cost, version);
					}

					if (nx == tx && nyy == ty && nd != endDir)
						cost += BendCost;
					var nxt = (((nx * ny) + nyy) * 5) + nd;
					var nd2 = cd + cost;
					ref var state = ref states[nxt];
					if (state.Stamp == version && state.Dist <= nd2)
						continue;
					state = new SearchState(nd2, version, cur);
					pq.Enqueue(nxt, nd2 + (hx[nx] + hy[nyy]));
				}
			}

			if (goal < 0)
				return null;
			var path = new List<LayoutPoint>();
			for (var s = goal; s >= 0; s = states[s].Prev)
			{
				var cell = s / 5;
				path.Add(new LayoutPoint(xs[cell / ny], ys[cell % ny]));
			}
			path.Reverse();
			path.Insert(0, p.SPort);
			path.Add(p.TPort);
			return path;
		}

		// v lies inside the step from a to b. A loop around the outside also counts a crossing that falls exactly on a grid
		// line (charged to the step arriving there): otherwise it threads through the drawing at grid points for free.
		private bool Passes(double v, double a, double b)
		{
			if (Math.Abs(v - b) < 0.5 && _countGridCrossings)
				return Math.Abs(v - a) >= 0.5;
			return v > Math.Min(a, b) + 0.5 && v < Math.Max(a, b) - 0.5;
		}

		private double Heuristic(int x, int y, int tx, int ty) => Math.Abs(_xs[x] - _xs[tx]) + Math.Abs(_ys[y] - _ys[ty]);

		private double StepCost(
			int x, int y, int nx, int ny, int nd, int dir,
			List<GroupBox> foreign, List<GroupBox> common)
		{
			double x0 = _xs[x], y0 = _ys[y], x1 = _xs[nx], y1 = _ys[ny];
			var len = Math.Abs(x1 - x0) + Math.Abs(y1 - y0);
			var cost = len * _lengthWeight;
			if (dir < 4 && dir != nd)
				cost += BendCost;
			var mx = (x0 + x1) / 2;
			var my = (y0 + y1) / 2;
			foreach (var g in foreign)
			{
				if (mx > g.X && mx < g.X + g.W && my > g.Y && my < g.Y + g.H)
					cost += ForeignGroupCost + (len * 2);
				// running alongside a foreign subgraph border reads as belonging to it
				else if (nd is 0 or 2 && mx > g.X && mx < g.X + g.W && (Math.Abs(my - (g.Y - Margin)) < 0.6 || Math.Abs(my - (g.Y + g.H + Margin)) < 0.6))
					cost += len * 0.6;
				else if (nd is 1 or 3 && my > g.Y && my < g.Y + g.H && (Math.Abs(mx - (g.X - Margin)) < 0.6 || Math.Abs(mx - (g.X + g.W + Margin)) < 0.6))
					cost += len * 0.6;
			}
			foreach (var g in common)
			{
				if (!(mx > g.X && mx < g.X + g.W && my > g.Y && my < g.Y + g.H))
					cost += LeaveGroupCost + len;
			}

			foreach (var g in _allGroups)
			{
				// keep lines off the subgraph's title text
				if (g.LabelW > 0 && mx > g.X && mx < g.X + g.LabelW && my > g.Y && my < g.Y + 30)
					cost += 90 + len;
			}

			var horizontal = nd is 0 or 2;
			// the parallel and crossing candidates, merged back into routed order
			var par = horizontal ? _parallelOnRow.Of(y) : _parallelOnColumn.Of(x);
			var cross = horizontal ? _crossingColumn.Of(Math.Min(x, nx)) : _crossingRow.Of(Math.Min(y, ny));
			int pi = 0, ci = 0;
			while (pi < par.Length || ci < cross.Length)
			{
				var s = _routed[ci >= cross.Length || (pi < par.Length && par[pi] < cross[ci]) ? par[pi++] : cross[ci++]];
				if (s.Horizontal == horizontal)
				{
					// a parallel run a few px away reads as the same line, so it costs as much as running right on top of it
					var offset = horizontal ? Math.Abs(s.Y0 - y0) : Math.Abs(s.X0 - x0);
					var shared = horizontal
						? Math.Min(Math.Max(s.X0, s.X1), Math.Max(x0, x1)) - Math.Max(Math.Min(s.X0, s.X1), Math.Min(x0, x1))
						: Math.Min(Math.Max(s.Y0, s.Y1), Math.Max(y0, y1)) - Math.Max(Math.Min(s.Y0, s.Y1), Math.Min(y0, y1));
					if (offset < RouteGap && shared > 0.5)
						cost += OverlapCost + len;
				}
				else if (horizontal)
				{
					// s is vertical
					if (Passes(s.X0, x0, x1) && y0 > Math.Min(s.Y0, s.Y1) + 0.5 && y0 < Math.Max(s.Y0, s.Y1) - 0.5)
						cost += _crossCost;
				}
				else if (Passes(s.Y0, y0, y1) && x0 > Math.Min(s.X0, s.X1) + 0.5 && x0 < Math.Max(s.X0, s.X1) - 0.5)
				{
					cost += _crossCost;
				}
			}
			return cost;
		}
	}

	// one search state (cell x incoming direction): distance, the search that wrote it, and where it came from, side by side so
	// relaxing a neighbour touches one cache line
	private readonly record struct SearchState(double Dist, int Stamp, int Prev);

	private readonly record struct StepEntry(double Cost, int Stamp);

	/// <summary>
	/// Search state for one grid, reused across renders: entries are only valid for the search whose stamp they carry, so a reused
	/// space needs no clearing. At most one (bounded) space is cached; a concurrent render takes a fresh one.
	/// </summary>
	private sealed class SearchSpace
	{
		private static SearchSpace? Cached;
		internal SearchState[] States = [];
		internal StepEntry[] Steps = [];
		private int _version;

		internal static SearchSpace Rent(int cells)
		{
			var space = Interlocked.Exchange(ref Cached, null) ?? new SearchSpace();
			if (space.States.Length < cells * 5)
			{
				// fresh arrays are zeroed: stamp 0 never matches a search, which starts at 1
				space.States = new SearchState[cells * 5];
				space.Steps = new StepEntry[cells * 8];
				space._version = 0;
			}
			return space;
		}

		// the space kept for the next render is bounded (about 3 MB); a bigger grid's space is left to the GC
		private const int MaxCachedCells = 16_000;

		internal static void Return(SearchSpace space)
		{
			if (space.States.Length <= MaxCachedCells * 5)
				Volatile.Write(ref Cached, space);
		}

		internal int NextVersion()
		{
			if (_version == int.MaxValue)
			{
				Array.Clear(States);
				Array.Clear(Steps);
				_version = 0;
			}
			return ++_version;
		}
	}

	/// <summary>Segment indices per bucket (compressed rows), each bucket in ascending segment order; reused across searches.</summary>
	private sealed class SegBuckets(int buckets)
	{
		private readonly int[] _start = new int[buckets + 1];
		private readonly int[] _fill = new int[buckets];
		private readonly List<(int Seg, int Lo, int Hi)> _spans = [];
		private int[] _items = new int[64];

		internal void Clear() => _spans.Clear();

		internal void Add(int seg, int lo, int hi)
		{
			if (lo <= hi)
				_spans.Add((seg, lo, hi));
		}

		internal void Build()
		{
			Array.Clear(_start);
			foreach (var (_, lo, hi) in _spans)
			{
				for (var b = lo; b <= hi; b++)
					_start[b + 1]++;
			}
			for (var b = 0; b < buckets; b++)
				_start[b + 1] += _start[b];
			if (_items.Length < _start[buckets])
				_items = new int[Math.Max(_start[buckets], _items.Length * 2)];
			Array.Copy(_start, _fill, buckets);
			foreach (var (seg, lo, hi) in _spans)
			{
				for (var b = lo; b <= hi; b++)
					_items[_fill[b]++] = seg;
			}
		}

		internal ReadOnlySpan<int> Of(int bucket) => _items.AsSpan(_start[bucket], _start[bucket + 1] - _start[bucket]);
	}

	/// <summary>
	/// The 4-ary min-heap of <see cref="PriorityQueue{TElement, TPriority}"/> with the same sift-up/sift-down rules, so equal
	/// priorities leave the queue in the same order; specialised to int states and double priorities (never NaN).
	/// </summary>
	private sealed class MinHeap
	{
		private double[] _priority = new double[256];
		private int[] _element = new int[256];
		private int _size;

		internal void Clear() => _size = 0;

		internal void Enqueue(int element, double priority)
		{
			if (_size == _priority.Length)
			{
				Array.Resize(ref _priority, _size * 2);
				Array.Resize(ref _element, _size * 2);
			}
			var i = _size++;
			while (i > 0)
			{
				var parent = (i - 1) >> 2;
				if (priority >= _priority[parent])
					break;
				_priority[i] = _priority[parent];
				_element[i] = _element[parent];
				i = parent;
			}
			_priority[i] = priority;
			_element[i] = element;
		}

		internal bool TryDequeue(out int element, out double priority)
		{
			if (_size == 0)
			{
				element = 0;
				priority = 0;
				return false;
			}
			element = _element[0];
			priority = _priority[0];
			var size = --_size;
			if (size == 0)
				return true;
			var pr = _priority;
			var el = _element;
			var nodePriority = pr[size];
			var nodeElement = el[size];
			var i = 0;
			int child;
			while ((child = (i << 2) + 1) < size)
			{
				// the first of the smallest children, picked with conditional selects: which child wins is data dependent,
				// so a branch on it mispredicts about every other time
				var min = child;
				var end = Math.Min(child + 4, size);
				for (var k = child + 1; k < end; k++)
					min += (k - min) & -(pr[k] < pr[min] ? 1 : 0);
				var minPriority = pr[min];
				if (nodePriority <= minPriority)
					break;
				pr[i] = minPriority;
				el[i] = el[min];
				i = min;
			}
			pr[i] = nodePriority;
			el[i] = nodeElement;
			return true;
		}
	}
}
