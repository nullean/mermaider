using System.Xml.Linq;
using Mermaider.Animate.Scene;
using Mermaider.Animate.Score;
using Mermaider.Animate.Timeline;

namespace Mermaider.Animate.Compile;

/// <summary>
/// Outages, in the words people use for them: a node fails, a failure cascades, a node recovers. Each is what an author
/// would otherwise build from marks, badges and cuts, and undoes as one.
/// </summary>
internal sealed partial class Compiler
{
	/// <summary>The edges a failure cut, by the node that failed, so its recovery heals them.</summary>
	private readonly Dictionary<string, List<SceneEdge>> _failCuts = [with(StringComparer.Ordinal)];

	/// <summary>The edges a cascade ran along, so a recovery at either end clears them.</summary>
	private readonly Dictionary<SceneEdge, string> _cascaded = [];

	/// <summary>
	/// The node is marked failed, carries its reason, and what leads into it is cut: nothing reaches it any more. A node
	/// nothing leads into (where the flow starts) loses what leaves it instead.
	/// </summary>
	private double Fail(FailStatement fail, double at)
	{
		foreach (var target in Resolve(fail.Targets))
			FailNode(target, fail.Reason, at, fail.Line);
		return at + _spec.Shift;
	}

	private void FailNode(SceneTarget target, string? reason, double at, int line)
	{
		Mark(target.Id, target.Box, null, Role.Fail, at, target.Elements.Count > 0 ? Outer(target.Elements[0]) : null);
		if (reason is not null)
			_ = BadgeAll(new BadgeStatement([new TargetRef(target.Id, IsGroup: false, line)], reason, Role.Fail, line), at);
		var into = _scene.Edges.Where(e => e.To == target.Id && e.From != target.Id).ToList();
		var cut = into.Count > 0 ? into : [.. _scene.Edges.Where(e => e.From == target.Id && e.To != target.Id)];
		foreach (var edge in cut)
			_ = Cut(edge, at, cut: true);
		if (!_failCuts.TryGetValue(target.Id, out var known))
			_failCuts[target.Id] = known = [];
		known.AddRange(cut.Where(e => !known.Contains(e)));
	}

	/// <summary>
	/// The node comes back: its failure mark and badge clear, the edges its failure cut heal, a cascade along its edges
	/// fades, and it pulses, so the return is seen.
	/// </summary>
	private double Recover(RecoverStatement recover, double at)
	{
		var targets = Resolve(recover.Targets);
		foreach (var target in targets)
		{
			Mark(target.Id, target.Box, null, null, at);
			_ = BadgeAll(new BadgeStatement([new TargetRef(target.Id, IsGroup: false, recover.Line)], null, null, recover.Line), at);
			if (_failCuts.TryGetValue(target.Id, out var cut))
			{
				foreach (var edge in cut)
					_ = Cut(edge, at, cut: false);
				_ = _failCuts.Remove(target.Id);
			}
			foreach (var cascaded in _cascaded.Where(c => c.Key.From == target.Id || c.Key.To == target.Id).ToList())
			{
				Mark(cascaded.Value, Box.Empty, cascaded.Key, null, at);
				_ = _cascaded.Remove(cascaded.Key);
			}
		}
		var end = at + _spec.Shift;
		foreach (var target in targets)
			end = Math.Max(end, Pulse(target, at + _spec.Shift, _spec.PulseScale));
		return end;
	}

	/// <summary>
	/// The cause fails, then the failure travels each edge of the chain in turn, along the edge or against it, a line in
	/// the failure colour drawn in the direction it spreads, and the node at the far end fails as it arrives. The reason
	/// goes on the cause only: what follows fails because of it.
	/// </summary>
	private double Cascade(CascadeStatement cascade, double at)
	{
		var nodes = cascade.Path.Select(id => ResolveNode(id, cascade.Line)).ToList();
		if (nodes.Any(n => n is null))
			return at;
		var routes = new List<(SceneEdge Edge, bool Reverse)>();
		for (var i = 0; i + 1 < nodes.Count; i++)
		{
			if (Route(nodes[i]!.Id, nodes[i + 1]!.Id, cascade.Line) is not { } route)
				return at;
			routes.Add(route);
		}

		FailNode(nodes[0]!, cascade.Reason, at, cascade.Line);
		var t = at + _spec.Shift;
		for (var i = 0; i < routes.Count; i++)
		{
			var (edge, reverse) = routes[i];
			var path = reverse ? edge.Path.Reversed() : edge.Path;
			var line = Overlay.Stroke(path, _colors.Fail, 3.5, round: true);
			line.SetAttributeValue("opacity", null);
			line.SetAttributeValue("pathLength", "1");
			line.SetAttributeValue("stroke-dasharray", "1 1");
			_overlay.AddFirst(line);
			// visible from the start, hidden by its dash until it is drawn; recovery fades it
			_ = Opacity(line, ReducedMotion.Cut, 1);
			var duration = _spec.DrawTime(edge.Path.Length);
			// a cut along the way says less than the failure running down it: its cross gives way to the line
			if (_edgeVisuals.TryGetValue(edge, out var visual) && visual.Cross is { } cross)
				Opacity(cross, ReducedMotion.Cut, 0).Animate(t, _spec.Exit, Channel.Value(0), Easing.Exit);
			ChannelFor(line, Property.DashOffset, Channel.Value(1), ReducedMotion.Cut).Animate(t, duration, Channel.Value(0), Easing.Move);
			var key = $"{edge.From}->{edge.To}#cascade";
			if (_cascaded.TryGetValue(edge, out var previous) && _marks.TryGetValue((previous, Role.Fail), out var old))
				Opacity(old, ReducedMotion.Cut).Animate(t, _spec.Exit, Channel.Value(0), Easing.Exit);
			_marks[(key, Role.Fail)] = line;
			_cascaded[edge] = key;

			var arrival = t + (duration * 0.9);
			var next = nodes[i + 1]!;
			Mark(next.Id, next.Box, null, Role.Fail, arrival, next.Elements.Count > 0 ? Outer(next.Elements[0]) : null);
			t = arrival + _spec.Shift;
		}
		return t;
	}
}
