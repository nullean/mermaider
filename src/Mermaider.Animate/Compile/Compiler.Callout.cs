using System.Xml.Linq;
using Mermaider.Animate.Scene;
using Mermaider.Animate.Score;
using Mermaider.Animate.Timeline;

namespace Mermaider.Animate.Compile;

/// <summary>
/// Callouts: a short label clear of the diagram with a leader line to what it explains. Captions say what happens; a
/// callout says where. Every callout is placed before anything is drawn, so the stage grows on the sides that need room,
/// once, and a callout's leader runs through empty space: each takes the side (right, left or below) whose straight
/// line from its subject crosses no other mark, the nearest of those, and callouts in the same step stack clear of each
/// other.
/// </summary>
internal sealed partial class Compiler
{
	private enum Side
	{
		Right,
		Left,
		Below,
	}

	private sealed record Placement(Vec Anchor, Vec Stub, Side Side, double X, double Y, IReadOnlyList<string> Lines);

	private const double CalloutGap = 28;
	private const double CalloutLine = 16;
	private const double CalloutCharWidth = 6.9;
	private const int CalloutWrap = 28;

	private readonly Dictionary<CalloutStatement, Placement> _callouts = [];

	/// <summary>Places every callout in the score and makes room for them round the diagram.</summary>
	private void PlaceCallouts(IReadOnlyList<ScoreStep> steps)
	{
		var stage = _stage;
		double right = 0, left = 0, below = 0;
		foreach (var step in steps)
		{
			var taken = new List<Box>();
			foreach (var callout in step.Statements.OfType<CalloutStatement>())
			{
				if (Subject(callout, quiet: true, out var along) is not { } subject)
					continue;
				var lines = Wrap(callout.Text);
				var width = lines.Max(l => l.Length) * CalloutCharWidth;
				var height = lines.Count * CalloutLine;
				var side = BestSide(subject, stage);
				var anchor = side switch
				{
					Side.Right => new Vec(subject.Right, subject.Center.Y),
					Side.Left => new Vec(subject.X, subject.Center.Y),
					_ => new Vec(subject.Center.X, subject.Bottom),
				};
				// a leader that would run along its edge first steps off it, diagonally, towards its side
				var stub = anchor;
				var outward = side == Side.Left ? -10 : 10;
				if (along is { } d && side != Side.Below && Math.Abs(d.X) > Math.Abs(d.Y))
					stub = new Vec(subject.Center.X + outward, subject.Center.Y + 10);
				else if (along is { } v && side == Side.Below && Math.Abs(v.Y) >= Math.Abs(v.X))
					stub = new Vec(subject.Center.X + 10, subject.Center.Y + 10);
				if (along is not null)
					anchor = subject.Center;
				// the label's box, beside its subject on its side, then moved along the side until it is clear
				var box = side switch
				{
					Side.Right => new Box(stage.Right + CalloutGap, stub.Y - (height / 2), width, height),
					Side.Left => new Box(stage.X - CalloutGap - width, stub.Y - (height / 2), width, height),
					_ => new Box(Math.Max(stage.X, stub.X - (width / 2)), stage.Bottom + CalloutGap - 8, width, height),
				};
				while (taken.Where(t => Overlaps(t, box)).Select(t => (Box?)t).FirstOrDefault() is { } clash)
					box = side == Side.Below ? box with { Y = clash.Bottom + 4 } : box with { Y = clash.Bottom + 6 };
				taken.Add(box);
				_callouts[callout] = new Placement(anchor, stub, side, box.X, box.Y, lines);
				switch (side)
				{
					case Side.Right:
						right = Math.Max(right, box.Right - stage.Right + 12);
						break;
					case Side.Left:
						left = Math.Max(left, stage.X - box.X + 12);
						break;
					default:
						below = Math.Max(below, box.Bottom - stage.Bottom + 12);
						break;
				}
			}
		}
		if (right + left + below > 0)
			GrowStage(left, right, below);
	}

	/// <summary>
	/// The side the leader reaches with the fewest marks in its way, then the shortest. Labels are wider than they are
	/// tall, so a side wins over below when they tie.
	/// </summary>
	private Side BestSide(Box subject, Box stage)
	{
		var others = _scene.Targets.Where(t => !t.Box.IsEmpty && !Contains(t.Box, subject) && !Contains(subject, t.Box)).Select(t => t.Box).ToList();
		var center = subject.Center;

		int Crossing(double x1, double y1, double x2, double y2) =>
			others.Count(
				b => y1 == y2
					? b.Y < y1 && b.Bottom > y1 && b.Right > Math.Min(x1, x2) && b.X < Math.Max(x1, x2)
					: b.X < x1 && b.Right > x1 && b.Bottom > Math.Min(y1, y2) && b.Y < Math.Max(y1, y2)
			);

		(Side Side, int Crossings, double Length)[] options =
		[
			(Side.Right, Crossing(subject.Right, center.Y, stage.Right, center.Y), stage.Right - subject.Right),
			(Side.Left, Crossing(stage.X, center.Y, subject.X, center.Y), subject.X - stage.X),
			(Side.Below, Crossing(center.X, subject.Bottom, center.X, stage.Bottom), stage.Bottom - subject.Bottom + 40),
		];
		return options.OrderBy(o => o.Crossings).ThenBy(o => o.Length).First().Side;
	}

	private static bool Contains(Box outer, Box inner) =>
		outer.X <= inner.X && outer.Y <= inner.Y && outer.Right >= inner.Right && outer.Bottom >= inner.Bottom;

	private static bool Overlaps(Box a, Box b) => a.X < b.Right + 8 && b.X < a.Right + 8 && a.Y < b.Bottom + 4 && b.Y < a.Bottom + 4;

	/// <summary>The text in lines of at most <see cref="CalloutWrap"/> characters, broken between words.</summary>
	private static List<string> Wrap(string text)
	{
		var lines = new List<string>();
		var line = "";
		foreach (var word in text.Split([' '], StringSplitOptions.RemoveEmptyEntries))
		{
			if (line.Length > 0 && line.Length + 1 + word.Length > CalloutWrap)
			{
				lines.Add(line);
				line = word;
			}
			else
				line = line.Length == 0 ? word : $"{line} {word}";
		}
		if (line.Length > 0 || lines.Count == 0)
			lines.Add(line);
		return lines;
	}

	/// <summary>
	/// What a callout points at, as a box: a node, or a point on an edge, with the edge's direction there in
	/// <paramref name="along"/>. An edge with a label is pointed at past it, so the dot does not sit on the label.
	/// </summary>
	private Box? Subject(CalloutStatement callout, bool quiet, out Vec? along)
	{
		var mark = _diagnostics.Count;
		Box? subject = null;
		along = null;
		if (callout.Target is { } target && Resolve([target]).FirstOrDefault() is { Box.IsEmpty: false } node)
			subject = node.Box;
		else if (callout.Edge is { } edgeRef && ResolveEdges([edgeRef]).FirstOrDefault() is { } edge)
		{
			var at = edge.Labels.Count > 0 ? 0.72 : 0.5;
			var point = edge.Path.At(at);
			along = edge.Path.At(Math.Min(1, at + 0.02)) - edge.Path.At(Math.Max(0, at - 0.02));
			subject = new Box(point.X - 2, point.Y - 2, 4, 4);
		}
		if (quiet)
			_diagnostics.RemoveRange(mark, _diagnostics.Count - mark);
		return subject;
	}

	/// <summary>The callout fades in with its leader, and out when its step ends.</summary>
	private double Callout(CalloutStatement callout, double at)
	{
		if (Subject(callout, quiet: false, out _) is null || !_callouts.TryGetValue(callout, out var placement))
			return at;
		var color = callout.Role is { } role ? _colors.For(role) : "var(--accent, #3b82f6)";
		var element = Overlay.Callout(
			placement.Anchor,
			placement.Stub,
			placement.Side switch
			{
				Side.Right => 0,
				Side.Left => 1,
				_ => 2,
			},
			placement.X,
			placement.Y,
			placement.Lines,
			color,
			CalloutLine
		);
		_overlay.Add(element);
		var opacity = Opacity(element, ReducedMotion.Cut, 0);
		opacity.Animate(at, _spec.Enter, Channel.Value(1), Easing.Enter);
		_stepEnd.Add(end => opacity.Animate(end, _spec.Exit, Channel.Value(0), Easing.Exit));
		return at + _spec.Enter + _spec.ReadTime(callout.Text);
	}

	/// <summary>Grows the view box on the left, right and bottom, keeping the diagram where it is.</summary>
	private void GrowStage(double left, double right, double below)
	{
		var vb = _scene.ViewBox;
		_scene.ViewBox = new Box(vb.X - left, vb.Y, vb.Width + left + right, vb.Height + below);
		var root = _scene.Root;
		root.SetAttributeValue("viewBox", $"{Num.F(vb.X - left)} {Num.F(vb.Y)} {Num.F(vb.Width + left + right)} {Num.F(vb.Height + below)}");
		var width = Num.Parse((string?)root.Attribute("width"));
		if (!double.IsNaN(width))
			root.SetAttributeValue("width", Num.F(width + left + right));
		var height = Num.Parse((string?)root.Attribute("height"));
		if (!double.IsNaN(height))
			root.SetAttributeValue("height", Num.F(height + below));
	}
}
