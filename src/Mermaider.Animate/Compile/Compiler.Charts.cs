using System.Xml.Linq;
using Mermaider.Animate.Scene;
using Mermaider.Animate.Score;
using Mermaider.Animate.Timeline;

namespace Mermaider.Animate.Compile;

/// <summary>
/// Charts: bars grow from their baseline and lines draw through their points, pie slices sweep round, radar curves grow
/// out of the centre, and a sankey's flows run left to right. <c>set</c> changes a bar's value with the number shown;
/// <c>trace</c> lights every flow between two sankey nodes.
/// </summary>
internal sealed partial class Compiler
{
	private readonly Dictionary<SceneTarget, XElement> _values = [];

	private double BuildChart(double at) => _scene.Kind switch
	{
		SceneKind.XyChart => GrowBars(at),
		SceneKind.Pie => SweepPie(at),
		SceneKind.Radar => GrowCurves(at),
		_ => at,
	};

	/// <summary>The bar's scale channel: it grows from its baseline, so the origin is its base edge.</summary>
	private Channel BarScale(SceneTarget bar)
	{
		var box = bar.Box;
		var origin = _scene.Upright
			? (bar.Value >= 0 ? new Vec(box.X, box.Bottom) : new Vec(box.X, box.Y))
			: (bar.Value >= 0 ? new Vec(box.X, box.Y) : new Vec(box.Right, box.Y));
		var channel = ChannelFor(Outer(bar.Elements[0]), Property.Transform, Channel.Transform(0, 0, 1, 1), ReducedMotion.Cut);
		channel.Origin = new Vec(origin.X - _stage.X, origin.Y - _stage.Y);
		return channel;
	}

	private static double[] Scaled(bool upright, double s) => upright ? Channel.Transform(0, 0, 1, s) : Channel.Transform(0, 0, s, 1);

	/// <summary>Each series' bars grow from the baseline one after another, then each line draws through its points.</summary>
	private double GrowBars(double at)
	{
		var t = at;
		var series = _scene.Targets.Where(b => b.Mark is not null).GroupBy(b => b.Id.Contains('.') ? b.Id[(b.Id.IndexOf('.') + 1)..] : "").ToList();
		foreach (var group in series)
		{
			var bars = group.OrderBy(b => _scene.Upright ? b.Box.X : b.Box.Y).ToList();
			var end = t;
			for (var i = 0; i < bars.Count; i++)
			{
				var scale = BarScale(bars[i]);
				if (!scale.IsAnimated)
				{
					scale.Initial[2] = _scene.Upright ? 1 : 0;
					scale.Initial[3] = _scene.Upright ? 0 : 1;
				}
				var start = t + (i * _spec.Stagger);
				scale.Animate(start, _spec.Enter * 1.4, Scaled(_scene.Upright, 1), Easing.Enter);
				end = Math.Max(end, start + (_spec.Enter * 1.4));
			}
			t = end - (_spec.Enter * 0.5);
		}
		foreach (var (line, path, points) in _scene.Lines)
		{
			var edge = new SceneEdge("line", "line", line, path) { Stroke = line };
			var duration = Math.Max(_spec.DrawMin, _spec.DrawTime(path.Length) * 1.5);
			var visual = Drawable(edge, 14);
			var ink = Ink(visual);
			ink.Initial[0] = 1;
			ink.Animate(t, duration, Channel.Value(0), Easing.Linear);
			// a point lands as the line reaches it
			foreach (var point in points)
			{
				var x = SvgScene.BoxOf(point).Center.X;
				var reach = path.Length <= 0 ? 0 : Math.Max(0, Math.Min(1, (x - path.Start.X) / Math.Max(1, path.End.X - path.Start.X)));
				var opacity = Opacity(Outer(point), ReducedMotion.Cut, 0);
				opacity.Animate(t + (duration * reach), _spec.Shift * 0.6, Channel.Value(1), Easing.Enter);
			}
			t += duration;
		}
		return t;
	}

	/// <summary>
	/// A bar takes a new value: it grows or shrinks from its baseline, and the number appears at its new top. The value
	/// stays until set again.
	/// </summary>
	private double Set(SetStatement set, double at)
	{
		if (_scene.Kind != SceneKind.XyChart)
		{
			_diagnostics.Add(new(set.Line, "set changes a bar's value in an xy chart."));
			return at;
		}
		if (Resolve([set.Target]).FirstOrDefault() is not { } bar || bar.Mark is null || bar.Value == 0)
			return at;
		var scale = BarScale(bar);
		scale.Animate(at, _spec.Camera, Scaled(_scene.Upright, set.Value / bar.Value), Easing.Move);

		var box = bar.Box;
		var length = (_scene.Upright ? box.Height : box.Width) * set.Value / bar.Value;
		var tip = _scene.Upright ? new Vec(box.Center.X, box.Bottom - length - 14) : new Vec(box.X + length + 24, box.Center.Y);
		var text = set.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
		var label = Overlay.Label(text, "var(--accent, #3b82f6)", 0);
		label.SetAttributeValue("transform", $"translate({Num.F(tip.X)} {Num.F(tip.Y)})");
		var holder = new XElement(SvgScene.Ns + "g", new XAttribute("opacity", "0"), label);
		_overlay.Add(holder);
		if (_values.TryGetValue(bar, out var previous))
			Opacity(previous, ReducedMotion.Cut, 0).Animate(at, _spec.Exit, Channel.Value(0), Easing.Exit);
		Opacity(holder, ReducedMotion.Cut, 0).Animate(at + (_spec.Camera * 0.6), _spec.Enter, Channel.Value(1), Easing.Enter);
		_values[bar] = holder;
		return at + _spec.Camera;
	}

	/// <summary>Slices sweep in clockwise, each through a mask along its own arc; its percentage lands with it.</summary>
	private double SweepPie(double at)
	{
		var t = at;
		foreach (var slice in _scene.Ordered)
		{
			var points = new List<Vec>();
			var span = Math.Max(1, slice.EndAngle - slice.StartAngle);
			var steps = Math.Max(4, (int)Math.Ceiling(span / 6));
			for (var i = 0; i <= steps; i++)
			{
				var angle = (slice.StartAngle + (span * i / steps)) * Math.PI / 180;
				points.Add(new Vec(slice.Center.X + (slice.Radius / 2 * Math.Cos(angle)), slice.Center.Y + (slice.Radius / 2 * Math.Sin(angle))));
			}
			var edge = new SceneEdge(slice.Id, slice.Id, slice.Elements[0], new Polyline(points)) { Stroke = slice.Elements[0], MaskWidth = slice.Radius + 8 };
			edge.Labels.AddRange(slice.Followers.SelectMany(f => f.Elements));
			t = Draw(edge, t);
		}
		return t;
	}

	/// <summary>Curves grow out of the centre one after another.</summary>
	private double GrowCurves(double at)
	{
		var t = at;
		foreach (var curve in _scene.Ordered)
		{
			var growth = Movement(curve.Elements[0], curve.Center);
			if (!growth.IsAnimated)
			{
				growth.Initial[2] = 0;
				growth.Initial[3] = 0;
			}
			growth.Animate(t, _spec.Enter * 1.6, Channel.Transform(0, 0, 1), Easing.Enter);
			t += _spec.Enter * 1.1;
		}
		return t + (_spec.Enter * 0.5);
	}

	/// <summary>Every flow on a way from the first node to the last is lit; the rest dims for the step.</summary>
	private double Trace(TraceStatement trace, double at)
	{
		var stops = trace.Path.Select(id => ResolveNode(id, trace.Line)).ToList();
		if (stops.Any(s => s is null))
			return at;
		var keep = new HashSet<string>(StringComparer.Ordinal);
		for (var i = 0; i + 1 < stops.Count; i++)
		{
			var on = Between(stops[i]!.Id, stops[i + 1]!.Id);
			if (on.Count == 0)
			{
				_diagnostics.Add(new(trace.Line, $"Nothing flows from {stops[i]!.Id} to {stops[i + 1]!.Id}."));
				return at;
			}
			keep.UnionWith(on);
		}
		// focus keeps the flows between the kept nodes; ribbons are already the colour of what they carry
		var end = Focus([.. _scene.Targets.Where(t => keep.Contains(t.Id))], close: false, at);
		return Math.Max(end, Pulse(stops[^1]!, end, _spec.PulseScale));
	}

	/// <summary>The nodes on any way from one node to another, both included; empty when there is none.</summary>
	private HashSet<string> Between(string from, string to)
	{
		var reaches = new Dictionary<string, bool>(StringComparer.Ordinal);

		bool Reaches(string node)
		{
			if (node == to)
				return true;
			if (reaches.TryGetValue(node, out var known))
				return known;
			reaches[node] = false;
			var result = false;
			foreach (var edge in _scene.Edges.Where(e => e.From == node))
				result |= Reaches(edge.To);
			reaches[node] = result;
			return result;
		}

		var on = new HashSet<string>(StringComparer.Ordinal);

		void Walk(string node)
		{
			if (!Reaches(node) || !on.Add(node))
				return;
			foreach (var edge in _scene.Edges.Where(e => e.From == node))
				Walk(edge.To);
		}

		Walk(from);
		return on;
	}
}
