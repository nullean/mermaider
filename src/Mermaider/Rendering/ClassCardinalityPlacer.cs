using Mermaider.Models;

namespace Mermaider.Rendering;

/// <summary>
/// Places class-diagram cardinalities as mermaid.js does: beside the line near the class they belong to, not on it, so they
/// never sit on top of the relation label at the line's midpoint. The two ends of a straight line take opposite sides; at a
/// bend the pill goes on the outside of the turn, clear of the next segment. Each candidate is checked against everything
/// already drawn (class boxes, relationship lines, relation labels and the cardinalities placed before it) and the first clear
/// one inside the canvas wins, so short edges and fanned-out ports stay readable. Obstacles are bucketed on a coarse grid to keep the check local.
/// </summary>
internal sealed class ClassCardinalityPlacer
{
	/// <summary>Room between the pill and the class box it belongs to.</summary>
	private const double EndGap = 6;

	/// <summary>Room between the pill and its own line: clears the end marker, which is 12px across.</summary>
	private const double SideGap = 8;

	/// <summary>Least room between the pill and anything else that is drawn.</summary>
	private const double Clearance = 3;

	private const double Step = 6;

	/// <summary>How many steps a pill may slide along its line, away from its end, before moving further from the line.</summary>
	private const int AlongSteps = 12;

	/// <summary>How many steps a pill may move away from its line.</summary>
	private const int PushSteps = 4;

	private const double CellSize = 64;

	private readonly double _width;
	private readonly double _height;
	private readonly List<Obstacle> _obstacles = [];
	private readonly Dictionary<(int X, int Y), List<int>> _cells = [];

	internal ClassCardinalityPlacer(PositionedClassDiagram diagram, DesignSystem ds)
	{
		_width = diagram.Width;
		_height = diagram.Height;
		foreach (var cls in diagram.Classes)
			Add(new Obstacle(cls.X, cls.Y, cls.X + cls.Width, cls.Y + cls.Height, null));

		foreach (var rel in diagram.Relationships)
		{
			for (var i = 1; i < rel.Points.Count; i++)
			{
				var (a, b) = (rel.Points[i - 1], rel.Points[i]);
				Add(new Obstacle(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), (a, b)));
			}

			if (rel.Label != null && rel.Points.Count >= 2)
			{
				var c = LabelCenter(rel);
				var (w, h) = ds.EdgeLabelSize(rel.Label);
				Add(new Obstacle(c.X - (w / 2), c.Y - (h / 2), c.X + (w / 2), c.Y + (h / 2), null));
			}
		}
	}

	/// <summary>Where the relation label of <paramref name="rel"/> is centred: the layout's label slot, else the path midpoint.</summary>
	internal static Point LabelCenter(PositionedClassRelationship rel) => rel.LabelPosition ?? VisualLanguage.PathMidpoint(rel.Points);

	/// <summary>
	/// Centre of a <paramref name="width"/> × <paramref name="height"/> cardinality at the start of <paramref name="points"/>, or at
	/// its end when <paramref name="atEnd"/>. The placed pill becomes an obstacle for the cardinalities placed after it.
	/// </summary>
	internal Point Place(IReadOnlyList<Point> points, bool atEnd, double width, double height)
	{
		// the path walked from this cardinality's own end
		Point At(int i) => atEnd ? points[points.Count - 1 - i] : points[i];

		var total = 0.0;
		for (var i = 1; i < points.Count; i++)
			total += Distance(points[i - 1], points[i]);

		var u0 = Direction(At(0), At(1));
		// default: the start of a line takes one side and its end the other, so two cardinalities on a short line never meet
		var sign = 1.0;
		if (points.Count > 2)
		{
			// at a bend, the outside of the turn: the next segment then runs away from the pill
			var turn = Direction(At(1), At(2));
			if ((turn.X * u0.Y) - (turn.Y * u0.X) > 1e-9)
				sign = -1;
		}

		var first = (Math.Abs(u0.X) * width / 2) + (Math.Abs(u0.Y) * height / 2) + EndGap;
		// the pill stays in its own half of the line, so it reads as this end's
		var half = Math.Max(first, total / 2);

		Point? fallback = null;
		for (var push = 0; push <= PushSteps; push++)
		{
			for (var k = 0; k <= AlongSteps; k++)
			{
				var along = first + (k * Step);
				if (k > 0 && along > half)
					break;
				var (p, u) = PointAlong(points, atEnd, along);
				var offset = (Math.Abs(u.Y) * width / 2) + (Math.Abs(u.X) * height / 2) + SideGap + (push * Step);
				foreach (var side in (ReadOnlySpan<double>)[sign, -sign])
				{
					var c = new Point(p.X + (side * u.Y * offset), p.Y - (side * u.X * offset));
					fallback ??= c;
					if (IsClear(c, width, height))
						return Commit(c, width, height);
				}
			}
		}

		return Commit(fallback ?? At(0), width, height);
	}

	private Point Commit(Point c, double width, double height)
	{
		Add(new Obstacle(c.X - (width / 2), c.Y - (height / 2), c.X + (width / 2), c.Y + (height / 2), null));
		return c;
	}

	/// <summary>The point <paramref name="along"/> px into the path from the chosen end, and the walking direction there.</summary>
	private static (Point Point, Point Direction) PointAlong(IReadOnlyList<Point> points, bool atEnd, double along)
	{
		Point At(int i) => atEnd ? points[points.Count - 1 - i] : points[i];

		var remaining = along;
		for (var i = 1; i < points.Count; i++)
		{
			var (a, b) = (At(i - 1), At(i));
			var len = Distance(a, b);
			if (len < 0.01)
				continue;
			var u = new Point((b.X - a.X) / len, (b.Y - a.Y) / len);
			if (remaining <= len || i == points.Count - 1)
				return (new Point(a.X + (u.X * remaining), a.Y + (u.Y * remaining)), u);
			remaining -= len;
		}

		return (At(0), Direction(At(0), At(1)));
	}

	private bool IsClear(Point c, double width, double height)
	{
		var x0 = c.X - (width / 2) - Clearance;
		var y0 = c.Y - (height / 2) - Clearance;
		var x1 = c.X + (width / 2) + Clearance;
		var y1 = c.Y + (height / 2) + Clearance;
		// inside the canvas, which the layout pads on every side
		if (x0 < 0 || y0 < 0 || x1 > _width || y1 > _height)
			return false;
		for (var cx = Cell(x0); cx <= Cell(x1); cx++)
		{
			for (var cy = Cell(y0); cy <= Cell(y1); cy++)
			{
				if (!_cells.TryGetValue((cx, cy), out var bucket))
					continue;
				foreach (var i in bucket)
				{
					var o = _obstacles[i];
					if (o.X1 < x0 || o.X0 > x1 || o.Y1 < y0 || o.Y0 > y1)
						continue;
					if (o.Segment is not { } s || SegmentHitsBox(s.A, s.B, x0, y0, x1, y1))
						return false;
				}
			}
		}

		return true;
	}

	private void Add(Obstacle o)
	{
		var index = _obstacles.Count;
		_obstacles.Add(o);
		for (var cx = Cell(o.X0); cx <= Cell(o.X1); cx++)
		{
			for (var cy = Cell(o.Y0); cy <= Cell(o.Y1); cy++)
			{
				if (!_cells.TryGetValue((cx, cy), out var bucket))
					_cells[(cx, cy)] = bucket = [];
				bucket.Add(index);
			}
		}
	}

	private static int Cell(double v) => (int)Math.Floor(v / CellSize);

	// Liang–Barsky: does segment a→b cross the box?
	private static bool SegmentHitsBox(Point a, Point b, double x0, double y0, double x1, double y1)
	{
		double t0 = 0, t1 = 1;
		var dx = b.X - a.X;
		var dy = b.Y - a.Y;
		return Clip(-dx, a.X - x0, ref t0, ref t1) && Clip(dx, x1 - a.X, ref t0, ref t1) && Clip(-dy, a.Y - y0, ref t0, ref t1) && Clip(
			dy,
			y1 - a.Y,
			ref t0,
			ref t1
		);
	}

	private static bool Clip(double p, double q, ref double t0, ref double t1)
	{
		if (p == 0)
			return q >= 0;
		var r = q / p;
		if (p < 0)
		{
			if (r > t1)
				return false;
			t0 = Math.Max(t0, r);
		}
		else
		{
			if (r < t0)
				return false;
			t1 = Math.Min(t1, r);
		}

		return true;
	}

	private static Point Direction(Point from, Point to)
	{
		var len = Distance(from, to);
		return len < 0.01 ? new Point(0, 1) : new Point((to.X - from.X) / len, (to.Y - from.Y) / len);
	}

	private static double Distance(Point a, Point b) => Math.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)));

	/// <summary>A drawn rectangle, or a line segment with its bounding box.</summary>
	private readonly record struct Obstacle(double X0, double Y0, double X1, double Y1, (Point A, Point B)? Segment);
}
