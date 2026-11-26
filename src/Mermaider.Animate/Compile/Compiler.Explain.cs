using System.Text.RegularExpressions;
using System.Xml.Linq;
using Mermaider.Animate.Scene;
using Mermaider.Animate.Score;
using Mermaider.Animate.Timeline;

namespace Mermaider.Animate.Compile;

/// <summary>
/// The verbs that explain a system rather than move through it: something planned or missing (ghost), things that belong
/// together (group), a cause and its effect off the edges (because), attempts that fail and try again (retry), and a
/// service running as several (scale).
/// </summary>
internal sealed partial class Compiler
{
	/// <summary>A ghost's outline and the element it dims, by what it is a ghost of, so it can be cleared.</summary>
	private readonly Dictionary<string, (XElement Outline, XElement Dimmed)> _ghosts = [with(StringComparer.Ordinal)];

	/// <summary>The replicas behind a node, nearest first.</summary>
	private readonly Dictionary<string, List<XElement>> _replicas = [with(StringComparer.Ordinal)];

	private const double GhostOpacity = 0.38;

	/// <summary>How far from a because line its reason may sit, in pill sizes, nearest first.</summary>
	private static readonly double[] ReasonDistances = [1.0, 1.8, 2.6];

	/// <summary>Where round a because line's bend its reason may sit: beside, above and below, and corner-wise.</summary>
	private static readonly Vec[] ReasonDirections = [new(1, 0), new(-1, 0), new(0, -1), new(0, 1), new(1, -1), new(-1, -1), new(1, 1), new(-1, 1)];

	/// <summary>
	/// A ghost is faded, with a dashed outline: planned, missing or removed. It stays until <c>ghost … clear</c>, and an
	/// outline belongs to its node, so it moves and appears with it.
	/// </summary>
	private double Ghost(GhostStatement ghost, double at)
	{
		var line = "var(--_line, #64748b)";
		foreach (var target in Resolve(ghost.Targets))
			Haunt(
				target.Id,
				Outer(target.Elements[0]),
				() => Overlay.Ring(target.Box, line, 1.5, 5, round: false),
				target.Elements[0],
				ghost.Clear,
				at,
				inside: true
			);
		foreach (var edge in ResolveEdges(ghost.Edges))
			Haunt($"{edge.From}->{edge.To}", _overlay, () => Overlay.Stroke(edge.Path, line, 1.5, "6 5"), edge.Element, ghost.Clear, at, inside: false);
		return at + _spec.Shift;
	}

	private void Haunt(string key, XElement holder, Func<XElement> outline, XElement dimmed, bool clear, double at, bool inside)
	{
		if (clear)
		{
			if (_ghosts.TryGetValue(key, out var ghost))
			{
				Opacity(ghost.Outline, ReducedMotion.Cut).Animate(at, _spec.Exit, Channel.Value(0), Easing.Exit);
				Opacity(State(ghost.Dimmed), ReducedMotion.Cut).Animate(at, _spec.Shift, Channel.Value(1), Easing.Move);
				_ = _ghosts.Remove(key);
			}
			return;
		}
		if (_ghosts.ContainsKey(key))
			return;
		var element = outline();
		element.SetAttributeValue("stroke-dasharray", "6 5");
		element.SetAttributeValue("opacity", null);
		if (inside)
			holder.Add(element);
		else
			holder.AddFirst(element);
		Opacity(element, ReducedMotion.Cut, 0).Animate(at, _spec.Shift, Channel.Value(1), Easing.Enter);
		Opacity(State(dimmed), ReducedMotion.Cut).Animate(at, _spec.Shift, Channel.Value(GhostOpacity), Easing.Move);
		_ghosts[key] = (element, dimmed);
	}

	/// <summary>
	/// A labelled outline round nodes the diagram does not group: "these three are the problem". It is drawn behind what
	/// moves, faintly filled, and leaves when its step ends.
	/// </summary>
	private double Group(GroupStatement group, double at)
	{
		var targets = Resolve(group.Targets).Where(t => !t.Box.IsEmpty).ToList();
		if (targets.Count == 0)
			return at;
		var box = targets.Select(t => t.Box).Aggregate((a, b) => a.Union(b)).Inflate(12);
		var color = group.Role is { } role ? _colors.For(role) : "var(--accent, #3b82f6)";
		var outline = Overlay.Ring(box, color, 1.5, 0, color, 0.06);
		outline.SetAttributeValue("opacity", null);
		outline.SetAttributeValue("rx", "14");
		var label = new XElement(
			SvgScene.Ns + "text",
			new XAttribute("x", Num.F(box.X + 10)),
			new XAttribute("y", Num.F(box.Y - 7)),
			new XAttribute("font-size", "12"),
			new XAttribute("font-weight", "700"),
			new XAttribute("fill", color),
			group.Label
		);
		var element = new XElement(SvgScene.Ns + "g", new XAttribute("opacity", "0"), outline, label);
		_overlay.AddFirst(element);
		var opacity = Opacity(element, ReducedMotion.Cut, 0);
		opacity.Animate(at, _spec.Enter, Channel.Value(1), Easing.Enter);
		_stepEnd.Add(end => opacity.Animate(end, _spec.Exit, Channel.Value(0), Easing.Exit));
		return at + _spec.Enter;
	}

	/// <summary>
	/// A cause and its effect where the diagram has no edge: a curved line draws from one to the other, its arrowhead lands,
	/// and the reason shows on the bend. It leaves when its step ends.
	/// </summary>
	private double Because(BecauseStatement because, double at)
	{
		if (ResolveNode(because.From, because.Line) is not { } cause || ResolveNode(because.To, because.Line) is not { } effect)
			return at;
		var start = cause.Box.Exit(effect.Box.Center);
		var end = effect.Box.Exit(cause.Box.Center);
		var middle = (start + end) * 0.5;
		var along = end - start;
		var length = Math.Max(1, along.Length);
		// bend to the side, so the line reads as an aside, not an edge
		var normal = new Vec(-along.Y / length, along.X / length);
		var control = middle + (normal * Math.Min(56, length * 0.3));
		var points = new List<Vec>();
		for (var i = 0; i <= 24; i++)
		{
			var t = i / 24.0;
			var u = 1 - t;
			points.Add((start * (u * u)) + (control * (2 * u * t)) + (end * (t * t)));
		}
		var path = new Polyline(points);
		var color = because.Role is { } role ? _colors.For(role) : _colors.Warn;

		var line = Overlay.Stroke(path, color, 2, round: true);
		line.SetAttributeValue("opacity", null);
		line.SetAttributeValue("pathLength", "1");
		line.SetAttributeValue("stroke-dasharray", "1 1");
		var tip = points[^1];
		var back = points[^3];
		var angle = Math.Atan2(tip.Y - back.Y, tip.X - back.X) * 180 / Math.PI;
		var head = new XElement(
			SvgScene.Ns + "path",
			new XAttribute("d", "M0,0 L-9,-4.5 L-9,4.5 Z"),
			new XAttribute("fill", color),
			new XAttribute("transform", $"translate({Num.F(tip.X)} {Num.F(tip.Y)}) rotate({Num.F(angle)})"),
			new XAttribute("opacity", "0")
		);
		// the reason sits near the bend on a spot that covers no node and keeps off its own line: a ring of spots round
		// the bend, beside it, above, below and corner-wise, at growing distances; the nearest clear one wins
		var pill = new Vec((because.Reason.Length * 6.9) + 18, 20);
		var middle0 = path.At(0.5);

		bool Over(Box b, Vec at) => Math.Abs(b.Center.X - at.X) < (b.Width + pill.X) / 2 && Math.Abs(b.Center.Y - at.Y) < (b.Height + pill.Y) / 2;

		int Covered(Vec at) => _scene.Targets.Count(t => !t.Box.IsEmpty && Over(t.Box, at));

		bool OnLine(Vec at) => points.Any(p => Math.Abs(p.X - at.X) < (pill.X / 2) + 2 && Math.Abs(p.Y - at.Y) < (pill.Y / 2) + 2);

		var spots = ReasonDistances.SelectMany(
			k => ReasonDirections.Select(d => middle0 + new Vec(d.X * ((pill.X / 2) + 10) * k, d.Y * ((pill.Y / 2) + 10) * k))
		).ToList();
		var bend = spots.OrderBy(Covered).ThenBy(p => OnLine(p) ? 1 : 0).ThenBy(p => (p - middle0).Length).First();
		var reason = Overlay.Label(because.Reason, color, 0, filled: false);
		reason.SetAttributeValue("transform", $"translate({Num.F(bend.X)} {Num.F(bend.Y)})");
		reason.SetAttributeValue("opacity", "0");
		var element = new XElement(SvgScene.Ns + "g", line, head, reason);
		_overlay.Add(element);

		var draw = _spec.DrawTime(path.Length);
		ChannelFor(line, Property.DashOffset, Channel.Value(1), ReducedMotion.Cut).Animate(at, draw, Channel.Value(0), Easing.Move);
		Opacity(head, ReducedMotion.Cut, 0).Animate(at + (draw * 0.85), _spec.Shift * 0.5, Channel.Value(1), Easing.Enter);
		Opacity(reason, ReducedMotion.Cut, 0).Animate(at + (draw * 0.5), _spec.Enter, Channel.Value(1), Easing.Enter);
		var whole = Opacity(element, ReducedMotion.Cut, 1);
		_stepEnd.Add(stepEnd => whole.Animate(stepEnd, _spec.Exit, Channel.Value(0), Easing.Exit));
		return at + draw + _spec.ReadTime(because.Reason);
	}

	/// <summary>
	/// Attempts along an edge, each a token numbered n/N that is lost on the way, the wait between them doubling with
	/// backoff; when the retry succeeds the last attempt arrives.
	/// </summary>
	private double Retry(RetryStatement retry, double at)
	{
		if (ResolveNode(retry.From, retry.Line) is not { } from || ResolveNode(retry.To, retry.Line) is not { } to)
			return at;
		if (Route(from.Id, to.Id, retry.Line) is not { } route)
			return at;
		var gap = _spec.Beat * 2;
		var t = at;
		var end = at;
		for (var k = 1; k <= retry.Attempts; k++)
		{
			var lands = k == retry.Attempts && retry.Succeeds;
			end = Send(
				route.Edge,
				route.Reverse,
				t,
				$"{k}/{retry.Attempts}",
				lands ? Role.Ok : Role.Warn,
				null,
				Speed.Fast,
				lands ? Fate.Arrives : Fate.Lost,
				to
			);
			t = end + gap;
			if (retry.Backoff)
				gap *= 2;
		}
		return end;
	}

	/// <summary>
	/// A node runs as several: copies of it stack out behind it, like a deck, and a badge counts them; scaling back folds
	/// them in. The offset is a cut under reduced motion, so the stack still shows.
	/// </summary>
	private double Scale(ScaleStatement scale, double at)
	{
		if (Resolve([scale.Target]).FirstOrDefault() is not { Box.IsEmpty: false } target)
			return at;
		var node = target.Elements[0];
		// inside the node's wrapper, so the replicas move, pulse and appear with it
		_ = Outer(node);
		if (!_replicas.TryGetValue(target.Id, out var replicas))
			_replicas[target.Id] = replicas = [];
		var wanted = scale.Count - 1;
		var ownClass = new Regex($@"(^|\s){Regex.Escape(_scene.Prefix)}-\S+");
		for (var i = replicas.Count; i < wanted; i++)
		{
			var copy = new XElement(node);
			copy.SetAttributeValue("data-id", null);
			if ((string?)copy.Attribute("class") is { } cls)
				copy.SetAttributeValue("class", ownClass.Replace(cls, "").Trim());
			foreach (var inner in copy.Descendants().Where(d => d.Attribute("class") is not null).ToList())
				inner.SetAttributeValue("class", ownClass.Replace((string)inner.Attribute("class")!, "").Trim());
			var replica = new XElement(SvgScene.Ns + "g", new XAttribute("aria-hidden", "true"), copy);
			// behind the node and its earlier replicas
			(replicas.Count == 0 ? node : replicas[^1]).AddBeforeSelf(replica);
			replicas.Add(replica);
		}
		var end = at;
		for (var i = 0; i < replicas.Count; i++)
		{
			var replica = replicas[i];
			var shown = i < wanted;
			var depth = (i + 1) * 6.0;
			var t = at + (i * _spec.Stagger);
			var place = ChannelFor(replica, Property.Transform, Channel.Transform(0, 0, 1), ReducedMotion.Cut);
			place.Origin = new Vec(target.Box.Center.X - _stage.X, target.Box.Center.Y - _stage.Y);
			place.Animate(t, _spec.Shift, shown ? Channel.Transform(depth, depth, 1) : Channel.Transform(0, 0, 1), Easing.Move);
			Opacity(replica, ReducedMotion.Cut, 0).Animate(t, _spec.Shift * 0.6, Channel.Value(shown ? 1 : 0), shown ? Easing.Enter : Easing.Exit);
			end = Math.Max(end, t + _spec.Shift);
		}
		_ = BadgeAll(
			new BadgeStatement([new TargetRef(target.Id, IsGroup: false, scale.Line)], scale.Count > 1 ? $"×{scale.Count}" : null, null, scale.Line),
			at
		);
		return end;
	}
}
