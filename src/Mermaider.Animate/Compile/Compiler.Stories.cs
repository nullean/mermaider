using System.Xml.Linq;
using Mermaider.Animate.Scene;
using Mermaider.Animate.Score;
using Mermaider.Animate.Timeline;

namespace Mermaider.Animate.Compile;

/// <summary>The verbs that tell a whole story at once (build, run, play) and the default animation for each diagram type.</summary>
internal sealed partial class Compiler
{
	/// <summary>What a bare <c>animate</c> plays: a sequence plays its messages, a state machine runs, the rest build up.</summary>
	private List<ScoreStep> DefaultSteps()
	{
		var line = _score.Line;
		Statement statement = _scene.Kind switch
		{
			SceneKind.Sequence => new PlayStatement(null, null, line),
			SceneKind.State when CoverWalk() is { Count: >= 2 } walk => new RunStatement(walk, line),
			_ => new BuildStatement(line),
		};
		return [new ScoreStep(null, [statement], line)];
	}

	// ---- build ----------------------------------------------------------------------------------------------------

	/// <summary>
	/// Reveals the diagram along its flow: groups outside-in, then nodes column by column in the flow's direction, each
	/// column's incoming edges drawing just before it appears. Edges back against the flow draw last.
	/// </summary>
	private double Build(int line, double at)
	{
		if (_scene.Kind == SceneKind.Sequence)
			return Play(new PlayStatement(null, null, line), at);

		var t = at;
		var groups = _scene.Groups.OrderBy(Depth).ThenBy(g => g.Box.Y).ThenBy(g => g.Box.X).ToList();
		var groupEnd = t;
		for (var i = 0; i < groups.Count; i++)
			groupEnd = Math.Max(groupEnd, Show(groups[i], t + (i * _spec.Stagger)));
		if (groups.Count > 0)
			t += _spec.Enter * 0.5;

		var shown = new HashSet<string>(StringComparer.Ordinal);
		var drawn = new HashSet<SceneEdge>();
		foreach (var column in Columns())
		{
			var incoming = _scene.Edges.Where(e => !drawn.Contains(e) && column.Any(n => n.Id == e.To) && shown.Contains(e.From)).ToList();
			if (incoming.Count > 0)
			{
				var end = t;
				foreach (var edge in incoming)
				{
					end = Math.Max(end, Draw(edge, t));
					_ = drawn.Add(edge);
				}
				t = end - (_spec.Enter * 0.3);
			}
			var columnEnd = t;
			for (var i = 0; i < column.Count; i++)
			{
				columnEnd = Math.Max(columnEnd, Show(column[i], t + (i * _spec.Stagger)));
				_ = shown.Add(column[i].Id);
			}
			t = columnEnd - (_spec.Enter * 0.25);

			var within = _scene.Edges.Where(e => !drawn.Contains(e) && shown.Contains(e.From) && shown.Contains(e.To)).ToList();
			if (within.Count > 0)
			{
				var end = t;
				foreach (var edge in within)
				{
					end = Math.Max(end, Draw(edge, t));
					_ = drawn.Add(edge);
				}
				t = end;
			}
		}

		var rest = _scene.Edges.Where(e => !drawn.Contains(e)).ToList();
		var restEnd = t;
		foreach (var edge in rest)
			restEnd = Math.Max(restEnd, Draw(edge, t));
		return Math.Max(Math.Max(restEnd, t), groupEnd);
	}

	private int Depth(SceneTarget group)
	{
		var depth = 0;
		for (var parent = group.Parent; parent is not null && depth < 32; parent = _scene.Group(parent)?.Parent)
			depth++;
		return depth;
	}

	/// <summary>Nodes grouped into columns along the flow: by x for left-to-right, by y for top-to-bottom, reversed for RL and BT.</summary>
	private List<List<SceneTarget>> Columns()
	{
		var horizontal = _scene.Direction is "LR" or "RL";
		var reverse = _scene.Direction is "RL" or "BT";
		var nodes = _scene.Targets.Where(t => !t.Box.IsEmpty).ToList();

		double Primary(SceneTarget t) => (horizontal ? t.Box.Center.X : t.Box.Center.Y) * (reverse ? -1 : 1);

		double Secondary(SceneTarget t) => horizontal ? t.Box.Center.Y : t.Box.Center.X;

		var sorted = nodes.OrderBy(Primary).ThenBy(Secondary).ToList();
		var columns = new List<List<SceneTarget>>();
		var tolerance = nodes.Count == 0 ? 0 : nodes.Average(n => horizontal ? n.Box.Width : n.Box.Height) * 0.5;
		foreach (var node in sorted)
		{
			if (columns.Count > 0 && Math.Abs(Primary(node) - Primary(columns[^1][0])) <= tolerance)
				columns[^1].Add(node);
			else
				columns.Add([node]);
		}
		foreach (var column in columns)
			column.Sort((a, b) => Secondary(a).CompareTo(Secondary(b)));
		return columns;
	}

	// ---- state machines -------------------------------------------------------------------------------------------

	/// <summary>
	/// Plays a path through a state machine: a ring shows the current state, a token carries each transition's event
	/// along its edge, and states already visited keep a faint trail. The ring stays where a run ends, so the next run
	/// can carry on from there.
	/// </summary>
	private double Run(IReadOnlyList<string> path, int line, double at)
	{
		var states = new List<SceneTarget>();
		for (var i = 0; i < path.Count; i++)
		{
			var id = path[i] == "[*]" ? (i == 0 ? "_start" : "_end") : path[i];
			if (_scene.Target(id) is { } state)
				states.Add(state);
			else
			{
				_diagnostics.Add(
					new(
						line,
						path[i] == "[*]"
							? "This diagram has no [*] state there."
							: $"There is no state '{path[i]}' in the diagram.{Known(_scene.Targets.Select(t => t.Id).Where(t => !t.StartsWith('_')))}"
					)
				);
				return at;
			}
		}

		var edges = new List<SceneEdge>();
		for (var i = 0; i + 1 < states.Count; i++)
		{
			var edge = _scene.Edges.FirstOrDefault(e => e.From == states[i].Id && e.To == states[i + 1].Id);
			if (edge is null)
			{
				var allowed = _scene.Edges.Where(e => e.From == states[i].Id).Select(e => Display(e.To)).Distinct().ToList();
				_diagnostics.Add(
					new(
						line,
						allowed.Count == 0
							? $"There is no transition {Display(states[i].Id)} -> {Display(states[i + 1].Id)}: {Display(states[i].Id)} has no transitions out."
							: $"There is no transition {Display(states[i].Id)} -> {Display(states[i + 1].Id)}. From {Display(states[i].Id)} the diagram allows: {string.Join(", ", allowed)}."
					)
				);
				return at;
			}
			edges.Add(edge);
		}

		var t = at;
		if (_ringAt != states[0].Id)
		{
			if (_ringAt is not null)
				MoveRing(_ringAt, states[0], t);
			else
				RingOn(states[0], t);
			t += _spec.Shift;
		}
		for (var i = 0; i < edges.Count; i++)
		{
			var arrive = Send(edges[i], false, t, edges[i].Label, null, null, Speed.Normal, Fate.Arrives, null);
			MoveRing(states[i].Id, states[i + 1], arrive - (_spec.Shift * 0.3));
			t = arrive + _spec.Beat;
		}
		return t + (_spec.Shift * 0.5);
	}

	private static string Display(string id) => id switch
	{
		"_start" => "[*]",
		"_end" => "[*]",
		_ => id,
	};

	private (XElement Ring, XElement Trail) RingFor(SceneTarget state)
	{
		if (_rings.TryGetValue(state.Id, out var ring))
			return ring;
		var accent = "var(--accent, #3b82f6)";
		var trail = Overlay.Ring(state.Box, accent, 0, 3, accent, 0.1);
		var current = Overlay.Ring(state.Box, accent, 2.5, 5);
		_overlay.AddFirst(trail);
		_overlay.Add(current);
		ring = (current, trail);
		_rings[state.Id] = ring;
		return ring;
	}

	private void RingOn(SceneTarget state, double at)
	{
		Opacity(RingFor(state).Ring, ReducedMotion.Cut, 0).Animate(at, _spec.Shift, Channel.Value(1), Easing.Enter);
		_ringAt = state.Id;
		_scoreEnd.Add(end =>
		{
			foreach (var (ring, trail) in _rings.Values)
			{
				Opacity(ring, ReducedMotion.Cut, 0).Animate(end, _spec.Exit, Channel.Value(0), Easing.Exit);
				Opacity(trail, ReducedMotion.Cut, 0).Animate(end, _spec.Exit, Channel.Value(0), Easing.Exit);
			}
		});
	}

	private void MoveRing(string fromId, SceneTarget to, double at)
	{
		if (_scene.Target(fromId) is { } from)
		{
			var (ring, trail) = RingFor(from);
			Opacity(ring, ReducedMotion.Cut, 0).Animate(at, _spec.Shift, Channel.Value(0), Easing.Exit);
			Opacity(trail, ReducedMotion.Cut, 0).Animate(at, _spec.Shift, Channel.Value(1), Easing.Enter);
		}
		Opacity(RingFor(to).Ring, ReducedMotion.Cut, 0).Animate(at, _spec.Shift, Channel.Value(1), Easing.Enter);
		_ = Pulse(to, at, _spec.PulseScale);
		_ringAt = to.Id;
	}

	/// <summary>A refused transition: the token sets out, stops short, and fails; the ring does not move.</summary>
	private double Try(TryStatement attempt, double at)
	{
		var from = ResolveNode(attempt.From, attempt.Line);
		var to = ResolveNode(attempt.To, attempt.Line);
		if (from is null || to is null)
			return at;
		var edge = _scene.Edges.FirstOrDefault(e => e.From == from.Id && e.To == to.Id);
		Polyline path;
		if (edge is not null)
			path = edge.Path;
		else
		{
			var a = from.Box.Exit(to.Box.Center);
			var b = to.Box.Exit(from.Box.Center);
			path = new Polyline([a, b]);
			var guide = Overlay.Stroke(path, _colors.Fail, 1.5, "4 4");
			_overlay.AddFirst(guide);
			var g = Opacity(guide, ReducedMotion.Cut, 0);
			g.Animate(at, _spec.Shift, Channel.Value(0.7), Easing.Enter);
			_stepEnd.Add(end => g.Animate(end, _spec.Exit, Channel.Value(0), Easing.Exit));
		}

		var token = Overlay.Token(_colors.Fail, "var(--bg)", 5, attempt.Reason, out _);
		_overlay.Add(token);
		var duration = _spec.TravelTime(path.Length, Speed.Normal) * 0.45;
		var move = ChannelFor(token, Property.Transform, Channel.Transform(path.Start.X, path.Start.Y, 1), ReducedMotion.Off);
		var opacity = ChannelFor(token, Property.Opacity, Channel.Value(0), ReducedMotion.Off);
		opacity.Animate(at, 140, Channel.Value(1), Easing.Enter);
		Travel(move, path, at, duration, 0.42);
		var stop = at + duration;

		var cross = Overlay.Cross(path.At(0.42), _colors.Fail);
		_overlay.Add(cross);
		var c = Opacity(cross, ReducedMotion.Cut, 0);
		c.Animate(stop, _spec.Shift * 0.6, Channel.Value(1), Easing.Enter);
		_stepEnd.Add(end =>
		{
			c.Animate(end, _spec.Exit, Channel.Value(0), Easing.Exit);
			opacity.Animate(end, _spec.Exit, Channel.Value(0), Easing.Exit);
		});
		return stop + _spec.Shift;
	}

	/// <summary>A walk from the start state that takes every transition at least once, preferring ones not yet taken.</summary>
	private List<string>? CoverWalk()
	{
		var start = _scene.Target("_start") ?? _scene.Targets.FirstOrDefault();
		if (start is null)
			return null;
		var remaining = new HashSet<SceneEdge>(_scene.Edges.Where(e => _scene.Target(e.From) is not null && _scene.Target(e.To) is not null));
		var walk = new List<string> { start.Id };
		var at = start.Id;
		var limit = (remaining.Count * 3) + 2;
		while (remaining.Count > 0 && walk.Count < limit)
		{
			var next = remaining.FirstOrDefault(e => e.From == at);
			if (next is not null)
			{
				_ = remaining.Remove(next);
				walk.Add(next.To);
				at = next.To;
				continue;
			}
			// walk to the nearest state with an untaken transition, along taken ones
			var route = Nearest(at, s => remaining.Any(e => e.From == s));
			if (route is null || route.Count == 0)
				break;
			foreach (var step in route)
			{
				walk.Add(step);
				at = step;
			}
		}
		return walk.Select(Display).ToList();
	}

	private List<string>? Nearest(string from, Func<string, bool> goal)
	{
		var previous = new Dictionary<string, string>(StringComparer.Ordinal);
		var queue = new Queue<string>();
		queue.Enqueue(from);
		var seen = new HashSet<string>(StringComparer.Ordinal) { from };
		while (queue.Count > 0)
		{
			var current = queue.Dequeue();
			if (current != from && goal(current))
			{
				var route = new List<string>();
				for (var s = current; s != from; s = previous[s])
					route.Add(s);
				route.Reverse();
				return route;
			}
			foreach (var edge in _scene.Edges.Where(e => e.From == current))
				if (seen.Add(edge.To))
				{
					previous[edge.To] = current;
					queue.Enqueue(edge.To);
				}
		}
		return null;
	}

	// ---- sequence diagrams ----------------------------------------------------------------------------------------

	/// <summary>Plays messages in order: each draws from sender to receiver and a token makes the trip; notes appear in their place.</summary>
	private double Play(PlayStatement play, double at)
	{
		if (_scene.Kind != SceneKind.Sequence)
		{
			_diagnostics.Add(new(play.Line, "play is for sequence diagrams. For other diagrams use build, send or tour."));
			return at;
		}
		var messages = _scene.Edges;
		var first = play.First ?? 1;
		var last = play.Last ?? messages.Count;
		if (first > messages.Count || last > messages.Count || first > last)
		{
			_diagnostics.Add(new(play.Line, $"The diagram has {messages.Count} messages, so #{first}..#{last} is out of range."));
			return at;
		}

		var t = at;
		var notesShown = new HashSet<XElement>();
		for (var i = first - 1; i < last; i++)
		{
			var message = messages[i];
			var y = message.Path.Start.Y;
			foreach (var note in _scene.Notes.Where(n => n.Y < y && !notesShown.Contains(n.Element) && (i == first - 1 || n.Y > messages[i - 1].Path.Start.Y)))
			{
				ShowElement(note.Element, t);
				_ = notesShown.Add(note.Element);
				t += _spec.Enter + _spec.Beat;
			}
			var drawEnd = Draw(message, t);
			t = Send(message, false, t + (_spec.DrawTime(message.Path.Length) * 0.2), null, null, null, Speed.Normal, Fate.Arrives, null);
			t = Math.Max(t, drawEnd) + _spec.Beat;
		}
		if (play.Last is null)
			foreach (var note in _scene.Notes.Where(n => !notesShown.Contains(n.Element)))
			{
				ShowElement(note.Element, t);
				t += _spec.Enter;
			}
		return t;
	}

	private void ShowElement(XElement element, double at)
	{
		var visibility = Visibility(element);
		if (!visibility.IsAnimated)
			visibility.Initial[0] = 0;
		visibility.Animate(at, _spec.Enter, Channel.Value(1), Easing.Enter);
	}
}
