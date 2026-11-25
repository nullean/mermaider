using System.Globalization;

namespace Mermaider.Animate.Scene;

internal readonly record struct Vec(double X, double Y)
{
	public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y);
	public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y);
	public static Vec operator *(Vec a, double s) => new(a.X * s, a.Y * s);
	public double Length => Math.Sqrt((X * X) + (Y * Y));
}

internal readonly record struct Box(double X, double Y, double Width, double Height)
{
	public static readonly Box Empty = new(double.NaN, double.NaN, 0, 0);

	public bool IsEmpty => double.IsNaN(X);
	public double Right => X + Width;
	public double Bottom => Y + Height;
	public Vec Center => new(X + (Width / 2), Y + (Height / 2));

	public Box Union(Box other)
	{
		if (IsEmpty)
			return other;
		if (other.IsEmpty)
			return this;
		var x = Math.Min(X, other.X);
		var y = Math.Min(Y, other.Y);
		return new Box(x, y, Math.Max(Right, other.Right) - x, Math.Max(Bottom, other.Bottom) - y);
	}

	public Box Include(Vec p) => Union(new Box(p.X, p.Y, 0, 0));

	public Box Inflate(double by) => new(X - by, Y - by, Width + (2 * by), Height + (2 * by));

	/// <summary>Where the ray from the centre towards <paramref name="toward"/> leaves the box.</summary>
	public Vec Exit(Vec toward)
	{
		var c = Center;
		var d = toward - c;
		if (d.Length < 1e-9)
			return c;
		var tx = Math.Abs(d.X) < 1e-9 ? double.PositiveInfinity : Width / 2 / Math.Abs(d.X);
		var ty = Math.Abs(d.Y) < 1e-9 ? double.PositiveInfinity : Height / 2 / Math.Abs(d.Y);
		return c + (d * Math.Min(tx, ty));
	}
}

/// <summary>A flattened path: straight segments with their cumulative length, so a distance maps to a point.</summary>
internal sealed class Polyline
{
	private readonly double[] _cumulative;

	public Polyline(IReadOnlyList<Vec> points)
	{
		Points = points;
		_cumulative = new double[points.Count];
		for (var i = 1; i < points.Count; i++)
			_cumulative[i] = _cumulative[i - 1] + (points[i] - points[i - 1]).Length;
	}

	public IReadOnlyList<Vec> Points { get; }

	public double Length => _cumulative.Length == 0 ? 0 : _cumulative[^1];

	public Vec Start => Points[0];
	public Vec End => Points[^1];

	public Polyline Reversed()
	{
		var reversed = new List<Vec>(Points);
		reversed.Reverse();
		return new Polyline(reversed);
	}

	/// <summary>The point at a fraction (0–1) of the length.</summary>
	public Vec At(double fraction)
	{
		if (Points.Count == 1 || Length <= 0)
			return Points[0];
		var distance = Math.Max(0, Math.Min(1, fraction)) * Length;
		var i = 1;
		while (i < Points.Count - 1 && _cumulative[i] < distance)
			i++;
		var segment = _cumulative[i] - _cumulative[i - 1];
		var t = segment <= 0 ? 0 : (distance - _cumulative[i - 1]) / segment;
		return Points[i - 1] + ((Points[i] - Points[i - 1]) * t);
	}

	/// <summary>The fractions (0–1) of the length at which the path turns a corner.</summary>
	public IEnumerable<double> CornerFractions()
	{
		if (Length <= 0)
			yield break;
		for (var i = 1; i < Points.Count - 1; i++)
			yield return _cumulative[i] / Length;
	}

	/// <summary>The direction of travel at the end, in degrees, for an arrowhead.</summary>
	public double EndAngle()
	{
		for (var i = Points.Count - 1; i > 0; i--)
		{
			var d = Points[i] - Points[i - 1];
			if (d.Length > 1e-6)
				return Math.Atan2(d.Y, d.X) * 180 / Math.PI;
		}
		return 0;
	}

	public double StartAngle()
	{
		for (var i = 1; i < Points.Count; i++)
		{
			var d = Points[i - 1] - Points[i];
			if (d.Length > 1e-6)
				return Math.Atan2(d.Y, d.X) * 180 / Math.PI;
		}
		return 180;
	}

	public string ToPathData()
	{
		var sb = new System.Text.StringBuilder();
		for (var i = 0; i < Points.Count; i++)
			_ = sb.Append(i == 0 ? "M" : " L").Append(Num.F(Points[i].X)).Append(',').Append(Num.F(Points[i].Y));
		return sb.ToString();
	}
}

/// <summary>Reads the absolute path commands the renderers write (M L H V Q C Z, and their relative forms) into points.</summary>
internal static class PathData
{
	private const int CurveSamples = 8;

	internal static List<Vec> Flatten(string d)
	{
		var points = new List<Vec>();
		var numbers = new List<double>();
		var current = new Vec(0, 0);
		var start = current;
		var i = 0;
		var command = 'M';
		while (i < d.Length)
		{
			var c = d[i];
			if (char.IsLetter(c))
			{
				command = c;
				i++;
				if (c is 'Z' or 'z')
				{
					current = start;
					points.Add(current);
				}
				continue;
			}
			if (char.IsWhiteSpace(c) || c == ',')
			{
				i++;
				continue;
			}
			numbers.Clear();
			i = ReadNumbers(d, i, numbers, Arity(command));
			if (numbers.Count < Arity(command))
				break;
			var relative = char.IsLower(command);

			Vec P(int k) => relative ? current + new Vec(numbers[k], numbers[k + 1]) : new Vec(numbers[k], numbers[k + 1]);

			switch (char.ToUpperInvariant(command))
			{
				case 'M':
					current = P(0);
					start = current;
					points.Add(current);
					command = relative ? 'l' : 'L';
					break;
				case 'L':
					current = P(0);
					points.Add(current);
					break;
				case 'H':
					current = new Vec(relative ? current.X + numbers[0] : numbers[0], current.Y);
					points.Add(current);
					break;
				case 'V':
					current = new Vec(current.X, relative ? current.Y + numbers[0] : numbers[0]);
					points.Add(current);
					break;
				case 'Q':
					{
						var c1 = P(0);
						var end = P(2);
						for (var s = 1; s <= CurveSamples; s++)
						{
							var t = (double)s / CurveSamples;
							var u = 1 - t;
							points.Add((current * (u * u)) + (c1 * (2 * u * t)) + (end * (t * t)));
						}
						current = end;
						break;
					}
				case 'C':
					{
						var c1 = P(0);
						var c2 = P(2);
						var end = P(4);
						for (var s = 1; s <= CurveSamples; s++)
						{
							var t = (double)s / CurveSamples;
							var u = 1 - t;
							points.Add((current * (u * u * u)) + (c1 * (3 * u * u * t)) + (c2 * (3 * u * t * t)) + (end * (t * t * t)));
						}
						current = end;
						break;
					}
				default:
					// arcs and anything else: jump to the end point; renderers do not draw edges with them
					current = new Vec(numbers[^2], numbers[^1]);
					points.Add(current);
					break;
			}
		}
		return points;
	}

	private static int Arity(char command) => char.ToUpperInvariant(command) switch
	{
		'H' or 'V' => 1,
		'Q' => 4,
		'C' => 6,
		'A' => 7,
		_ => 2,
	};

	private static int ReadNumbers(string d, int i, List<double> numbers, int count)
	{
		while (numbers.Count < count && i < d.Length)
		{
			while (i < d.Length && (char.IsWhiteSpace(d[i]) || d[i] == ','))
				i++;
			var begin = i;
			if (i < d.Length && (d[i] == '-' || d[i] == '+'))
				i++;
			var seenDot = false;
			while (i < d.Length && (char.IsDigit(d[i]) || (d[i] == '.' && !seenDot)))
			{
				seenDot |= d[i] == '.';
				i++;
			}
			if (i < d.Length && (d[i] == 'e' || d[i] == 'E'))
			{
				i++;
				if (i < d.Length && (d[i] == '-' || d[i] == '+'))
					i++;
				while (i < d.Length && char.IsDigit(d[i]))
					i++;
			}
			if (i == begin)
				break;
			var text = d[begin..i];
			numbers.Add(double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture));
		}
		return i;
	}
}

/// <summary>Invariant, compact number formatting for SVG and CSS.</summary>
internal static class Num
{
	internal static string F(double value) => Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);

	internal static string F3(double value) => Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);

	internal static double Parse(string? value) =>
		value is not null && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
}
