namespace Sugiyama.Internal;

/// <summary>
/// Obstacle-aware orthogonal router for compound flowcharts. Works on the final absolute node/subgraph boxes:
/// ports are spread along the side facing the other end, a short stub leaves each port, and the stub ends are joined by
/// an A* search on a grid built from box borders (cost: length, bends, crossings, overlap with earlier routes, and a very
/// heavy cost for crossing subgraphs the edge does not belong to).
/// </summary>
internal static class FlowEdgeRouter
{
	internal sealed record Box(string Id, double X, double Y, double W, double H, bool CentrePorts)
	{
		internal double Right => X + W;
		internal double Bottom => Y + H;
		internal double Cx => X + (W / 2);
		internal double Cy => Y + (H / 2);
	}

	internal sealed record GroupBox(string Id, double X, double Y, double W, double H, HashSet<string> NodeIds);

	internal sealed record RouteEdge(int Index, string Source, string Target, double LabelW, double LabelH);

	private const double Stub = 22;
	private const double Margin = 8;
	private const double BendCost = 28;
	private const double CrossCost = 55;
	private const double OverlapCost = 40;
	private const double ForeignGroupCost = 450;
	private const double LeaveGroupCost = 45;

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

			if (ReferenceEquals(s, t))
			{
				results[e.Index] = SelfLoop(e, s);
				continue;
			}

			var (ss, ts) = ChooseSides(s, t, vertical);
			plans.Add(new Plan { Edge = e, S = s, T = t, SSide = ss, TSide = ts });
		}

		AssignPorts(plans);

		var grid = new Grid(boxes, groups, plans);
		var routed = new List<Seg>();
		var placedLabels = new List<(double X0, double Y0, double X1, double Y1)>();
		var polylines = new Dictionary<int, List<LayoutPoint>>();

		foreach (var p in plans.OrderBy(p => Math.Abs(p.SPort.X - p.TPort.X) + Math.Abs(p.SPort.Y - p.TPort.Y)))
		{
			var pts = grid.Find(p, groups, routed) ?? Fallback(p);
			pts = Simplify(pts);
			polylines[p.Edge.Index] = pts;
			for (var i = 0; i < pts.Count - 1; i++)
				routed.Add(new Seg(pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y));
		}

		foreach (var p in plans)
		{
			var pts = polylines[p.Edge.Index];
			var label = p.Edge.LabelW > 0 ? PlaceLabel(pts, p.Edge, boxes, placedLabels, routed) : null;
			results[p.Edge.Index] = new LayoutEdgeResult(p.Edge.Index, pts, label);
		}

		return results.Where(r => r is not null).Select(r => r!).ToList();
	}

	private static (int SSide, int TSide) ChooseSides(Box s, Box t, bool vertical)
	{
		var dx = t.Cx - s.Cx;
		var dy = t.Cy - s.Cy;
		var gapY = Math.Max(t.Y - s.Bottom, s.Y - t.Bottom);
		var gapX = Math.Max(t.X - s.Right, s.X - t.Right);
		var useVertical = vertical ? gapY >= 0 || gapX < 0 : gapX < 0 && gapY >= 0;
		return useVertical
			? (dy >= 0 ? 1 : 3, dy >= 0 ? 3 : 1)
			: (dx >= 0 ? 0 : 2, dx >= 0 ? 2 : 0);
	}

	private static void AssignPorts(List<Plan> plans)
	{
		var groups = new Dictionary<(string, int), List<(Plan Plan, bool IsSource, double Key)>>();
		void Add(Box b, int side, Plan p, bool src, Box other)
		{
			var key = side is 1 or 3 ? other.Cx : other.Cy;
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
			var b = plans.SelectMany(p => new[] { p.S, p.T }).First(x => x.Id == id);
			var sorted = list.OrderBy(i => i.Key).ThenBy(i => i.Plan.Edge.Index).ToList();
			for (var i = 0; i < sorted.Count; i++)
			{
				var f = b.CentrePorts ? 0.5 : (i + 1.0) / (sorted.Count + 1);
				var pt = side switch
				{
					0 => new LayoutPoint(b.Right, Round(b.Y + (b.H * f))),
					2 => new LayoutPoint(b.X, Round(b.Y + (b.H * f))),
					1 => new LayoutPoint(Round(b.X + (b.W * f)), b.Bottom),
					_ => new LayoutPoint(Round(b.X + (b.W * f)), b.Y),
				};
				if (sorted[i].IsSource)
					sorted[i].Plan.SPort = pt;
				else
					sorted[i].Plan.TPort = pt;
			}
		}
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
		var a = Out(p.SPort, p.SSide, Stub);
		var b = Out(p.TPort, p.TSide, Stub);
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
		List<LayoutPoint> pts, RouteEdge e, IReadOnlyList<Box> boxes,
		List<(double X0, double Y0, double X1, double Y1)> placed, List<Seg> routed)
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
		foreach (var (i, len) in segs.OrderByDescending(s => s.Len).Take(3))
		{
			foreach (var f in new[] { 0.5, 0.35, 0.65, 0.25, 0.75 })
			{
				var cx = pts[i].X + ((pts[i + 1].X - pts[i].X) * f);
				var cy = pts[i].Y + ((pts[i + 1].Y - pts[i].Y) * f);
				var cost = 0.0;
				cost += (1 - (len / Math.Max(1, segs.Max(s => s.Len)))) * 30;
				cost += Math.Abs(f - 0.5) * 20;
				var r = (X0: cx - hw, Y0: cy - hh, X1: cx + hw, Y1: cy + hh);
				foreach (var b in boxes)
				{
					if (r.X0 < b.Right && r.X1 > b.X && r.Y0 < b.Bottom && r.Y1 > b.Y)
						cost += 1000;
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
		private readonly double[] _dist;
		private readonly int[] _stamp;
		private readonly int[] _prev;
		private int _version;

		internal Grid(IReadOnlyList<Box> boxes, IReadOnlyList<GroupBox> groups, List<Plan> plans)
		{
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
				foreach (var (port, side) in new[] { (p.SPort, p.SSide), (p.TPort, p.TSide) })
				{
					var o = Out(port, side, Stub);
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
				var b = boxes[bi];
				for (var ix = 0; ix < _xs.Length; ix++)
				{
					if (_xs[ix] <= b.X - Margin + 0.01 || _xs[ix] >= b.Right + Margin - 0.01)
						continue;
					for (var iy = 0; iy < _ys.Length; iy++)
					{
						if (_ys[iy] > b.Y - Margin + 0.01 && _ys[iy] < b.Bottom + Margin - 0.01)
							_owner[(ix * _ys.Length) + iy] = bi;
					}
				}
			}

			_dist = new double[n * 5];
			_stamp = new int[n * 5];
			_prev = new int[n * 5];
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
				if (gap >= 44)
				{
					r.Add(mid - 10);
					r.Add(mid + 10);
				}
			}
			return r;
		}

		private static double[] Dedupe(List<double> v)
		{
			var s = v.Select(x => Math.Round(x * 2) / 2).Distinct().OrderBy(x => x).ToList();
			return [.. s];
		}

		private int IndexOf(double[] a, double v)
		{
			var i = Array.BinarySearch(a, Math.Round(v * 2) / 2);
			return i >= 0 ? i : -1;
		}

		internal List<LayoutPoint>? Find(Plan p, IReadOnlyList<GroupBox> groups, List<Seg> routed)
		{
			var a = Out(p.SPort, p.SSide, Stub);
			var b = Out(p.TPort, p.TSide, Stub);
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
				var hasS = g.NodeIds.Contains(p.S.Id);
				var hasT = g.NodeIds.Contains(p.T.Id);
				if (hasS && hasT)
					common.Add(g);
				else if (!hasS && !hasT)
					foreign.Add(g);
			}

			_version++;
			var ny = _ys.Length;
			var pq = new PriorityQueue<int, double>();
			var startDir = p.SSide;
			var start = (((sx * ny) + sy) * 5) + startDir;
			_dist[start] = 0;
			_stamp[start] = _version;
			_prev[start] = -1;
			pq.Enqueue(start, Heuristic(sx, sy, tx, ty));
			var endDir = (p.TSide + 2) % 4; // direction of the last move into the end stub
			var pops = 0;
			var goal = -1;
			while (pq.TryDequeue(out var cur, out _))
			{
				if (++pops > 400_000)
					break;
				var dir = cur % 5;
				var cell = cur / 5;
				var cx = cell / ny;
				var cy = cell % ny;
				var cd = _dist[cur];
				if (cx == tx && cy == ty)
				{
					goal = cur;
					break;
				}

				for (var nd = 0; nd < 4; nd++)
				{
					if (dir < 4 && nd == (dir + 2) % 4)
						continue;
					var nx = cx + Dx[nd];
					var nyy = cy + Dy[nd];
					if (nx < 0 || nx >= _xs.Length || nyy < 0 || nyy >= ny)
						continue;
					var own = _owner[(nx * ny) + nyy];
					if (own >= 0 && !(nx == tx && nyy == ty))
						continue;
					var cost = StepCost(cx, cy, nx, nyy, nd, dir, foreign, common, routed);
					if (nx == tx && nyy == ty && nd != endDir)
						cost += BendCost;
					var nxt = (((nx * ny) + nyy) * 5) + nd;
					var nd2 = cd + cost;
					if (_stamp[nxt] == _version && _dist[nxt] <= nd2)
						continue;
					_stamp[nxt] = _version;
					_dist[nxt] = nd2;
					_prev[nxt] = cur;
					pq.Enqueue(nxt, nd2 + Heuristic(nx, nyy, tx, ty));
				}
			}

			if (goal < 0)
				return null;
			var path = new List<LayoutPoint>();
			for (var s = goal; s >= 0; s = _prev[s])
			{
				var cell = s / 5;
				path.Add(new LayoutPoint(_xs[cell / ny], _ys[cell % ny]));
			}
			path.Reverse();
			path.Insert(0, p.SPort);
			path.Add(p.TPort);
			return path;
		}

		private double Heuristic(int x, int y, int tx, int ty) => Math.Abs(_xs[x] - _xs[tx]) + Math.Abs(_ys[y] - _ys[ty]);

		private double StepCost(
			int x, int y, int nx, int ny, int nd, int dir,
			List<GroupBox> foreign, List<GroupBox> common, List<Seg> routed)
		{
			double x0 = _xs[x], y0 = _ys[y], x1 = _xs[nx], y1 = _ys[ny];
			var len = Math.Abs(x1 - x0) + Math.Abs(y1 - y0);
			var cost = len;
			if (dir < 4 && dir != nd)
				cost += BendCost;
			var mx = (x0 + x1) / 2;
			var my = (y0 + y1) / 2;
			foreach (var g in foreign)
			{
				if (mx > g.X && mx < g.X + g.W && my > g.Y && my < g.Y + g.H)
					cost += ForeignGroupCost + (len * 2);
			}
			foreach (var g in common)
			{
				if (!(mx > g.X && mx < g.X + g.W && my > g.Y && my < g.Y + g.H))
					cost += LeaveGroupCost + len;
			}

			var horizontal = nd is 0 or 2;
			foreach (var s in routed)
			{
				if (s.Horizontal == horizontal)
				{
					if (horizontal && Math.Abs(s.Y0 - y0) < 0.5 && Math.Min(s.X0, s.X1) < Math.Max(x0, x1) - 0.5 && Math.Max(s.X0, s.X1) > Math.Min(x0, x1) + 0.5)
						cost += OverlapCost + len;
					else if (!horizontal && Math.Abs(s.X0 - x0) < 0.5 && Math.Min(s.Y0, s.Y1) < Math.Max(y0, y1) - 0.5 && Math.Max(s.Y0, s.Y1) > Math.Min(y0, y1) + 0.5)
						cost += OverlapCost + len;
				}
				else if (horizontal)
				{
					// s is vertical
					if (s.X0 > Math.Min(x0, x1) + 0.5 && s.X0 < Math.Max(x0, x1) - 0.5 && y0 > Math.Min(s.Y0, s.Y1) + 0.5 && y0 < Math.Max(s.Y0, s.Y1) - 0.5)
						cost += CrossCost;
				}
				else if (s.Y0 > Math.Min(y0, y1) + 0.5 && s.Y0 < Math.Max(y0, y1) - 0.5 && x0 > Math.Min(s.X0, s.X1) + 0.5 && x0 < Math.Max(s.X0, s.X1) - 0.5)
				{
					cost += CrossCost;
				}
			}
			return cost;
		}
	}
}
