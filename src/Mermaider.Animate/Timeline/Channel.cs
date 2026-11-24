using System.Xml.Linq;
using Mermaider.Animate.Scene;

namespace Mermaider.Animate.Timeline;

/// <summary>The only properties an animation may change: they cost the browser no layout and no paint of their own.</summary>
internal enum Property
{
	Opacity,

	/// <summary>translate(x, y) scale(s), as three values.</summary>
	Transform,

	DashOffset,
}

/// <summary>What a channel does under <c>prefers-reduced-motion</c>.</summary>
internal enum ReducedMotion
{
	/// <summary>Content still changes, as cuts at the moment each change starts.</summary>
	Cut,

	/// <summary>Not animated at all: movement such as travel, pulses and rises.</summary>
	Off,
}

internal sealed record Segment(double Start, double End, double[] From, double[] To, Easing Ease);

/// <summary>One property of one element over the whole timeline: a start value and the changes to it.</summary>
internal sealed class Channel(XElement element, Property property, double[] initial, ReducedMotion reduced)
{
	public XElement Element { get; } = element;
	public Property Property { get; } = property;
	public double[] Initial { get; } = initial;
	public ReducedMotion Reduced { get; } = reduced;
	public List<Segment> Segments { get; } = [];

	/// <summary>For a transform: the point it scales around, in user units.</summary>
	public Vec Origin { get; set; }

	public bool IsAnimated => Segments.Count > 0;

	/// <summary>
	/// Changes the value to <paramref name="to"/> from <paramref name="start"/> over <paramref name="duration"/>. A change
	/// that starts before an earlier one has finished cuts the earlier one short where it stands, so the last instruction
	/// wins and the value never jumps.
	/// </summary>
	public void Animate(double start, double duration, double[] to, Easing ease)
	{
		duration = Math.Max(1, duration);
		for (var i = Segments.Count - 1; i >= 0; i--)
		{
			var segment = Segments[i];
			if (segment.Start >= start)
				Segments.RemoveAt(i);
			else if (segment.End > start)
				Segments[i] = segment with { End = start, To = ValueAt(segment, start) };
		}
		Segments.Add(new Segment(start, start + duration, ValueAt(start), to, ease));
	}

	/// <summary>Sets the value at <paramref name="time"/> without a visible transition.</summary>
	public void Set(double time, double[] to) => Animate(time, 1, to, Easing.Linear);

	public double[] ValueAt(double time)
	{
		var value = Initial;
		foreach (var segment in Segments)
		{
			if (time < segment.Start)
				break;
			value = time >= segment.End ? segment.To : ValueAt(segment, time);
		}
		return value;
	}

	public double[] Final => Segments.Count == 0 ? Initial : Segments[^1].To;

	private static double[] ValueAt(Segment segment, double time)
	{
		var t = segment.End <= segment.Start ? 1 : (time - segment.Start) / (segment.End - segment.Start);
		var p = segment.Ease.Evaluate(t);
		var result = new double[segment.From.Length];
		for (var i = 0; i < result.Length; i++)
			result[i] = segment.From[i] + ((segment.To[i] - segment.From[i]) * p);
		return result;
	}

	public string Css(double[] value) => Property switch
	{
		Property.Opacity => $"opacity:{Num.F3(value[0])}",
		Property.DashOffset => $"stroke-dashoffset:{Num.F3(value[0])}",
		_ => $"transform:translate({Num.F(value[0])}px,{Num.F(value[1])}px) scale({Num.F3(value[2])})",
	};

	public static double[] Value(double v) => [v];

	public static double[] Transform(double x, double y, double scale) => [x, y, scale];
}
