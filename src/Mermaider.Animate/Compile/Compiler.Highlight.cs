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
		foreach (var highlight in _score.Highlights.Concat(_defaultHighlights))
		{
			var color = highlight.Role is { } role ? _colors.For(role) : "var(--accent, #3b82f6)";
			switch (highlight.Kind)
			{
				case HighlightKind.Node when highlight.Pick is { } pick:
					if (Pick(pick, highlight.Line) is { } picked)
						Glow(picked, color, from, to);
					break;
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
	/// A mark glows: a ring in the role's colour that stays, and a halo round it that breathes in and out, moving and
	/// appearing with its mark. Some marks glow their own way: a pie slice pulls out of the pie and breathes, a venn overlap
	/// lights its region, a radar curve breathes out from the centre, and a small round mark takes a round ring.
	/// </summary>
	private void Glow(SceneTarget target, string color, double from, double to)
	{
		if (target.Box.IsEmpty)
			return;
		var (cycles, length) = Cycles(from, to, BreathPeriod);
		if (_scene.Kind == SceneKind.Pie && target.Radius > 0)
		{
			// out along the slice's middle, breathing a little further and back. Untouched by a story, it rests pulled out from
			// the start, so the loop begins where it ends and reduced motion shows it out of the pie, still.
			var middle = (target.StartAngle + target.EndAngle) / 2 * Math.PI / 180;

			double[] Out(double by) => Channel.Transform(Math.Cos(middle) * by, Math.Sin(middle) * by, 1);

			var pull = Movement(target.Elements[0], target.Center);
			if (!pull.IsAnimated)
				Out(7).CopyTo(pull.Initial, 0);
			for (var i = 0; i < cycles; i++)
			{
				pull.Animate(from + (i * length), length / 2, Out(11), Easing.Move);
				pull.Animate(from + (i * length) + (length / 2), length / 2, Out(7), Easing.Move);
			}
			return;
		}
		if (_scene.Kind == SceneKind.Radar)
		{
			var grow = Movement(target.Elements[0], target.Center);
			for (var i = 0; i < cycles; i++)
			{
				grow.Animate(from + (i * length), length / 2, Channel.Transform(0, 0, 1.1), Easing.Move);
				grow.Animate(from + (i * length) + (length / 2), length / 2, Channel.Transform(0, 0, 1), Easing.Move);
			}
			return;
		}
		if (_scene.Unions.FirstOrDefault(u => u.Union == target) is { Union: not null } union)
		{
			// a region no story lit is lit from the start, and only breathes; one a story lit breathes once it is lit
			var settled = 0.0;
			if (!_regions.ContainsKey(target) && Region(target, union.Sets) is { } made)
			{
				_regions[target] = made;
				_ = Opacity(made, ReducedMotion.Off, 1);
			}
			else
			{
				Light(target, from);
				settled = _spec.Enter;
			}
			if (_regions.TryGetValue(target, out var region))
			{
				var lit = Opacity(region, ReducedMotion.Cut, 0);
				for (var i = 0; i < cycles; i++)
				{
					var at = from + (i * length) + (i == 0 ? settled : 0);
					lit.Animate(at, from + (i * length) + (length / 2) - at, Channel.Value(0.6), Easing.Move);
					lit.Animate(from + (i * length) + (length / 2), length / 2, Channel.Value(1), Easing.Move);
				}
			}
			return;
		}
		var holder = target.Elements.Count > 0 && target.Kind == TargetKind.Node ? Outer(target.Elements[0]) : _overlay;
		var small = (target.Box.Width <= 40 && target.Box.Height <= 40) ||
			target.Elements[0].DescendantsAndSelf().FirstOrDefault(e => e.Name.LocalName is "rect" or "circle" or "ellipse" or "polygon" or "path") is
			{
				Name.LocalName: "circle" or "ellipse"
			};
		var ring = Overlay.Ring(target.Box, color, 2, 4, color, 0.1, round: small);
		ring.SetAttributeValue("opacity", null);
		var halo = Overlay.Ring(target.Box, color, 6, 7, round: small);
		halo.SetAttributeValue("opacity", null);
		halo.SetAttributeValue("stroke-opacity", "0.22");
		holder.Add(halo, ring);

		// the halo swells and fades, the ring holds; reduced motion keeps the halo at its rest
		var breath = ChannelFor(halo, Property.Opacity, Channel.Value(0.35), ReducedMotion.Off);
		var swell = ChannelFor(halo, Property.Transform, Channel.Transform(0, 0, 1), ReducedMotion.Off);
		swell.Origin = new Vec(target.Box.Center.X - _stage.X, target.Box.Center.Y - _stage.Y);
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
		if (_scene.Kind == SceneKind.Sankey && Band(edge) is { } band)
		{
			BandFlow(edge, band.Centre, band.Width, from, to);
			return;
		}
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

	/// <summary>
	/// A sankey band flows as pulses of light the width of the band, travelling along its middle: a thin dashed line would
	/// read as a different edge laid over it. The pulses are clipped to the band, so they keep to its shape on the bends.
	/// </summary>
	private void BandFlow(SceneEdge edge, Polyline centre, double width, double from, double to)
	{
		const double dashLength = 26;
		var gap = Math.Max(70, Math.Min(150, centre.Length / 3));
		var pattern = dashLength + gap;
		var clip = NextId("c");
		_defs.Add(
			new XElement(
				SvgScene.Ns + "clipPath",
				new XAttribute("id", clip),
				new XElement(SvgScene.Ns + "path", new XAttribute("d", (string)edge.Element.Attribute("d")!))
			)
		);
		var pulse = Overlay.Stroke(centre, "var(--bg, #fff)", width * 1.6, $"{Num.F(dashLength)} {Num.F(gap)}");
		pulse.SetAttributeValue("opacity", "0.45");
		pulse.SetAttributeValue("clip-path", $"url(#{clip})");
		pulse.SetAttributeValue("pointer-events", "none");
		_overlay.AddFirst(pulse);
		var distance = _spec.StreamSpeed * ((to - from) / 1000);
		var steps = Math.Max(1, Math.Round(distance / pattern));
		ChannelFor(pulse, Property.DashOffset, Channel.Value(0), ReducedMotion.Off).Animate(from, to - from, Channel.Value(-(steps * pattern)), Easing.Linear);
	}

	/// <summary>
	/// A sankey link's middle and thickness, read from its band: <c>M x1 y1 C … x2 y2 L x2 y2b C … x1 y1b Z</c>, two
	/// horizontal-tangent curves, so the middle is the same curve through the midpoints of its ends.
	/// </summary>
	private static (Polyline Centre, double Width)? Band(SceneEdge edge)
	{
		var numbers = ((string?)edge.Element.Attribute("d") ?? "")
			.Split([' ', ',', 'M', 'C', 'L', 'Z'], StringSplitOptions.RemoveEmptyEntries)
			.Select(Num.Parse)
			.ToArray();
		if (numbers.Length < 16)
			return null;
		double x1 = numbers[0], y1 = numbers[1], cx1 = numbers[2], cx2 = numbers[4], x2 = numbers[6], y2 = numbers[7], y2b = numbers[9], y1b = numbers[15];
		var width = Math.Abs(y1b - y1);
		if (width <= 0)
			return null;
		double ya = (y1 + y1b) / 2, yb = (y2 + y2b) / 2;
		var points = new List<Vec>();
		for (var i = 0; i <= 24; i++)
		{
			var t = i / 24.0;
			var u = 1 - t;
			points.Add(
				new Vec(
					(u * u * u * x1) + (3 * u * u * t * cx1) + (3 * u * t * t * cx2) + (t * t * t * x2),
					(u * u * u * ya) + (3 * u * u * t * ya) + (3 * u * t * t * yb) + (t * t * t * yb)
				)
			);
		}
		return (new Polyline(points), width);
	}

	private readonly List<HighlightStatement> _defaultHighlights = [];

	/// <summary>
	/// The mark the data picks. <c>biggest</c>: the largest slice, bar or tile, the radar's widest curve, a venn's
	/// labelled overlap (else its largest set), the widest packet field, the quadrant's top-right point. <c>lowest</c>: the
	/// smallest slice or bar, a journey's lowest-scored task.
	/// </summary>
	private SceneTarget? Pick(string pick, int line)
	{
		var marks = _scene.Ordered;

		double Data(SceneTarget t, string attribute) => Num.Parse((string?)t.Elements[0].Attribute(attribute));

		double Area(SceneTarget t) => t.Box.Width * t.Box.Height;

		SceneTarget? picked = (_scene.Kind, pick) switch
		{
			(SceneKind.Pie or SceneKind.XyChart, "biggest") => marks.Where(m => m.Value != 0).OrderByDescending(m => m.Value).FirstOrDefault(),
			(SceneKind.Pie or SceneKind.XyChart, "lowest") => marks.Where(m => m.Value != 0).OrderBy(m => m.Value).FirstOrDefault(),
			(SceneKind.Treemap, "biggest") =>
				marks.Where(m => SvgScene.HasClass(m.Elements[0], "treemap-tile")).OrderByDescending(m => Data(m, "data-value")).FirstOrDefault(),
			(SceneKind.Radar, "biggest") => marks.OrderByDescending(Area).FirstOrDefault(),
			(SceneKind.Venn, "biggest") => _scene.Unions.Select(u => u.Union).FirstOrDefault() ?? marks.OrderByDescending(Area).FirstOrDefault(),
			(SceneKind.Packet, "biggest") => marks.OrderByDescending(m => m.Box.Width).FirstOrDefault(),
			(SceneKind.Quadrant, "biggest") => marks.OrderByDescending(m => m.Box.Center.X - m.Box.Center.Y).FirstOrDefault(),
			(SceneKind.Journey, "lowest") => marks.OrderBy(m => Data(m, "data-score")).FirstOrDefault(),
			_ => null,
		};
		if (picked is null)
			_diagnostics.Add(
				new(
					line,
					$"highlight {pick} picks by the diagram's data: biggest for a pie, bar, tile, radar curve, venn overlap, packet field or quadrant point; lowest for a pie, a bar or a journey."
				)
			);
		return picked;
	}

	/// <summary>
	/// What a bare <c>animate</c> highlights, for the diagram types whose default would only have built them up: the
	/// longest path through a graph flows, a tree's root glows, the busiest kanban column glows, a chart's biggest mark is
	/// singled out, a sankey's biggest flow runs, and a journey's lowest moment glows as a failure. Types whose order is
	/// their content (sequences, git graphs, gantt and timelines, numbered C4 relations, state machines) keep their own.
	/// </summary>
	private bool DefaultHighlights()
	{
		var line = _score.Line;

		HighlightStatement Node(string? pick = null, IReadOnlyList<TargetRef>? targets = null, Role? role = null) =>
			new(HighlightKind.Node, targets ?? [], [], role, line) { Pick = pick };

		HighlightStatement? highlight = _scene.Kind switch
		{
			SceneKind.Pie or SceneKind.XyChart or SceneKind.Treemap or SceneKind.Radar or SceneKind.Venn or SceneKind.Packet or SceneKind.Quadrant =>
				Node("biggest"),
			SceneKind.Journey => Node("lowest", role: Role.Fail),
			SceneKind.Sankey when BiggestFlow() is { Count: >= 2 } flow => new HighlightStatement(HighlightKind.Flow, [], flow, null, line),
			SceneKind.Graph or SceneKind.Architecture when _scene.Tree && _scene.Ordered.FirstOrDefault() is { } root =>
				Node(targets: [new TargetRef(root.Id, false, line)]),
			SceneKind.Graph when Kanban() is { } column => Node(targets: [new TargetRef(column, IsGroup: true, line)]),
			SceneKind.Graph or SceneKind.Architecture when !_scene.Dynamic && LongestPath() is { Count: >= 2 } path =>
				new HighlightStatement(HighlightKind.Flow, [], path, null, line),
			_ => null,
		};
		if (highlight is null)
			return false;
		_defaultHighlights.Add(highlight);
		return true;
	}

	/// <summary>The column with the most cards, on a kanban board.</summary>
	private string? Kanban()
	{
		var columns = _scene.Groups.Where(g => g.Elements.FirstOrDefault() is { } e && SvgScene.HasClass(e, "kanban-column")).ToList();
		return columns.Count == 0 ? null : columns.OrderByDescending(c => _scene.Targets.Count(t => t.Parent == c.Id)).First().Id;
	}

	/// <summary>The longest path through the graph from a node nothing leads into, back edges left out.</summary>
	private List<string> LongestPath()
	{
		var outgoing = _scene
			.Edges
			.Where(e => e.From != e.To)
			.GroupBy(e => e.From)
			.ToDictionary(g => g.Key, g => g.Select(e => e.To).Distinct().ToList(), StringComparer.Ordinal);
		var targets = new HashSet<string>(_scene.Edges.Select(e => e.To), StringComparer.Ordinal);
		var memo = new Dictionary<string, List<string>>(StringComparer.Ordinal);
		var visiting = new HashSet<string>(StringComparer.Ordinal);

		List<string> From(string node)
		{
			if (memo.TryGetValue(node, out var known))
				return known;
			_ = visiting.Add(node);
			var best = new List<string> { node };
			foreach (var next in outgoing.TryGetValue(node, out var nexts) ? nexts : [])
			{
				if (visiting.Contains(next))
					continue;
				var path = From(next);
				if (path.Count + 1 > best.Count)
					best = [node, .. path];
			}
			_ = visiting.Remove(node);
			memo[node] = best;
			return best;
		}

		var starts = outgoing.Keys.Where(n => !targets.Contains(n)).DefaultIfEmpty(outgoing.Keys.FirstOrDefault() ?? "").Where(n => n.Length > 0);
		return starts.Select(From).OrderByDescending(p => p.Count).FirstOrDefault() ?? [];
	}

	/// <summary>A sankey's biggest flow: from the source sending the most, along the biggest link at each node.</summary>
	private List<string> BiggestFlow()
	{
		static double Size(SceneEdge e) => Num.Parse(e.Label);

		var targets = new HashSet<string>(_scene.Edges.Select(e => e.To), StringComparer.Ordinal);
		var source = _scene
			.Edges
			.Where(e => !targets.Contains(e.From))
			.GroupBy(e => e.From)
			.OrderByDescending(g => g.Sum(Size))
			.Select(g => g.Key)
			.FirstOrDefault();
		var path = new List<string>();
		for (var node = source; node is not null && !path.Contains(node); node = _scene
			.Edges
			.Where(e => e.From == node)
			.OrderByDescending(Size)
			.FirstOrDefault()?.To)
			path.Add(node);
		return path;
	}
}
