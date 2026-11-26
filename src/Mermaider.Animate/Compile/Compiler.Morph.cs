using System.Xml.Linq;
using Mermaider.Animate.Scene;
using Mermaider.Animate.Score;
using Mermaider.Animate.Timeline;

namespace Mermaider.Animate.Compile;

/// <summary>
/// becomes: the diagram moves into its next version. Each version is rendered and laid out on its own, as it would be
/// without the animation, and both are in the picture, one shown at a time. The morph runs in four beats: what the next
/// version does not have leaves (nodes, and every edge, since edges are routed anew); what both share moves to its new
/// place; the versions swap, where the shared nodes already stand, so nothing jumps; and what is new arrives, nodes
/// fading in and edges drawing. From then on the score names the next version's elements.
/// </summary>
internal sealed partial class Compiler
{
	private readonly Func<string, string>? _render;
	private readonly Dictionary<BecomesStatement, SvgScene> _versions = [];
	private XElement? _layer;

	/// <summary>
	/// Renders every version the score moves into, before anything is drawn, so the stage grows once to hold the largest
	/// of them. A version of another diagram type, or one that does not render, is reported on its line.
	/// </summary>
	private void PrepareVersions(IReadOnlyList<ScoreStep> steps)
	{
		var becomes = steps.SelectMany(s => s.Statements).OfType<BecomesStatement>().ToList();
		if (becomes.Count == 0 || _render is null)
			return;
		if (_score.Highlights.Count > 0)
			_diagnostics.Add(
				new(
					_score.Highlights[0].Line,
					"highlight holds for the whole animation, so it cannot follow a diagram that becomes another; mark or pulse in the steps instead."
				)
			);
		var bounds = _scene.ViewBox;
		foreach (var statement in becomes)
		{
			string svg;
			try
			{
				svg = _render(statement.Source);
			}
			catch (Exception e) when (e is not OutOfMemoryException)
			{
				_diagnostics.Add(new(statement.FirstLine, $"The diagram under becomes does not render: {e.Message}"));
				continue;
			}
			var version = SvgScene.Read(svg, statement.Source, _scene.Prefix);
			if (version.Kind != _scene.Kind)
			{
				_diagnostics.Add(
					new(
						statement.FirstLine,
						$"becomes moves a diagram into its next version, so it is the same type: this is a {version.Kind}, the diagram is a {_scene.Kind}."
					)
				);
				continue;
			}
			_versions[statement] = version;
			bounds = bounds.Union(version.ViewBox);
		}
		var vb = _scene.ViewBox;
		var (left, right, below) = (Math.Max(0, vb.X - bounds.X), Math.Max(0, bounds.Right - vb.Right), Math.Max(0, bounds.Bottom - vb.Bottom));
		if (left + right + below > 0)
			GrowStage(left, right, below);
		_stage = _scene.ViewBox;
		foreach (var version in _versions.Values)
			version.ViewBox = _scene.ViewBox;
	}

	private double Becomes(BecomesStatement statement, double at)
	{
		if (!_versions.TryGetValue(statement, out var next))
			return at;
		var previous = _scene;

		// the version on show becomes a layer of its own, and the next one goes in beside it, hidden
		_layer ??= Layer(_camera.Elements().Where(e => e != _overlay).ToList());
		var incoming = Import(next);
		var shown = Opacity(_layer, ReducedMotion.Cut, 1);
		var hidden = Opacity(incoming, ReducedMotion.Cut, 0);

		var shared = new List<(SceneTarget From, SceneTarget To)>();
		foreach (var target in previous.Targets.Concat(previous.Groups))
			if ((next.Target(target.Id) ?? next.Group(target.Id)) is { } match && !target.Box.IsEmpty && !match.Box.IsEmpty)
				shared.Add((target, match));
		var staying = new HashSet<SceneTarget>(shared.Select(s => s.From));
		var arriving = next.Targets.Concat(next.Groups).Where(t => (previous.Target(t.Id) ?? previous.Group(t.Id)) is null).ToList();

		// a split brings new nodes out of an old one; a merge takes old nodes into a new one
		var sources = Resolve(statement.From);
		_scene = next;
		var results = Resolve(statement.Into);
		_scene = previous;
		var split = sources.Count == 1 && statement.Into.Count > 0 ? sources[0] : null;
		var merge = statement.From.Count > 1 && results.Count == 1 ? results[0] : null;
		var emerging = split is null ? [] : new HashSet<SceneTarget>(results.Where(arriving.Contains));
		var converging = merge is null ? [] : new HashSet<SceneTarget>(sources.Where(t => !staying.Contains(t)));
		if (split is not null && !staying.Contains(split))
			converging.Add(split);

		// 1. what is not in the next version leaves, and every edge with it; a split's source and a merge's parts stay
		foreach (var target in previous.Targets.Concat(previous.Groups).Where(t => !staying.Contains(t) && !converging.Contains(t)))
			foreach (var element in target.Elements)
				Visibility(element).Animate(at, _spec.Exit, Channel.Value(0), Easing.Exit);
		foreach (var edge in previous.Edges)
		{
			foreach (var element in edge.Labels.Prepend(edge.Element))
				Visibility(element).Animate(at, _spec.Exit, Channel.Value(0), Easing.Exit);
			if (_edgeVisuals.TryGetValue(edge, out var visual))
				foreach (var piece in new[] { visual.Arrow, visual.ArrowStart, visual.Cross })
					if (piece is not null)
						Opacity(piece, ReducedMotion.Cut).Animate(at, _spec.Exit, Channel.Value(0), Easing.Exit);
		}
		Forget(at);

		// 2. what both share moves to where the next version has it
		var move = at + (_spec.Exit * 0.6);
		foreach (var (from, to) in shared)
		{
			var by = to.Box.Center - from.Box.Center;
			foreach (var element in from.Elements)
			{
				var box = SvgScene.BoxOf(element);
				var movement = Movement(element, box.IsEmpty ? from.Box.Center : box.Center);
				var now = movement.ValueAt(move);
				movement.Animate(move, _spec.Camera, Channel.Transform(now[0] + by.X, now[1] + by.Y, 1), Easing.Move);
			}
		}
		if (merge is not null)
			foreach (var part in converging)
				foreach (var element in part.Elements)
				{
					var box = SvgScene.BoxOf(element);
					var movement = Movement(element, box.IsEmpty ? part.Box.Center : box.Center);
					var by = merge.Box.Center - part.Box.Center;
					movement.Animate(move, _spec.Camera, Channel.Transform(by.X, by.Y, 1), Easing.Move);
				}

		// 3. the versions swap where the shared nodes stand: a shape or a label that changed crossfades
		var swap = move + _spec.Camera;
		foreach (var target in arriving)
			foreach (var element in target.Elements)
				Visibility(element).Initial[0] = 0;
		shown.Animate(swap, _spec.Shift * 0.6, Channel.Value(0), Easing.Move);
		hidden.Animate(swap, _spec.Shift * 0.6, Channel.Value(1), Easing.Move);
		_layer = incoming;

		// 4. what is new arrives: nodes fade in, then the edges draw between them. In a split they come out of their
		// source, where it stands now, and move to their places; a merge's result is there as the parts arrive.
		var arrive = swap + (_spec.Shift * 0.6);
		var end = arrive;
		if (split is not null)
		{
			var origin = shared.FirstOrDefault(s => s.From == split) is { To: { } kept } ? kept.Box.Center : split.Box.Center;
			foreach (var target in emerging)
			{
				foreach (var element in target.Elements)
				{
					var box = SvgScene.BoxOf(element);
					var movement = Movement(element, box.IsEmpty ? target.Box.Center : box.Center);
					// a step, not the start value: with motion reduced the node rests in its own place
					var from = origin - target.Box.Center;
					movement.Set(swap, Channel.Transform(from.X, from.Y, 1));
					movement.Animate(arrive, _spec.Camera, Channel.Transform(0, 0, 1), Easing.Move);
					Visibility(element).Animate(swap, _spec.Shift * 0.6, Channel.Value(1), Easing.Enter);
				}
				end = Math.Max(end, arrive + _spec.Camera);
			}
		}
		if (merge is not null && arriving.Contains(merge))
		{
			foreach (var element in merge.Elements)
				Visibility(element).Animate(swap, _spec.Shift * 0.6, Channel.Value(1), Easing.Enter);
			_ = arriving.Remove(merge);
		}
		arriving.RemoveAll(emerging.Contains);
		// in a split, the rest waits for the new nodes to reach their places
		var rest = emerging.Count > 0 ? arrive + (_spec.Camera * 0.6) : arrive;
		for (var i = 0; i < arriving.Count; i++)
		{
			var t = rest + (i * _spec.Stagger * 0.5);
			foreach (var element in arriving[i].Elements)
				Visibility(element).Animate(t, _spec.Enter, Channel.Value(1), Easing.Enter);
			end = Math.Max(end, t + _spec.Enter);
		}
		var draw = arriving.Count > 0 ? rest + (_spec.Enter * 0.6) : emerging.Count > 0 ? arrive + (_spec.Camera * 0.7) : arrive;
		// the stage has grown since the versions were read (callouts, the caption band): the picture is one
		next.ViewBox = previous.ViewBox;
		_scene = next;
		foreach (var edge in next.Edges)
			end = Math.Max(end, Draw(edge, draw));
		return end;
	}

	/// <summary>Wraps the elements of the version on show in a group, so it can leave as one.</summary>
	private XElement Layer(List<XElement> elements)
	{
		var layer = new XElement(SvgScene.Ns + "g");
		_overlay.AddBeforeSelf(layer);
		foreach (var element in elements)
		{
			element.Remove();
			layer.Add(element);
		}
		return layer;
	}

	/// <summary>
	/// Brings the next version's drawing into this picture, under the overlay, with the definitions it needs that are not
	/// here already (both versions share a theme, so most are).
	/// </summary>
	private XElement Import(SvgScene next)
	{
		var layer = new XElement(SvgScene.Ns + "g");
		_overlay.AddBeforeSelf(layer);
		var known = new HashSet<string>(_scene.Root.Descendants().Select(e => (string?)e.Attribute("id")).OfType<string>(), StringComparer.Ordinal);
		foreach (var node in next.Root.Nodes().ToList())
		{
			if (node is XElement { Name.LocalName: "style" or "title" or "desc" })
				continue;
			node.Remove();
			if (node is XElement { Name.LocalName: "defs" } defs)
			{
				foreach (var definition in defs.Elements().Where(d => (string?)d.Attribute("id") is not { } id || !known.Contains(id)).ToList())
				{
					definition.Remove();
					_defs.Add(definition);
				}
				continue;
			}
			layer.Add(node);
		}
		return layer;
	}

	/// <summary>
	/// What the score built on the version on show (marks, badges, meters, rings, counts, failures) goes with it: the next
	/// version starts clean, and a mark there is drawn anew.
	/// </summary>
	private void Forget(double at)
	{
		var leaving = _marks
			.Values
			.Concat(_badges.Values)
			.Concat(_rings.Values.SelectMany(r => new[] { r.Ring, r.Trail }))
			.Concat(_counts.Values)
			.Concat(_meters.Values.SelectMany(m => m.Slots.Append(m.Track)));
		foreach (var element in leaving.Distinct())
			Opacity(element, ReducedMotion.Cut).Animate(at, _spec.Exit, Channel.Value(0), Easing.Exit);
		_marks.Clear();
		_badges.Clear();
		_rings.Clear();
		_counts.Clear();
		_meters.Clear();
		_counted.Clear();
		_failCuts.Clear();
		_cascaded.Clear();
		foreach (var outline in _ghosts.Values.Select(g => g.Outline))
			Opacity(outline, ReducedMotion.Cut).Animate(at, _spec.Exit, Channel.Value(0), Easing.Exit);
		_ghosts.Clear();
		_replicas.Clear();
	}
}
