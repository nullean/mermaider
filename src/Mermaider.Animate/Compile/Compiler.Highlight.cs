using System.Xml.Linq;
using Mermaider.Animate.Scene;
using Mermaider.Animate.Score;
using Mermaider.Animate.Timeline;

namespace Mermaider.Animate.Compile;

/// <summary>
/// Highlights: a standing emphasis that holds for the whole animation, with or without steps. Each moves gently and
/// endlessly over the story's clock, in whole cycles, so the loop never shows a seam; under reduced motion it holds still,
/// lit but not moving. They are applied after the story and its settle, which never touches them.
/// </summary>
internal sealed partial class Compiler
{
	private const double BreathPeriod = 2400;
	private const double SheenPeriod = 2600;

	/// <param name="from">Where the story's clock starts: highlights begin with the story, after any intro.</param>
	/// <param name="to">Where the clock ends.</param>
	private void ApplyHighlights(double from, double to)
	{
		if (to - from < 1)
			return;
		foreach (var highlight in _score.Highlights)
		{
			var color = highlight.Role is { } role ? _colors.For(role) : "var(--accent, #3b82f6)";
			switch (highlight.Kind)
			{
				case HighlightKind.Node:
					foreach (var target in Resolve(highlight.Targets))
						Glow(target, color, from, to);
					break;
				case HighlightKind.Path:
					if (RouteOf(highlight) is { } path)
						LitPath(path, color, from, to);
					break;
				case HighlightKind.Flow:
					if (RouteOf(highlight) is { } flow)
						foreach (var (edge, reverse) in flow)
							Flow(edge, reverse, highlight.Role is null ? null : color, from, to);
					break;
			}
		}
	}

	/// <summary>The edges along a highlighted route, each with whether the route runs against it.</summary>
	private List<(SceneEdge Edge, bool Reverse)>? RouteOf(HighlightStatement highlight)
	{
		var stops = highlight.Route.Select(id => ResolveNode(id, highlight.Line)).ToList();
		if (stops.Any(s => s is null))
			return null;
		var hops = new List<(SceneEdge, bool)>();
		for (var i = 0; i + 1 < stops.Count; i++)
		{
			if (Route(stops[i]!.Id, stops[i + 1]!.Id, highlight.Line) is not { } hop)
				return null;
			hops.Add(hop);
		}
		return hops;
	}

	/// <summary>Whole cycles of <paramref name="period"/> as near as fits the span, so the end of one loop meets the start of the next.</summary>
	private static (int Cycles, double Length) Cycles(double from, double to, double period)
	{
		var cycles = Math.Max(1, (int)Math.Round((to - from) / period));
		return (cycles, (to - from) / cycles);
	}

	/// <summary>
	/// A node glows: a ring in the role's colour that stays, and a halo round it that breathes in and out. The ring moves
	/// and appears with its node.
	/// </summary>
	private void Glow(SceneTarget target, string color, double from, double to)
	{
		if (target.Box.IsEmpty)
			return;
		var holder = target.Elements.Count > 0 && target.Kind == TargetKind.Node ? Outer(target.Elements[0]) : _overlay;
		var ring = Overlay.Ring(target.Box, color, 2, 4, color, 0.1);
		ring.SetAttributeValue("opacity", null);
		var halo = Overlay.Ring(target.Box, color, 6, 7);
		halo.SetAttributeValue("opacity", null);
		halo.SetAttributeValue("stroke-opacity", "0.22");
		holder.Add(halo, ring);

		// the halo swells and fades, the ring holds; reduced motion keeps the halo at its rest
		var breath = ChannelFor(halo, Property.Opacity, Channel.Value(0.35), ReducedMotion.Off);
		var swell = ChannelFor(halo, Property.Transform, Channel.Transform(0, 0, 1), ReducedMotion.Off);
		swell.Origin = new Vec(target.Box.Center.X - _stage.X, target.Box.Center.Y - _stage.Y);
		var (cycles, length) = Cycles(from, to, BreathPeriod);
		for (var i = 0; i < cycles; i++)
		{
			var at = from + (i * length);
			breath.Animate(at, length / 2, Channel.Value(1), Easing.Move);
			breath.Animate(at + (length / 2), length / 2, Channel.Value(0.35), Easing.Move);
			swell.Animate(at, length / 2, Channel.Transform(0, 0, 1.06), Easing.Move);
			swell.Animate(at + (length / 2), length / 2, Channel.Transform(0, 0, 1), Easing.Move);
		}
	}

	/// <summary>A route stays lit in its colour, and a sheen runs along the whole of it, start to end, once a cycle.</summary>
	private void LitPath(List<(SceneEdge Edge, bool Reverse)> route, string color, double from, double to)
	{
		var points = new List<Vec>();
		foreach (var (edge, reverse) in route)
		{
			var path = reverse ? edge.Path.Reversed() : edge.Path;
			var lit = Overlay.Stroke(path, color, edge.Thick ? 5 : 3.5, round: true);
			lit.SetAttributeValue("opacity", null);
			_overlay.AddFirst(lit);
			points.AddRange(points.Count > 0 ? path.Points.Skip(1) : path.Points);
		}
		var whole = new Polyline(points);
		var sheen = Overlay.Stroke(whole, "var(--bg, #fff)", 2, round: true);
		sheen.SetAttributeValue("pathLength", "1");
		sheen.SetAttributeValue("stroke-dasharray", "0.08 1.2");
		sheen.SetAttributeValue("stroke-opacity", "0.85");
		sheen.SetAttributeValue("opacity", null);
		_overlay.Add(sheen);
		// the sheen waits off the path (offset 0.08 hides it before the start), runs to the end, and waits again
		var run = ChannelFor(sheen, Property.DashOffset, Channel.Value(0.08), ReducedMotion.Off);
		var (cycles, length) = Cycles(from, to, SheenPeriod);
		for (var i = 0; i < cycles; i++)
		{
			var at = from + (i * length);
			run.Animate(at, length * 0.6, Channel.Value(-1), Easing.Move);
			run.Set(at + (length * 0.6) + 1, Channel.Value(0.08));
		}
	}

	/// <summary>
	/// An edge flows: a dotted line in the accent (or the role's colour) moves along it in the direction of travel, without
	/// end. It moves a whole number of its own dot spacings per loop, so it never jumps.
	/// </summary>
	private void Flow(SceneEdge edge, bool reverse, string? color, double from, double to)
	{
		var path = reverse ? edge.Path.Reversed() : edge.Path;
		var paint = color ?? "var(--accent, #3b82f6)";
		// the route, faintly lit, so the eye reads it; then short dashes travelling along it, which read as direction
		var underlay = Overlay.Stroke(path, paint, edge.Thick ? 5 : 3.5, round: true);
		underlay.SetAttributeValue("opacity", "0.22");
		_overlay.AddFirst(underlay);
		var dashLength = 9.0;
		var gap = Math.Max(9, Math.Min(16, path.Length / 4));
		var pattern = dashLength + gap;
		var stroke = Overlay.Stroke(path, paint, edge.Thick ? 4.5 : 3.2, $"{Num.F(dashLength)} {Num.F(gap)}", round: true);
		stroke.SetAttributeValue("opacity", "0.95");
		underlay.AddAfterSelf(stroke);
		var distance = _spec.StreamSpeed * ((to - from) / 1000);
		var steps = Math.Max(1, Math.Round(distance / pattern));
		ChannelFor(stroke, Property.DashOffset, Channel.Value(0), ReducedMotion.Off).Animate(from, to - from, Channel.Value(-(steps * pattern)), Easing.Linear);
	}
}
