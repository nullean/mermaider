using System.Xml.Linq;
using Mermaider.Animate.Scene;
using Mermaider.Animate.Score;
using Mermaider.Animate.Timeline;

namespace Mermaider.Animate.Compile;

/// <summary>
/// Verbs that show state rather than movement: how full a queue is, what a node is waiting on, a label it carries, what a
/// span of messages costs. Each stays until something changes it, so a step can leave the diagram in a state the next
/// step builds on.
/// </summary>
internal sealed partial class Compiler
{
	private const int MaxSlots = 16;

	private readonly Dictionary<string, Meter> _meters = [with(StringComparer.Ordinal)];
	private readonly Dictionary<string, XElement> _badges = [with(StringComparer.Ordinal)];

	private sealed class Meter(int capacity, List<XElement> slots, XElement track)
	{
		public int Capacity { get; } = capacity;
		public List<XElement> Slots { get; } = slots;
		public XElement Track { get; } = track;
		public int Shown { get; set; }
	}

	/// <summary>
	/// A meter of slots along the bottom of a node fills or drains to the count. The last quarter of the slots takes the
	/// warning colour, so a queue nearly full reads as a problem without the score saying so.
	/// </summary>
	private double Fill(FillStatement fill, double at)
	{
		if (ResolveNode(fill.Target.Id, fill.Line) is not { } target)
			return at;
		if (!_meters.TryGetValue(target.Id, out var meter))
		{
			if (fill.Capacity is not { } capacity)
			{
				_diagnostics.Add(new(fill.Line, $"The first fill on {target.Id} needs its capacity: fill {target.Id} {fill.Count}/8."));
				return at;
			}
			var slots = Math.Min(capacity, MaxSlots);
			var warnFrom = (int)Math.Ceiling(slots * 0.75);
			var track = Overlay.Slot(target.Box, 0, slots, "var(--_line-soft, color-mix(in srgb, var(--fg) 14%, transparent))", track: true);
			_overlay.Add(track);
			var elements = new List<XElement>();
			for (var i = 0; i < slots; i++)
			{
				var slot = Overlay.Slot(target.Box, i, slots, i >= warnFrom ? _colors.Warn : "var(--accent, #3b82f6)", track: false);
				_overlay.Add(slot);
				elements.Add(slot);
			}
			meter = new Meter(capacity, elements, track);
			_meters[target.Id] = meter;
			Opacity(track, ReducedMotion.Cut, 0).Animate(at, _spec.Enter, Channel.Value(1), Easing.Enter);
		}
		else if (fill.Capacity is { } capacity && capacity != meter.Capacity)
			_diagnostics.Add(new(fill.Line, $"{target.Id} was filled with a capacity of {meter.Capacity}, not {capacity}."));

		if (fill.Count > meter.Capacity)
		{
			_diagnostics.Add(new(fill.Line, $"{fill.Count} is more than {target.Id}'s capacity of {meter.Capacity}."));
			return at;
		}

		var lit = (int)Math.Round(fill.Count * (double)meter.Slots.Count / meter.Capacity);
		var end = at;
		var step = _spec.Stagger * 0.7;
		if (lit > meter.Shown)
		{
			for (var i = meter.Shown; i < lit; i++)
			{
				var t = at + ((i - meter.Shown) * step);
				Opacity(meter.Slots[i], ReducedMotion.Cut, 0).Animate(t, _spec.Shift, Channel.Value(1), Easing.Enter);
				end = t + _spec.Shift;
			}
		}
		else
		{
			for (var i = meter.Shown - 1; i >= lit; i--)
			{
				var t = at + ((meter.Shown - 1 - i) * step);
				Opacity(meter.Slots[i], ReducedMotion.Cut, 0).Animate(t, _spec.Shift, Channel.Value(0), Easing.Exit);
				end = t + _spec.Shift;
			}
		}
		meter.Shown = lit;
		return end;
	}

	/// <summary>A badge above a node, replacing the one it had; a badge stays until cleared or the score ends.</summary>
	private double BadgeAll(BadgeStatement badge, double at)
	{
		foreach (var target in Resolve(badge.Targets))
		{
			if (_badges.TryGetValue(target.Id, out var previous))
			{
				Opacity(previous, ReducedMotion.Cut, 0).Animate(at, _spec.Exit, Channel.Value(0), Easing.Exit);
				_ = _badges.Remove(target.Id);
			}
			if (badge.Text is null)
				continue;
			var element = Overlay.Badge(
				badge.Text,
				badge.Role is { } role ? _colors.For(role) : "var(--accent, #3b82f6)",
				target.Box,
				beside: _scene.Kind == SceneKind.Gantt
			);
			(target.Elements.Count > 0 && target.Kind == TargetKind.Node ? Outer(target.Elements[0]) : _overlay).Add(element);
			_badges[target.Id] = element;
			var opacity = Opacity(element, ReducedMotion.Cut, 0);
			opacity.Animate(at + (previous is null ? 0 : _spec.Exit * 0.5), _spec.Enter, Channel.Value(1), Easing.Enter);
		}
		return at + _spec.Enter;
	}

	/// <summary>
	/// A node that does not answer: a dash runs around it for the length of a wait. Waiting is the failure a timeout is
	/// made of, so it is shown, not only its result.
	/// </summary>
	private double Hang(SceneTarget target, double at)
	{
		var ring = Overlay.HangRing(target.Box, _colors.Warn);
		_overlay.Add(ring);
		var opacity = ChannelFor(ring, Property.Opacity, Channel.Value(0), ReducedMotion.Cut);
		var dash = ChannelFor(ring, Property.DashOffset, Channel.Value(0), ReducedMotion.Off);
		opacity.Animate(at, _spec.Shift * 0.6, Channel.Value(1), Easing.Enter);
		dash.Animate(at, _spec.Hang, Channel.Value(-_spec.Hang / 900), Easing.Linear);
		opacity.Animate(at + _spec.Hang - (_spec.Exit * 0.5), _spec.Exit, Channel.Value(0), Easing.Exit);
		return at + _spec.Hang;
	}

	/// <summary>A bracket beside a span of sequence messages with its label, kept until the score ends.</summary>
	private double Note(NoteStatement note, double at)
	{
		if (_scene.Kind != SceneKind.Sequence)
			return NoteMarks(note, at);
		if (note.Last > _scene.Edges.Count)
		{
			_diagnostics.Add(new(note.Line, $"The diagram has {_scene.Edges.Count} messages, so there is no #{note.Last}."));
			return at;
		}
		var span = _scene.Edges.Skip(note.First - 1).Take(note.Last - note.First + 1).ToList();
		var x = span.Max(m => Math.Max(m.Path.Start.X, m.Path.End.X)) + 14;
		var y1 = span.Min(m => Math.Min(m.Path.Start.Y, m.Path.End.Y));
		var y2 = span.Max(m => Math.Max(m.Path.Start.Y, m.Path.End.Y));
		if (y2 - y1 < 12)
		{
			y1 -= 6;
			y2 += 6;
		}
		return ShowBracket(Overlay.Bracket(x, y1, y2, note.Text, NoteColor(note)), at);
	}

	private string NoteColor(NoteStatement note) => note.Role is { } role ? _colors.For(role) : "var(--accent, #3b82f6)";

	private double ShowBracket(XElement bracket, double at)
	{
		_overlay.Add(bracket);
		Opacity(bracket, ReducedMotion.Cut, 0).Animate(at, _spec.Enter, Channel.Value(1), Easing.Enter);
		return at + _spec.Enter;
	}

	/// <summary>
	/// A bracket over a span of numbered marks (#2..#4: gantt tasks, timeline periods, commits, journey tasks, packet
	/// fields, bars), with what the span costs or means. A span that runs down the page is bracketed on its right; one
	/// that runs across, underneath (above, where underneath is off the diagram).
	/// </summary>
	private double NoteMarks(NoteStatement note, double at)
	{
		var marks = _scene.Ordered;
		if (marks.Count == 0)
		{
			_diagnostics.Add(
				new(
					note.Line,
					"note brackets a span of numbered things: sequence messages, gantt tasks, timeline periods, commits. To label a node, use badge."
				)
			);
			return at;
		}
		if (note.Last > marks.Count)
		{
			_diagnostics.Add(new(note.Line, $"The diagram has {marks.Count} numbered marks, so there is no #{note.Last}."));
			return at;
		}
		// a mark with what joined it (a period's events, a task's face) is one block
		static Box Block(SceneTarget m) =>
			m
				.Elements
				.Concat(m.Followers.SelectMany(f => f.Elements))
				.Select(SvgScene.BoxOf)
				.Where(b => !b.IsEmpty)
				.Aggregate(m.Box, (a, b) => a.IsEmpty ? b : a.Union(b));

		var span = marks.Skip(note.First - 1).Take(note.Last - note.First + 1).Select(Block).Where(b => !b.IsEmpty).ToList();
		if (span.Count == 0)
			return at;
		var box = span.Aggregate((a, b) => a.Union(b));
		// marks side by side (periods, commits, bars) run across; marks that share columns (gantt rows) run down
		var across = span.Count == 1
			? box.Width >= box.Height
			: span.Skip(1).Select((next, i) => (Prev: span[i], Next: next)).All(p => p.Prev.Right <= p.Next.X + 1 || p.Next.Right <= p.Prev.X + 1);
		var color = NoteColor(note);
		if (!across)
			return ShowBracket(Overlay.Bracket(box.Right + 14, box.Y, box.Bottom, note.Text, color), at);
		var above = box.Bottom + 34 > _stage.Bottom;
		return ShowBracket(Overlay.BracketAcross(box.X, box.Right, above ? box.Y - 8 : box.Bottom + 8, note.Text, color, above), at);
	}
}
