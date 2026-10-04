using Mermaider.Models;
using Mermaider.Rendering;
using Mermaider.Text;

namespace Mermaider.Tests.Snapshots.LayoutSkeleton;

/// <summary>
/// Geometry lint for laid-out flow diagrams (flowchart / state). It checks what a person sees when comparing against
/// mermaid.js: content outside the canvas, labels on top of lines/nodes/each other/arrowheads, edges running on top of
/// each other, lines through subgraph titles, unnecessary or tiny bends, non-uniform Z jogs, and unbalanced fans.
/// All coordinates are transformed so the flow always runs top-down (LR swaps axes, BT/RL mirror).
/// </summary>
internal static class VisualLint
{
	internal const string Canvas = "canvas";
	internal const string LabelOverlap = "label-overlap";
	internal const string LineOverlap = "line-overlap";
	internal const string TitleCross = "title-cross";
	internal const string ExtraBend = "extra-bend";
	internal const string TinyJog = "tiny-jog";
	internal const string JogUniformity = "jog-uniformity";
	internal const string Balance = "balance";

	internal static readonly string[] AllChecks = [Canvas, LabelOverlap, LineOverlap, TitleCross, ExtraBend, TinyJog, JogUniformity, Balance];

	internal sealed record Violation(string Check, string Detail);

	private sealed record Rect(double X0, double Y0, double X1, double Y1)
	{
		internal double Cx => (X0 + X1) / 2;
		internal double Cy => (Y0 + Y1) / 2;
		internal bool Overlaps(Rect o, double m = 0) => X0 < o.X1 - m && X1 > o.X0 + m && Y0 < o.Y1 - m && Y1 > o.Y0 + m;
	}

	private sealed record Route(PositionedEdge Edge, List<(double X, double Y)> Pts, Rect? Pill);

	private sealed record Box(string Id, Rect R);

	internal static List<Violation> Run(PositionedGraph g, Direction direction)
	{
		var t = Transform(direction);
		Rect Tr(double x0, double y0, double x1, double y1)
		{
			var (ax, ay) = t(x0, y0);
			var (bx, by) = t(x1, y1);
			return new Rect(Math.Min(ax, bx), Math.Min(ay, by), Math.Max(ax, bx), Math.Max(ay, by));
		}

		var nodes = g.Nodes.Select(n => new Box(n.Id, Tr(n.X, n.Y, n.X + n.Width, n.Y + n.Height))).ToList();
		var canvas = Tr(g.MinX, 0, g.Width, g.Height);
		var titles = new List<(string Id, Rect R, Rect Group)>();
		void Groups(IEnumerable<PositionedGroup> gs)
		{
			foreach (var gr in gs)
			{
				var titleW = Math.Min(gr.Width, 20 + (gr.Label.Length * 7.8));
				titles.Add((gr.Id, Tr(gr.X, gr.Y, gr.X + titleW, gr.Y + 28), Tr(gr.X, gr.Y, gr.X + gr.Width, gr.Y + gr.Height)));
				Groups(gr.Children);
			}
		}

		Groups(g.Groups);

		var routes = new List<Route>();
		foreach (var e in g.Edges.Where(e => e.Style != EdgeStyle.Invisible && e.Points.Count >= 2))
		{
			var pts = Simplify(e.Points.Select(p => t(p.X, p.Y)).ToList());
			Rect? pill = null;
			if (e.Label is { Length: > 0 })
			{
				var metrics = TextMetrics.MeasureMultiline(e.Label.AsSpan(), RenderConstants.FontSizes.EdgeLabel, RenderConstants.FontWeights.EdgeLabel);
				var w = ErSvgRenderer.LabelBoxWidth(metrics.Width);
				var h = metrics.Height + ErSvgRenderer.LabelPadY;
				var lp = e.LabelPosition ?? Mid(e.Points);
				pill = Tr(lp.X - (w / 2), lp.Y - (h / 2), lp.X + (w / 2), lp.Y + (h / 2));
			}

			routes.Add(new Route(e, pts, pill));
		}

		var found = new List<Violation>();
		CheckCanvas(found, canvas, nodes, routes, titles);
		CheckLabels(found, nodes, routes, titles);
		CheckOverlaps(found, routes, nodes);
		CheckTitles(found, routes, titles);
		CheckBends(found, routes, nodes);
		CheckBalance(found, routes, nodes);
		return found;
	}

	private static Func<double, double, (double, double)> Transform(Direction d) => d switch
	{
		Direction.LR => (x, y) => (y, x),
		Direction.RL => (x, y) => (y, -x),
		Direction.BT => (x, y) => (x, -y),
		_ => (x, y) => (x, y),
	};

	private static Point Mid(IReadOnlyList<Point> pts)
	{
		var total = 0.0;
		for (var i = 1; i < pts.Count; i++)
			total += Math.Abs(pts[i].X - pts[i - 1].X) + Math.Abs(pts[i].Y - pts[i - 1].Y);
		var half = total / 2;
		for (var i = 1; i < pts.Count; i++)
		{
			var len = Math.Abs(pts[i].X - pts[i - 1].X) + Math.Abs(pts[i].Y - pts[i - 1].Y);
			if (half <= len && len > 0)
			{
				var f = half / len;
				return new Point(pts[i - 1].X + ((pts[i].X - pts[i - 1].X) * f), pts[i - 1].Y + ((pts[i].Y - pts[i - 1].Y) * f));
			}

			half -= len;
		}

		return pts[0];
	}

	private static List<(double X, double Y)> Simplify(List<(double X, double Y)> pts)
	{
		var r = new List<(double X, double Y)>();
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

	private static bool SegHits(((double X, double Y) A, (double X, double Y) B) s, Rect r, double inset = 0)
	{
		var x0 = Math.Min(s.A.X, s.B.X);
		var x1 = Math.Max(s.A.X, s.B.X);
		var y0 = Math.Min(s.A.Y, s.B.Y);
		var y1 = Math.Max(s.A.Y, s.B.Y);
		return x1 > r.X0 + inset && x0 < r.X1 - inset && y1 > r.Y0 + inset && y0 < r.Y1 - inset;
	}

	private static IEnumerable<((double X, double Y) A, (double X, double Y) B)> Segs(List<(double X, double Y)> p)
	{
		for (var i = 0; i < p.Count - 1; i++)
			yield return (p[i], p[i + 1]);
	}

	// ---- 1. canvas ----------------------------------------------------------------------------------------

	private static void CheckCanvas(List<Violation> v, Rect canvas, List<Box> nodes, List<Route> routes, List<(string Id, Rect R, Rect Group)> titles)
	{
		const double tol = 0.5;
		bool Inside(Rect r) => r.X0 >= canvas.X0 - tol && r.X1 <= canvas.X1 + tol && r.Y0 >= canvas.Y0 - tol && r.Y1 <= canvas.Y1 + tol;
		foreach (var n in nodes.Where(n => !Inside(n.R)))
			v.Add(new Violation(Canvas, $"node {n.Id} outside canvas"));
		foreach (var r in routes)
		{
			if (r.Pts.Any(p => p.X < canvas.X0 - tol || p.X > canvas.X1 + tol || p.Y < canvas.Y0 - tol || p.Y > canvas.Y1 + tol))
				v.Add(new Violation(Canvas, $"edge {r.Edge.Source}->{r.Edge.Target} outside canvas"));
			if (r.Pill is { } pill && !Inside(pill))
				v.Add(new Violation(Canvas, $"label '{r.Edge.Label}' of {r.Edge.Source}->{r.Edge.Target} outside canvas"));
		}

		foreach (var t in titles.Where(t => !Inside(t.Group)))
			v.Add(new Violation(Canvas, $"subgraph {t.Id} outside canvas"));
	}

	// ---- 2. labels ----------------------------------------------------------------------------------------

	private static void CheckLabels(List<Violation> v, List<Box> nodes, List<Route> routes, List<(string Id, Rect R, Rect Group)> titles)
	{
		const double arrowZone = 14;
		for (var i = 0; i < routes.Count; i++)
		{
			if (routes[i].Pill is not { } pill)
				continue;
			var name = $"'{routes[i].Edge.Label}' on {routes[i].Edge.Source}->{routes[i].Edge.Target}";
			for (var j = i + 1; j < routes.Count; j++)
			{
				if (routes[j].Pill is { } other && pill.Overlaps(other, 1))
					v.Add(new Violation(LabelOverlap, $"{name} overlaps label '{routes[j].Edge.Label}'"));
			}

			foreach (var n in nodes.Where(n => pill.Overlaps(n.R, 1)))
				v.Add(new Violation(LabelOverlap, $"{name} overlaps node {n.Id}"));
			foreach (var t in titles.Where(t => pill.Overlaps(t.R, 1)))
				v.Add(new Violation(LabelOverlap, $"{name} overlaps title of {t.Id}"));
			foreach (var other in routes.Where(r => !ReferenceEquals(r, routes[i])))
			{
				if (Segs(other.Pts).Any(s => SegHits(s, pill, 1)))
					v.Add(new Violation(LabelOverlap, $"{name} sits on edge {other.Edge.Source}->{other.Edge.Target}"));
			}

			// the pill must not cover its own arrowhead / start marker
			var pts = routes[i].Pts;
			var end = pts[^1];
			var endZone = new Rect(end.X - arrowZone, end.Y - arrowZone, end.X + arrowZone, end.Y + arrowZone);
			if (pill.Overlaps(endZone, 1))
				v.Add(new Violation(LabelOverlap, $"{name} covers its own arrowhead"));
		}
	}

	// ---- 3. line overlap ----------------------------------------------------------------------------------

	private static void CheckOverlaps(List<Violation> v, List<Route> routes, List<Box> nodes)
	{
		const double shared = 28;
		for (var i = 0; i < routes.Count; i++)
		{
			for (var j = i + 1; j < routes.Count; j++)
			{
				var a = routes[i];
				var b = routes[j];
				var sameSource = a.Edge.Source == b.Edge.Source;
				var sameTarget = a.Edge.Target == b.Edge.Target;
				var hit = false;
				foreach (var sa in Segs(a.Pts))
				{
					foreach (var sb in Segs(b.Pts))
					{
						if (CollinearOverlap(sa, sb, out var from, out var to) <= 6)
							continue;
						// edges sharing an endpoint may share the stub next to it
						var nearSharedStart = sameSource && Dist(from, a.Pts[0]) <= shared && Dist(to, a.Pts[0]) <= shared;
						var nearSharedEnd = sameTarget && Dist(from, a.Pts[^1]) <= shared && Dist(to, a.Pts[^1]) <= shared;
						if (!nearSharedStart && !nearSharedEnd)
							hit = true;
					}
				}

				if (hit)
					v.Add(new Violation(LineOverlap, $"{a.Edge.Source}->{a.Edge.Target} runs on top of {b.Edge.Source}->{b.Edge.Target}"));
			}
		}
	}

	// parallel runs closer than this read as one thick line even when they are not exactly on top of each other
	private const double CrowdedGap = 7;

	private static double Dist((double X, double Y) a, (double X, double Y) b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

	private static double CollinearOverlap(((double X, double Y) A, (double X, double Y) B) s, ((double X, double Y) A, (double X, double Y) B) t, out (double X, double Y) from, out (double X, double Y) to)
	{
		from = default;
		to = default;
		if (Math.Abs(s.A.Y - s.B.Y) < 0.6 && Math.Abs(t.A.Y - t.B.Y) < 0.6 && Math.Abs(s.A.Y - t.A.Y) < CrowdedGap)
		{
			var lo = Math.Max(Math.Min(s.A.X, s.B.X), Math.Min(t.A.X, t.B.X));
			var hi = Math.Min(Math.Max(s.A.X, s.B.X), Math.Max(t.A.X, t.B.X));
			from = (lo, s.A.Y);
			to = (hi, s.A.Y);
			return hi - lo;
		}

		if (Math.Abs(s.A.X - s.B.X) < 0.6 && Math.Abs(t.A.X - t.B.X) < 0.6 && Math.Abs(s.A.X - t.A.X) < CrowdedGap)
		{
			var lo = Math.Max(Math.Min(s.A.Y, s.B.Y), Math.Min(t.A.Y, t.B.Y));
			var hi = Math.Min(Math.Max(s.A.Y, s.B.Y), Math.Max(t.A.Y, t.B.Y));
			from = (s.A.X, lo);
			to = (s.A.X, hi);
			return hi - lo;
		}

		return 0;
	}

	// ---- 4. titles ----------------------------------------------------------------------------------------

	private static void CheckTitles(List<Violation> v, List<Route> routes, List<(string Id, Rect R, Rect Group)> titles)
	{
		foreach (var r in routes)
		{
			foreach (var t in titles)
			{
				if (Segs(r.Pts).Any(s => SegHits(s, t.R, 2)))
					v.Add(new Violation(TitleCross, $"{r.Edge.Source}->{r.Edge.Target} crosses the title of {t.Id}"));
			}
		}
	}

	// ---- 5/6. bends ---------------------------------------------------------------------------------------

	private static void CheckBends(List<Violation> v, List<Route> routes, List<Box> nodes)
	{
		var byId = nodes.ToDictionary(n => n.Id, n => n.R, StringComparer.Ordinal);
		var zJogs = new List<(string Key, double Y, string Name)>();
		var outCount = routes.GroupBy(r => r.Edge.Source).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
		var inCount = routes.GroupBy(r => r.Edge.Target).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
		foreach (var r in routes)
		{
			var name = $"{r.Edge.Source}->{r.Edge.Target}";
			var bends = r.Pts.Count - 2;
			if (byId.TryGetValue(r.Edge.Source, out var s) && byId.TryGetValue(r.Edge.Target, out var t) && !ReferenceEquals(s, t))
			{
				var forward = t.Cy > s.Cy + 1;
				var overlapX = Math.Min(s.X1, t.X1) - Math.Max(s.X0, t.X0);
				// a lone edge between two overlapping boxes must be straight; fans and merges may use one uniform Z
				var lone = outCount.GetValueOrDefault(r.Edge.Source) == 1 && inCount.GetValueOrDefault(r.Edge.Target) == 1;
				var minBends = forward ? (overlapX >= 8 && lone ? 0 : 2) : 4;
				if (bends > minBends)
					v.Add(new Violation(ExtraBend, $"{name} has {bends} bends, {minBends} suffice"));

				if (bends == 2 && forward && r.Pts.Count == 4 && Math.Abs(r.Pts[1].Y - r.Pts[2].Y) < 0.6 && Math.Abs(r.Pts[0].X - r.Pts[1].X) < 0.6)
					zJogs.Add(($"{Math.Round(s.Cy / 8)}:{Math.Round(t.Cy / 8)}", r.Pts[1].Y, name));
			}

			for (var i = 1; i < r.Pts.Count - 2; i++)
			{
				var len = Dist(r.Pts[i], r.Pts[i + 1]);
				if (len < 12)
					v.Add(new Violation(TinyJog, $"{name} has a {len:0.#}px jog"));
			}
		}

		foreach (var grp in zJogs.GroupBy(z => z.Key).Where(g => g.Count() > 1))
		{
			var spread = grp.Max(z => z.Y) - grp.Min(z => z.Y);
			if (spread > 2)
				v.Add(new Violation(JogUniformity, $"Z-bends {string.Join(", ", grp.Select(z => z.Name))} jog at different heights ({spread:0.#}px apart)"));
		}
	}

	// ---- 7. balance ---------------------------------------------------------------------------------------

	private static void CheckBalance(List<Violation> v, List<Route> routes, List<Box> nodes)
	{
		var byId = nodes.ToDictionary(n => n.Id, n => n.R, StringComparer.Ordinal);
		var forward = routes
			.Where(r => byId.ContainsKey(r.Edge.Source) && byId.ContainsKey(r.Edge.Target) && byId[r.Edge.Target].Cy > byId[r.Edge.Source].Cy + 1)
			.Select(r => (From: r.Edge.Source, To: r.Edge.Target))
			.Distinct()
			.ToList();
		var parentsOf = forward.GroupBy(f => f.To).ToDictionary(g => g.Key, g => g.Select(x => x.From).ToList());
		foreach (var grp in forward.GroupBy(f => f.From))
		{
			var children = grp.Select(x => x.To).Where(c => parentsOf[c].Count == 1).ToList();
			if (children.Count == 0)
				continue;
			var parent = byId[grp.Key];
			var nextLayerY = children.Min(c => byId[c].Cy);
			var layer = children.Where(c => Math.Abs(byId[c].Cy - nextLayerY) < 4).ToList();
			if (layer.Count == 1 && grp.Count() == 1)
			{
				var dx = Math.Abs(byId[layer[0]].Cx - parent.Cx);
				if (dx > 2)
					v.Add(new Violation(Balance, $"{grp.Key} and its only child {layer[0]} are {dx:0.#}px off-axis"));
			}
			else if (layer.Count >= 2)
			{
				var centre = (layer.Min(c => byId[c].Cx) + layer.Max(c => byId[c].Cx)) / 2;
				var dx = Math.Abs(centre - parent.Cx);
				if (dx > 6)
					v.Add(new Violation(Balance, $"{grp.Key} is {dx:0.#}px off the centre of its children"));
			}
		}
	}
}
