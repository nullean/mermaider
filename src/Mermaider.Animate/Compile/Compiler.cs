using System.Xml.Linq;
using Mermaider.Animate.Scene;
using Mermaider.Animate.Score;
using Mermaider.Animate.Timeline;

namespace Mermaider.Animate.Compile;

/// <summary>A step as it plays: its caption and when it starts and ends, in milliseconds.</summary>
internal sealed record StepSpan(string? Caption, double Start, double End);

/// <summary>
/// A compiled score. In a loop, <see cref="StoryStart"/> splits the timeline: everything before it (the intro, such as a
/// build) plays once, and the story from there to <see cref="Duration"/> repeats, ending in a settle back to how it began.
/// </summary>
internal sealed record CompiledTimeline(
	IReadOnlyList<Channel> Channels,
	IReadOnlyList<StepSpan> Steps,
	double Duration,
	double ScoreEnd,
	double StoryStart,
	bool Loops
)
{
	/// <summary>The height of the caption band above the diagram, in user units; zero without captions.</summary>
	public double CaptionBand { get; init; }

	/// <summary>The SVG's view box once the caption band is added.</summary>
	public Box ViewBox { get; init; }
}

/// <summary>
/// Turns a score into channels over the scene. Statements in a step run one after another; <c>with</c> starts one
/// alongside the statement before it. Every duration comes from the <see cref="MotionSpec"/>. Things the score uses
/// before they are visible are brought in first (a token never travels an undrawn edge), so a short score still reads.
/// </summary>
internal sealed partial class Compiler
{
	private readonly SvgScene _scene;
	private readonly MotionSpec _spec;
	private readonly ScoreDocument _score;
	private readonly List<AnimationDiagnostic> _diagnostics;
	private readonly RoleColors _colors;
	private readonly PlaybackMode _mode;
	private readonly List<Channel> _channels = [];
	private readonly Dictionary<(XElement, Property), Channel> _byElement = [];
	private readonly Dictionary<XElement, XElement> _outer = [];
	private readonly Dictionary<XElement, XElement> _state = [];
	private readonly Dictionary<SceneEdge, EdgeVisual> _edgeVisuals = [];
	private readonly Dictionary<(string, Role), XElement> _marks = [];
	private readonly Dictionary<string, (XElement Ring, XElement Trail)> _rings = [];
	private readonly List<Action<double>> _stepEnd = [];
	private readonly List<Action<double>> _scoreEnd = [];
	private readonly XElement _overlay;
	private readonly XElement _camera;
	private readonly XElement _sceneRoot;
	private readonly XElement _defs;
	private readonly Box _stage;
	private int _ids;
	private string? _ringAt;
	private int _captionNumber;
	private XElement? _progress;
	private bool _cued;
	private double _captionBand;
	private int _captionTotal;
	private bool _dimmed;
	private bool _zoomed;

	private sealed class EdgeVisual
	{
		public XElement? Mask { get; set; }
		public XElement? Arrow { get; set; }
		public XElement? ArrowStart { get; set; }
		public XElement? Cross { get; set; }
		public bool Drawable => Mask is not null;
	}

	internal Compiler(SvgScene scene, ScoreDocument score, MotionSpec spec, PlaybackMode mode, RoleColors colors, List<AnimationDiagnostic> diagnostics)
	{
		_scene = scene;
		_score = score;
		_spec = spec;
		_mode = mode;
		_colors = colors;
		_diagnostics = diagnostics;
		_stage = scene.ViewBox;

		var root = scene.Root;
		_defs = new XElement(SvgScene.Ns + "defs");
		var content = root.Nodes().Where(n => n is not XElement e || e.Name.LocalName is not ("style" or "defs" or "title" or "desc")).ToList();
		foreach (var node in content)
			node.Remove();
		_overlay = new XElement(SvgScene.Ns + "g", new XAttribute("pointer-events", "none"));
		_camera = new XElement(SvgScene.Ns + "g", content, _overlay);
		_sceneRoot = new XElement(SvgScene.Ns + "g", _camera);
		root.Add(_sceneRoot, _defs);
	}

	internal CompiledTimeline Compile()
	{
		var steps = new List<StepSpan>();
		var cursor = _spec.Lead;
		var introEnd = 0.0;
		var inIntro = true;
		var source = _score.IsDefault ? DefaultSteps() : _score.Steps;
		var captions = source.Any(s => s.Caption is not null || s.Statements.OfType<CaptionStatement>().Any());
		var captionY = _stage.Y - 22;
		if (captions)
		{
			ExtendStage(46);
			_captionBand = 46;
			var widest = source.SelectMany(s => s.Statements.OfType<CaptionStatement>().Select(c => c.Text).Prepend(s.Caption ?? "")).Max(c => c.Length);
			var needed = 14 + 50 + (widest * 8.4) + 14;
			if (needed > _scene.ViewBox.Width)
				WidenStage(needed - _scene.ViewBox.Width);
		}
		_captionTotal = source.Count(s => s.Caption is not null || s.Statements.OfType<CaptionStatement>().Any());

		XElement? previousCaption = null;
		foreach (var step in source)
		{
			var start = cursor;
			XElement? caption = null;
			if (step.Caption is not null || step.Statements.OfType<CaptionStatement>().Any())
				_captionNumber++;
			if (step.Caption is not null)
				caption = ShowCaption(step.Caption, start, captionY, ref previousCaption);

			var lastStart = start;
			var lastEnd = start;
			var first = true;
			foreach (var statement in step.Statements)
			{
				var at = statement.With ? lastStart : first ? start : lastEnd + _spec.Beat;
				if (statement is CaptionStatement c)
				{
					caption = ShowCaption(c.Text, at, captionY, ref previousCaption);
					lastEnd = Math.Max(lastEnd, at + _spec.ReadTime(c.Text));
					continue;
				}
				var end = CompileStatement(statement, at);
				lastStart = at;
				lastEnd = statement.With ? Math.Max(lastEnd, end) : end;
				first = false;
			}

			var stepEnd = Math.Max(lastEnd + _spec.Hold, start + _spec.ReadTime(step.Caption));
			foreach (var action in _stepEnd)
				action(stepEnd);
			_stepEnd.Clear();
			if (_progress is { } bar)
			{
				ChannelFor(bar, Property.DashOffset, Channel.Value(1), ReducedMotion.Off).Animate(start, stepEnd - start, Channel.Value(0), Easing.Linear);
				_progress = null;
			}

			// leading caption-less build steps are the intro: in a loop they play once, and the story repeats after them
			inIntro &= step.Caption is null && step.Statements.All(st => st is BuildStatement);
			if (inIntro)
				introEnd = stepEnd;
			else if (!_cued)
			{
				Cue(step, start);
				_cued = true;
			}
			steps.Add(new StepSpan(step.Caption, start, stepEnd));
			cursor = stepEnd;
			_ = caption;
		}

		var scoreEnd = cursor;
		var loops = _mode == PlaybackMode.Loop && scoreEnd > introEnd + 1;
		if (!loops)
		{
			// once, scrub, or a loop with nothing after its intro: the last frame holds, state and caption included
			foreach (var action in _scoreEnd)
				action(scoreEnd);
			return new CompiledTimeline(_channels, steps, scoreEnd + _spec.Hold, scoreEnd, 0, Loops: false)
			{
				CaptionBand = _captionBand,
				ViewBox = _scene.ViewBox
			};
		}

		// the last frame holds for a rest, then everything settles back to how the story began: marks, trails, badges and
		// meters clear, cut edges heal, the camera returns. The diagram itself never leaves.
		var settle = scoreEnd + _spec.Rest;
		foreach (var action in _scoreEnd)
			action(settle);
		foreach (var channel in _channels.ToList())
		{
			if (!channel.IsAnimated)
				continue;
			var target = channel.ValueAt(introEnd);
			var now = channel.ValueAt(settle);
			if (!target.SequenceEqual(now))
				channel.Animate(settle, _spec.Settle, target, Easing.Move);
		}
		return new CompiledTimeline(_channels, steps, settle + _spec.Settle + _spec.Lead, scoreEnd, introEnd, Loops: true)
		{
			CaptionBand = _captionBand,
			ViewBox = _scene.ViewBox,
		};
	}

	private XElement ShowCaption(string text, double at, double y, ref XElement? previous)
	{
		if (previous is not null)
			Opacity(previous, ReducedMotion.Cut).Animate(at, _spec.Exit, Channel.Value(0), Easing.Exit);
		var caption = Overlay.Caption(text, _captionNumber, _captionTotal, _scene.ViewBox.X + 14, y);
		// a thin line under the caption fills as the step plays: something is happening, and this is how long it lasts
		_progress = Overlay.Progress(_scene.ViewBox.X + 14, _scene.ViewBox.Right - 14, y + 11);
		caption.Add(_progress);
		_sceneRoot.Add(caption);
		var enter = at + (previous is null ? 0 : _spec.Exit * 0.6);
		Opacity(caption, ReducedMotion.Cut, initial: 0).Animate(enter, _spec.Enter, Channel.Value(1), Easing.Enter);
		var rise = ChannelFor(caption, Property.Transform, Channel.Transform(0, _spec.Rise, 1), ReducedMotion.Off);
		rise.Animate(enter, _spec.Enter, Channel.Transform(0, 0, 1), Easing.Enter);
		previous = caption;
		return caption;
	}

	/// <summary>
	/// The first beat of the story draws the eye once: its subject pulses as the story begins. The score never asks for
	/// this; every animation opens the same way.
	/// </summary>
	private void Cue(ScoreStep step, double at)
	{
		var subject = step.Statements.Select(SubjectOf).FirstOrDefault(id => id is not null);
		if (subject is null || (step.Statements.Count > 0 && step.Statements[0] is PulseStatement or FocusStatement))
			return;
		if (_scene.Target(subject) is { } target && IsVisible(target, at))
			_ = Pulse(target, at, _spec.PulseScale);
	}

	private string? SubjectOf(Statement statement) => statement switch
	{
		SendStatement s => s.From,
		TourStatement s => s.Path[0],
		RunStatement s => s.Path[0] == "[*]" ? "_start" : s.Path[0],
		PingStatement s => s.From,
		StreamStatement s => s.Path[0],
		TryStatement s => s.From,
		FillStatement s => s.Target.Id,
		MarkStatement { Targets.Count: > 0 } s => s.Targets[0].Id,
		BadgeStatement s => s.Targets[0].Id,
		CountStatement s => s.Target.Id,
		HangStatement s => s.Targets[0].Id,
		ShowStatement { Targets.Count: > 0 } s => s.Targets[0].Id,
		PlayStatement s when _scene.Edges.Count > 0 => _scene.Edges[(s.First ?? 1) - 1].From,
		_ => null,
	};

	/// <summary>Widens the view box on both sides, so a caption wider than a narrow diagram is not cut off.</summary>
	private void WidenStage(double by)
	{
		var vb = _scene.ViewBox;
		_scene.ViewBox = vb with { X = vb.X - (by / 2), Width = vb.Width + by };
		var root = _scene.Root;
		root.SetAttributeValue("viewBox", $"{Num.F(vb.X - (by / 2))} {Num.F(vb.Y)} {Num.F(vb.Width + by)} {Num.F(vb.Height)}");
		var width = Num.Parse((string?)root.Attribute("width"));
		if (!double.IsNaN(width))
			root.SetAttributeValue("width", Num.F(width + by));
	}

	private void ExtendStage(double by)
	{
		// the band goes above the diagram, where a reader starts
		var vb = _scene.ViewBox;
		_scene.ViewBox = vb with { Y = vb.Y - by, Height = vb.Height + by };
		var root = _scene.Root;
		root.SetAttributeValue("viewBox", $"{Num.F(vb.X)} {Num.F(vb.Y - by)} {Num.F(vb.Width)} {Num.F(vb.Height + by)}");
		var height = Num.Parse((string?)root.Attribute("height"));
		if (!double.IsNaN(height))
			root.SetAttributeValue("height", Num.F(height + by));
	}

	// ---- channels -------------------------------------------------------------------------------------------------

	private Channel Opacity(XElement element, ReducedMotion reduced, double initial = 1) =>
		ChannelFor(element, Property.Opacity, Channel.Value(initial), reduced);

	private Channel ChannelFor(XElement element, Property property, double[] initial, ReducedMotion reduced)
	{
		if (_byElement.TryGetValue((element, property), out var channel))
			return channel;
		channel = new Channel(element, property, initial, reduced);
		_byElement[(element, property)] = channel;
		_channels.Add(channel);
		return channel;
	}

	/// <summary>The wrapper that carries an element's visibility and movement, created around it on first use.</summary>
	private XElement Outer(XElement element)
	{
		if (_outer.TryGetValue(element, out var wrapper))
			return wrapper;
		wrapper = new XElement(SvgScene.Ns + "g");
		element.ReplaceWith(wrapper);
		wrapper.Add(element);
		_outer[element] = wrapper;
		return wrapper;
	}

	/// <summary>A second wrapper inside <see cref="Outer"/>, for lasting state such as a cut edge, so focus and visibility stay independent.</summary>
	private XElement State(XElement element)
	{
		_ = Outer(element);
		if (_state.TryGetValue(element, out var wrapper))
			return wrapper;
		wrapper = new XElement(SvgScene.Ns + "g");
		element.ReplaceWith(wrapper);
		wrapper.Add(element);
		_state[element] = wrapper;
		return wrapper;
	}

	private Channel Visibility(XElement element) => Opacity(Outer(element), ReducedMotion.Cut);

	private Channel Movement(XElement element, Vec origin)
	{
		var channel = ChannelFor(Outer(element), Property.Transform, Channel.Transform(0, 0, 1), ReducedMotion.Off);
		channel.Origin = new Vec(origin.X - _stage.X, origin.Y - _stage.Y);
		return channel;
	}

	private Channel Dim(XElement element) => Opacity(element, ReducedMotion.Cut);

	private Channel Camera()
	{
		var channel = ChannelFor(_camera, Property.Transform, Channel.Transform(0, 0, 1), ReducedMotion.Cut);
		channel.Origin = new Vec(-_stage.X, -_stage.Y);
		return channel;
	}

	private string NextId(string kind) => $"{_scene.Prefix}-{kind}{_ids++}";

	// ---- statements -----------------------------------------------------------------------------------------------

	private double CompileStatement(Statement statement, double at) => Dispatch(Numbered(statement), at);

	/// <summary>Outside a sequence diagram, <c>#n</c> names the n-th element (a gantt task, a commit, a period), not a message.</summary>
	private Statement Numbered(Statement statement)
	{
		if (_scene.Kind == SceneKind.Sequence)
			return statement;

		static (List<TargetRef>, List<EdgeRef>) Split(IReadOnlyList<TargetRef> targets, IReadOnlyList<EdgeRef> edges) =>
			([.. targets, .. edges.Where(e => e.IsMessage).Select(e => new TargetRef($"#{e.Index}", false, e.Line))], [.. edges.Where(e => !e.IsMessage)]);

		switch (statement)
		{
			case ShowStatement s when s.Edges.Any(e => e.IsMessage):
				{
					var (t, e) = Split(s.Targets, s.Edges);
					return s with { Targets = t, Edges = e };
				}
			case HideStatement s when s.Edges.Any(e => e.IsMessage):
				{
					var (t, e) = Split(s.Targets, s.Edges);
					return s with { Targets = t, Edges = e };
				}
			case MarkStatement s when s.Edges.Any(e => e.IsMessage):
				{
					var (t, e) = Split(s.Targets, s.Edges);
					return s with { Targets = t, Edges = e };
				}
			default:
				return statement;
		}
	}

	private double Dispatch(Statement statement, double at) => statement switch
	{
		ShowStatement s => ShowAll(s.Targets, s.Edges, s.Each, at),
		HideStatement s => HideAll(s.Targets, s.Edges, s.Each, at),
		FocusStatement s => s.All ? Unfocus(at) : Focus(Resolve(s.Targets), s.Close, at),
		PulseStatement s => PulseAll(Resolve(s.Targets), s.Each, at),
		MarkStatement s => MarkAll(s, at),
		DrawStatement s => Each(ResolveEdges(s.Edges), s.Each, at, (e, t) => Draw(e, t)),
		EraseStatement s => Each(ResolveEdges(s.Edges), s.Each, at, (e, t) => Erase(e, t)),
		CutStatement s => Each(ResolveEdges(s.Edges), false, at, (e, t) => Cut(e, t, cut: true)),
		RestoreStatement s => Each(ResolveEdges(s.Edges), false, at, (e, t) => Cut(e, t, cut: false)),
		SendStatement s => SendAll(s, at),
		StreamStatement s => StreamAll(s, at),
		TourStatement s => Tour(s, at),
		RunStatement s => Run(s.Path, s.Line, at),
		TryStatement s => Try(s, at),
		PingStatement s => Ping(s, at),
		BuildStatement s => Build(s.Line, at),
		PlayStatement s => Play(s, at),
		SweepStatement s => Sweep(s, at),
		MoveStatement s => Move(s, at),
		SetStatement s => Set(s, at),
		TraceStatement s => Trace(s, at),
		ExpandStatement s => Expand(s, at),
		FillStatement s => Fill(s, at),
		BadgeStatement s => BadgeAll(s, at),
		CountStatement s => Count(s, at),
		HangStatement s => Each(Resolve(s.Targets), false, at, (t, a) => Hang(t, a)),
		NoteStatement s => Note(s, at),
		_ => at,
	};

	private double Each<T>(IReadOnlyList<T> items, bool each, double at, Func<T, double, double> action)
	{
		var end = at;
		for (var i = 0; i < items.Count; i++)
			end = Math.Max(end, action(items[i], at + (each ? i * _spec.Stagger : 0)));
		return end;
	}

	private double ShowAll(IReadOnlyList<TargetRef> targets, IReadOnlyList<EdgeRef> edges, bool each, double at)
	{
		var resolved = Resolve(targets);
		var end = Each(resolved, each, at, (t, s) => Show(t, s));
		var edgeStart = each ? at + (resolved.Count * _spec.Stagger) : at;
		return Math.Max(end, Each(ResolveEdges(edges), each, edgeStart, (e, s) => Draw(e, s)));
	}

	private double HideAll(IReadOnlyList<TargetRef> targets, IReadOnlyList<EdgeRef> edges, bool each, double at)
	{
		var end = Each(Resolve(targets), each, at, (t, s) =>
		{
			foreach (var element in t.Elements)
				Visibility(element).Animate(s, _spec.Exit, Channel.Value(0), Easing.Exit);
			return s + _spec.Exit;
		});
		return Math.Max(end, Each(ResolveEdges(edges), each, at, (e, s) =>
		{
			Visibility(e.Element).Animate(s, _spec.Exit, Channel.Value(0), Easing.Exit);
			foreach (var label in e.Labels)
				Visibility(label).Animate(s, _spec.Exit, Channel.Value(0), Easing.Exit);
			return s + _spec.Exit;
		}));
	}

	private double Show(SceneTarget target, double at)
	{
		var end = ShowSelf(target, at);
		if (target.Drop is { } drop)
			end = Math.Max(end, Draw(drop, at + (_spec.Enter * 0.3)));
		for (var i = 0; i < target.Followers.Count; i++)
			end = Math.Max(end, ShowSelf(target.Followers[i], at + (_spec.Enter * 0.6) + (i * _spec.Stagger)));
		return end;
	}

	private double ShowSelf(SceneTarget target, double at)
	{
		Light(target, at);
		// an element no statement has touched yet is visible only as a still; showing it means it appears here
		if (HasVisibility(target) && IsVisible(target, at))
			return at;
		foreach (var element in target.Elements)
		{
			var visibility = Visibility(element);
			if (!visibility.IsAnimated)
				visibility.Initial[0] = 0;
			visibility.Animate(at, _spec.Enter, Channel.Value(1), Easing.Enter);
			if (target.Kind != TargetKind.Group)
			{
				var movement = Movement(element, target.Box.Center);
				if (!movement.IsAnimated)
					movement.Initial[1] = _spec.Rise;
				var current = movement.ValueAt(at);
				movement.Animate(at, _spec.Enter, Channel.Transform(0, 0, current[2]), Easing.Enter);
			}
		}
		return at + _spec.Enter;
	}

	private bool HasVisibility(SceneTarget target) => target.Elements.Count > 0 && _byElement.ContainsKey((Outer(target.Elements[0]), Property.Opacity));

	private bool IsVisible(SceneTarget target, double at) =>
		target.Elements.Count == 0 || !_byElement.TryGetValue((Outer(target.Elements[0]), Property.Opacity), out var channel) || channel.ValueAt(at)[0] > 0.5;

	private double PulseAll(IReadOnlyList<SceneTarget> targets, bool each, double at) => Each(targets, each, at, (t, s) => Pulse(t, s, _spec.PulseScale));

	private double Pulse(SceneTarget target, double at, double scale)
	{
		foreach (var element in target.Elements)
		{
			var box = SvgScene.BoxOf(element);
			var movement = Movement(element, box.IsEmpty ? target.Box.Center : box.Center);
			var current = movement.ValueAt(at);
			movement.Animate(at, _spec.Pulse * 0.4, Channel.Transform(current[0], current[1], scale), Easing.Enter);
			movement.Animate(at + (_spec.Pulse * 0.4), _spec.Pulse * 0.6, Channel.Transform(current[0], current[1], 1), Easing.Move);
		}
		return at + _spec.Pulse;
	}

	// ---- focus and camera -----------------------------------------------------------------------------------------

	private double Focus(IReadOnlyList<SceneTarget> targets, bool close, double at)
	{
		if (targets.Count == 0)
			return at;
		var keep = new HashSet<XElement>();
		var ids = new HashSet<string>(targets.Select(t => t.Id), StringComparer.Ordinal);
		foreach (var target in targets)
		{
			// focusing a venn overlap lights the region it names
			Light(target, at);
			foreach (var element in target.Elements)
				_ = keep.Add(element);
			for (var parent = target.Parent; parent is not null; parent = _scene.Group(parent)?.Parent)
				foreach (var element in _scene.Group(parent)?.Elements ?? [])
					_ = keep.Add(element);
			if (target.Kind == TargetKind.Group)
				foreach (var member in _scene.Targets.Where(m => InGroup(m, target.Id)))
				{
					_ = ids.Add(member.Id);
					foreach (var element in member.Elements)
						_ = keep.Add(element);
				}
		}
		if (_scene.Tree)
		{
			var added = true;
			while (added)
			{
				added = false;
				foreach (var node in _scene.Targets.Where(n => n.Parent is not null && ids.Contains(n.Parent) && !ids.Contains(n.Id)).ToList())
				{
					_ = ids.Add(node.Id);
					foreach (var element in node.Elements)
						_ = keep.Add(element);
					added = true;
				}
			}
		}
		foreach (var edge in _scene.Edges.Where(e => ids.Contains(e.From) && ids.Contains(e.To)))
		{
			_ = keep.Add(edge.Element);
			foreach (var label in edge.Labels)
				_ = keep.Add(label);
		}

		foreach (var element in _scene.AllElements)
			Dim(element).Animate(at, _spec.Shift, Channel.Value(keep.Contains(element) ? 1 : _spec.Dim), Easing.Move);
		foreach (var visual in _edgeVisuals)
			foreach (var arrow in new[] { visual.Value.Arrow, visual.Value.ArrowStart })
				if (arrow is not null)
					Dim(arrow).Animate(at, _spec.Shift, Channel.Value(keep.Contains(visual.Key.Element) ? 1 : _spec.Dim), Easing.Move);
		_dimmed = true;

		var end = at + _spec.Shift;
		if (close)
		{
			// frame the targets with their neighbours, never a lone node
			var box = targets.Aggregate(Box.Empty, (b, t) => b.Union(t.Box));
			foreach (var edge in _scene.Edges.Where(e => ids.Contains(e.From) || ids.Contains(e.To)))
				if (_scene.Target(ids.Contains(edge.From) ? edge.To : edge.From) is { } neighbour)
					box = box.Union(neighbour.Box);
			end = Math.Max(end, Zoom(box, at));
		}
		_stepEnd.Add(stepEnd => Unfocus(stepEnd));
		return end;
	}

	private bool InGroup(SceneTarget target, string groupId)
	{
		for (var parent = target.Parent; parent is not null; parent = _scene.Group(parent)?.Parent)
			if (parent == groupId)
				return true;
		return false;
	}

	private double Unfocus(double at)
	{
		var end = at;
		if (_dimmed)
		{
			foreach (var element in _scene.AllElements)
				if (_byElement.ContainsKey((element, Property.Opacity)))
					Dim(element).Animate(at, _spec.Shift, Channel.Value(1), Easing.Move);
			foreach (var visual in _edgeVisuals.Values)
				foreach (var arrow in new[] { visual.Arrow, visual.ArrowStart })
					if (arrow is not null && _byElement.ContainsKey((arrow, Property.Opacity)))
						Dim(arrow).Animate(at, _spec.Shift, Channel.Value(1), Easing.Move);
			_dimmed = false;
			end = at + _spec.Shift;
		}
		if (_zoomed)
		{
			Camera().Animate(at, _spec.Camera, Channel.Transform(0, 0, 1), Easing.Move);
			_zoomed = false;
			end = at + _spec.Camera;
		}
		return end;
	}

	private double Zoom(Box box, double at)
	{
		if (box.IsEmpty)
			return at;
		const double pad = 40;
		var scale = Math.Min(_spec.MaxZoom, Math.Min(_stage.Width / (box.Width + (2 * pad)), _stage.Height / (box.Height + (2 * pad))));
		scale = Math.Max(1, scale);
		var c = box.Center;
		var tx = _stage.X + (_stage.Width / 2) - (scale * c.X);
		var ty = _stage.Y + (_stage.Height / 2) - (scale * c.Y);
		// never pan past the diagram's own edges
		tx = Math.Min(_stage.X * (1 - scale), Math.Max(tx, _stage.Right - (scale * _stage.Right)));
		ty = Math.Min(_stage.Y * (1 - scale), Math.Max(ty, _stage.Bottom - (scale * _stage.Bottom)));
		Camera().Animate(at, _spec.Camera, Channel.Transform(tx, ty, scale), Easing.Move);
		_zoomed = true;
		return at + _spec.Camera;
	}

	// ---- marks ----------------------------------------------------------------------------------------------------

	private double MarkAll(MarkStatement statement, double at)
	{
		foreach (var target in Resolve(statement.Targets))
		{
			Mark(target.Id, target.Box, null, statement.Role, at, target.Elements.Count > 0 ? Outer(target.Elements[0]) : null);
			// a journey task's score is its face, so the face takes the mark too
			foreach (var face in target.Elements.Where(e => SvgScene.HasClass(e, "face")))
				Mark(target.Id + "/face", SvgScene.BoxOf(face), null, statement.Role, at, Outer(face), round: true);
		}
		foreach (var edge in ResolveEdges(statement.Edges))
			Mark($"{edge.From}->{edge.To}#{edge.Index}", Box.Empty, edge, statement.Role, at);
		return at + _spec.Shift;
	}

	private void Mark(string key, Box box, SceneEdge? edge, Role? role, double at, XElement? holder = null, bool round = false)
	{
		foreach (var existing in _marks.Where(m => m.Key.Item1 == key && m.Key.Item2 != role))
			Opacity(existing.Value, ReducedMotion.Cut, 0).Animate(at, _spec.Shift, Channel.Value(0), Easing.Exit);
		if (role is not { } r)
			return;
		if (!_marks.TryGetValue((key, r), out var mark))
		{
			var color = _colors.For(r);
			if (edge is null)
			{
				// a node mark is a ring with a check or a cross, so the role reads without colour too
				var ring = Overlay.Ring(box, color, 2, 4, color, 0.14, round);
				ring.SetAttributeValue("opacity", null);
				mark = Overlay.Group(new XAttribute("opacity", "0"), ring, Overlay.MarkIcon(r, color, box.Inflate(4)));
				// inside the node's wrapper, so a mark moves, pulses and appears with its node
				(holder ?? _overlay).Add(mark);
			}
			else
			{
				mark = Overlay.Stroke(edge.Path, color, 3.5, round: true);
				_overlay.AddFirst(mark);
			}
			_marks[(key, r)] = mark;
		}
		Opacity(mark, ReducedMotion.Cut, 0).Animate(at, _spec.Shift, Channel.Value(1), Easing.Enter);
	}

	// ---- edges ----------------------------------------------------------------------------------------------------

	private EdgeVisual Drawable(SceneEdge edge, double? maskWidth = null)
	{
		if (!_edgeVisuals.TryGetValue(edge, out var visual))
		{
			visual = new EdgeVisual();
			_edgeVisuals[edge] = visual;
		}
		if (visual.Drawable)
			return visual;

		var stroke = edge.Stroke ?? edge.Element;
		var id = NextId("m");
		var region = _stage.Inflate(Math.Max(_stage.Width, _stage.Height));
		var maskPath = new XElement(
			SvgScene.Ns + "path",
			new XAttribute("d", edge.Path.ToPathData()),
			new XAttribute("fill", "none"),
			new XAttribute("stroke", "#fff"),
			new XAttribute("stroke-width", Num.F(maskWidth ?? (edge.Thick ? 18 : 14))),
			new XAttribute("pathLength", "1"),
			new XAttribute("stroke-dasharray", "1 1")
		);
		_defs.Add(
			new XElement(
				SvgScene.Ns + "mask",
				new XAttribute("id", id),
				new XAttribute("maskUnits", "userSpaceOnUse"),
				new XAttribute("x", Num.F(region.X)),
				new XAttribute("y", Num.F(region.Y)),
				new XAttribute("width", Num.F(region.Width)),
				new XAttribute("height", Num.F(region.Height)),
				maskPath
			)
		);
		stroke.SetAttributeValue("mask", $"url(#{id})");
		visual.Mask = maskPath;

		// arrowheads become shapes of their own, so they land when the line arrives
		var wrapper = Outer(edge.Element);
		if (MarkerFor(stroke, "marker-end") is { } end)
		{
			stroke.SetAttributeValue("marker-end", null);
			visual.Arrow = new XElement(SvgScene.Ns + "g", Overlay.Arrowhead(end, edge.Path.End, edge.Path.EndAngle()));
			wrapper.Add(visual.Arrow);
		}
		if (MarkerFor(stroke, "marker-start") is { } start)
		{
			stroke.SetAttributeValue("marker-start", null);
			var p = edge.Path.Points;
			var angle = Math.Atan2(p[1].Y - p[0].Y, p[1].X - p[0].X) * 180 / Math.PI;
			visual.ArrowStart = new XElement(SvgScene.Ns + "g", Overlay.Arrowhead(start, edge.Path.Start, angle));
			wrapper.Add(visual.ArrowStart);
		}
		return visual;
	}

	private XElement? MarkerFor(XElement stroke, string attribute)
	{
		var value = (string?)stroke.Attribute(attribute);
		if (value is null || !value.StartsWith("url(#", StringComparison.Ordinal))
			return null;
		var id = value[5..^1];
		return _scene.Root.Descendants(SvgScene.Ns + "marker").FirstOrDefault(m => (string?)m.Attribute("id") == id);
	}

	private Channel Ink(EdgeVisual visual) => ChannelFor(visual.Mask!, Property.DashOffset, Channel.Value(0), ReducedMotion.Cut);

	private bool IsDrawn(SceneEdge edge, double at)
	{
		if (_byElement.TryGetValue((Outer(edge.Element), Property.Opacity), out var vis) && vis.ValueAt(at)[0] < 0.5)
			return false;
		return !_edgeVisuals.TryGetValue(edge, out var visual) || !visual.Drawable || Math.Abs(Ink(visual).ValueAt(at)[0]) < 0.5;
	}

	private double Draw(SceneEdge edge, double at)
	{
		if (IsDrawn(edge, at) && _edgeVisuals.TryGetValue(edge, out var existing) && existing.Drawable)
			return at;
		var visual = Drawable(edge, edge.MaskWidth);
		var ink = Ink(visual);
		var firstUse = !ink.IsAnimated;
		if (firstUse)
			ink.Initial[0] = 1;
		var visibility = Visibility(edge.Element);
		if (visibility.ValueAt(at)[0] < 1)
			visibility.Set(at, Channel.Value(1));

		var duration = _spec.DrawTime(edge.Path.Length);
		if (Math.Abs(ink.ValueAt(at)[0]) > 0.5)
			ink.Set(at, Channel.Value(1));
		ink.Animate(at, duration, Channel.Value(0), Easing.Move);

		var arrive = at + (duration * 0.85);
		foreach (var arrow in new[] { visual.Arrow, visual.ArrowStart })
		{
			if (arrow is null)
				continue;
			var channel = Opacity(arrow, ReducedMotion.Cut, firstUse ? 0 : 1);
			channel.Animate(arrow == visual.ArrowStart ? at : arrive, _spec.Shift * 0.5, Channel.Value(1), Easing.Enter);
		}
		foreach (var label in edge.Labels)
		{
			var channel = Visibility(label);
			if (firstUse && !channel.IsAnimated)
				channel.Initial[0] = 0;
			channel.Animate(arrive, _spec.Enter, Channel.Value(1), Easing.Enter);
		}
		return at + duration;
	}

	private double Erase(SceneEdge edge, double at)
	{
		var visual = Drawable(edge);
		var ink = Ink(visual);
		var duration = _spec.DrawTime(edge.Path.Length);
		foreach (var arrow in new[] { visual.Arrow, visual.ArrowStart })
			if (arrow is not null)
				Opacity(arrow, ReducedMotion.Cut).Animate(at, _spec.Exit * 0.6, Channel.Value(0), Easing.Exit);
		foreach (var label in edge.Labels)
			Visibility(label).Animate(at, _spec.Exit, Channel.Value(0), Easing.Exit);
		ink.Animate(at, duration, Channel.Value(-1), Easing.Move);
		return at + duration;
	}

	private double Cut(SceneEdge edge, double at, bool cut)
	{
		if (!_edgeVisuals.TryGetValue(edge, out var visual))
		{
			visual = new EdgeVisual();
			_edgeVisuals[edge] = visual;
		}
		if (visual.Cross is null)
		{
			visual.Cross = Overlay.Cross(edge.Path.At(0.5), _colors.Fail);
			_overlay.Add(visual.Cross);
		}
		Opacity(State(edge.Element), ReducedMotion.Cut).Animate(at, _spec.Shift, Channel.Value(cut ? 0.28 : 1), Easing.Move);
		foreach (var label in edge.Labels)
			Opacity(State(label), ReducedMotion.Cut).Animate(at, _spec.Shift, Channel.Value(cut ? 0.28 : 1), Easing.Move);
		Opacity(visual.Cross, ReducedMotion.Cut, 0).Animate(at, _spec.Shift, Channel.Value(cut ? 1 : 0), cut ? Easing.Enter : Easing.Exit);
		return at + _spec.Shift;
	}

	// ---- tokens ---------------------------------------------------------------------------------------------------

	private double SendAll(SendStatement send, double at)
	{
		TokenDeclaration? declared = null;
		if (send.Token is not null)
			_ = _score.Tokens.TryGetValue(send.Token, out declared);
		var from = ResolveNode(send.From, send.Line);
		var end = at;
		for (var i = 0; i < send.To.Count; i++)
		{
			var to = ResolveNode(send.To[i], send.Line);
			if (from is null || to is null)
				continue;
			var route = Route(from.Id, to.Id, send.Line);
			if (route is null)
				continue;
			var start = at + (send.Each ? i * _spec.Stagger : 0);
			var label = send.Label ?? declared?.Label;
			var role = send.Role ?? declared?.Role;
			end = Math.Max(end, Send(route.Value.Edge, route.Value.Reverse, start, label, role, send.Becomes, send.Speed, send.Fate, to));
		}
		return end;
	}

	/// <summary>The edge a token takes from one node to another: the edge between them, or against an edge for a reply.</summary>
	private (SceneEdge Edge, bool Reverse)? Route(string from, string to, int line)
	{
		var forward = _scene.Edges.FirstOrDefault(e => e.From == from && e.To == to);
		if (forward is not null)
			return (forward, false);
		var backward = _scene.Edges.FirstOrDefault(e => e.From == to && e.To == from);
		if (backward is not null)
			return (backward, true);
		var leaving = _scene.Edges.Where(e => e.From == from).Select(e => e.To).Distinct().ToList();
		_diagnostics.Add(
			new(
				line,
				leaving.Count == 0
					? $"There is no edge between {from} and {to}, and {from} has no edges leaving it."
					: $"There is no edge between {from} and {to}. From {from} the diagram goes to: {string.Join(", ", leaving)}."
			)
		);
		return null;
	}

	private double Send(SceneEdge edge, bool reverse, double at, string? label, Role? role, string? becomes, Speed speed, Fate fate, SceneTarget? arrival)
	{
		// bring in what the token needs: its edge drawn, its ends visible
		if (_scene.Target(reverse ? edge.To : edge.From) is { } origin && !IsVisible(origin, at))
			at = Show(origin, at);
		if (!IsDrawn(edge, at))
			at = Draw(edge, at) + _spec.Beat;

		var path = reverse ? edge.Path.Reversed() : edge.Path;
		var color = role is { } r ? _colors.For(r) : "var(--accent, #3b82f6)";
		var hollow = reverse || (_scene.Kind == SceneKind.Sequence && edge.Dotted);
		var radius = edge.Thick ? 8 : 6.5;
		var token = Overlay.Token(color, hollow, radius, label, out var labelElement);
		_overlay.Add(token);

		var duration = _spec.TravelTime(path.Length, speed);
		var travel = fate == Fate.Arrives ? 1.0 : 0.5;
		Trail(path, color, edge.Thick, at, duration * travel, travel);
		var move = ChannelFor(token, Property.Transform, Channel.Transform(path.Start.X, path.Start.Y, 1), ReducedMotion.Off);
		var opacity = ChannelFor(token, Property.Opacity, Channel.Value(0), ReducedMotion.Off);
		opacity.Animate(at, 140, Channel.Value(1), Easing.Enter);
		Travel(move, path, at, duration * travel, travel);
		var arrive = at + (duration * travel);

		if (becomes is not null && labelElement is not null)
		{
			var next = Overlay.Label(becomes, color, -radius - 17, filled: !hollow);
			next.SetAttributeValue("opacity", "0");
			token.Add(next);
			var swap = at + (duration * travel * 0.5);
			ChannelFor(labelElement, Property.Opacity, Channel.Value(1), ReducedMotion.Off).Animate(swap, 160, Channel.Value(0), Easing.Exit);
			ChannelFor(next, Property.Opacity, Channel.Value(0), ReducedMotion.Off).Animate(swap + 80, 200, Channel.Value(1), Easing.Enter);
		}

		switch (fate)
		{
			case Fate.Lost:
				opacity.Animate(arrive - 60, 320, Channel.Value(0), Easing.Exit);
				return arrive + 260;
			case Fate.Stalls:
				_stepEnd.Add(stepEnd => opacity.Animate(stepEnd, _spec.Exit, Channel.Value(0), Easing.Exit));
				return arrive;
			default:
				opacity.Animate(arrive - 40, 160, Channel.Value(0), Easing.Exit);
				if (arrival is not null)
				{
					if (!IsVisible(arrival, arrive))
						_ = Show(arrival, arrive - 80);
					else
						_ = Pulse(arrival, arrive - 40, 1 + ((_spec.PulseScale - 1) * 0.6));
				}
				return arrive;
		}
	}

	/// <summary>
	/// Lights the path a token travels, drawn in step with it, and keeps it lit until the step ends: the route a request
	/// took stays readable after the token has gone.
	/// </summary>
	private void Trail(Polyline path, string color, bool thick, double at, double duration, double upTo)
	{
		var trail = Overlay.Stroke(path, color, thick ? 5 : 3.5);
		trail.SetAttributeValue("pathLength", "1");
		trail.SetAttributeValue("stroke-dasharray", "1 1");
		trail.SetAttributeValue("stroke-dashoffset", "1");
		_overlay.AddFirst(trail);
		var ink = ChannelFor(trail, Property.DashOffset, Channel.Value(1), ReducedMotion.Cut);
		var opacity = Opacity(trail, ReducedMotion.Cut, 0);
		opacity.Set(at, Channel.Value(0.85));
		ink.Animate(at, duration, Channel.Value(1 - upTo), Easing.Move);
		_stepEnd.Add(end => opacity.Animate(end, _spec.Exit, Channel.Value(0), Easing.Exit));
	}

	/// <summary>Moves a token along a path with the move easing applied to the whole trip, at constant speed between samples, through every corner.</summary>
	private void Travel(Channel move, Polyline path, double at, double duration, double upTo)
	{
		var times = new SortedSet<double>();
		const int samples = 18;
		for (var i = 1; i <= samples; i++)
			_ = times.Add((double)i / samples);
		foreach (var corner in path.CornerFractions())
			if (corner < upTo)
				_ = times.Add(Easing.Move.Inverse(corner / upTo));
		var previous = 0.0;
		foreach (var t in times)
		{
			var point = path.At(Easing.Move.Evaluate(t) * upTo);
			move.Animate(at + (previous * duration), (t - previous) * duration, Channel.Transform(point.X, point.Y, 1), Easing.Linear);
			previous = t;
		}
	}

	private double StreamAll(StreamStatement stream, double at)
	{
		for (var i = 0; i + 1 < stream.Path.Count; i++)
		{
			var from = ResolveNode(stream.Path[i], stream.Line);
			var to = ResolveNode(stream.Path[i + 1], stream.Line);
			if (from is null || to is null || Route(from.Id, to.Id, stream.Line) is not { } route)
				continue;
			if (!IsDrawn(route.Edge, at))
				_ = Draw(route.Edge, at);
			Stream(route.Edge, route.Reverse, stream.Rate, stream.Always, at);
		}
		return at + _spec.StreamHold;
	}

	private void Stream(SceneEdge edge, bool reverse, StreamRate rate, bool always, double at)
	{
		var path = reverse ? edge.Path.Reversed() : edge.Path;
		var (gap, pace) = rate switch
		{
			StreamRate.Trickle => (34.0, 0.7),
			StreamRate.Busy => (11.0, 1.5),
			_ => (19.0, 1.0),
		};
		gap = Math.Max(7, Math.Min(gap, path.Length / 3.5));
		var dash = edge.Dotted ? $"6 {Num.F(gap)}" : $"0.1 {Num.F(gap)}";
		var stroke = Overlay.Stroke(path, "var(--accent, #3b82f6)", edge.Thick ? 5.5 : 4.2, dash, round: !edge.Dotted);
		_overlay.AddFirst(stroke);
		var opacity = ChannelFor(stroke, Property.Opacity, Channel.Value(0), ReducedMotion.Off);
		var offset = ChannelFor(stroke, Property.DashOffset, Channel.Value(0), ReducedMotion.Off);
		opacity.Animate(at, _spec.Enter, Channel.Value(0.9), Easing.Enter);

		void Stop(double end)
		{
			opacity.Animate(end, _spec.Exit, Channel.Value(0), Easing.Exit);
			var distance = _spec.StreamSpeed * pace * ((end + _spec.Exit - at) / 1000);
			offset.Animate(at, end + _spec.Exit - at, Channel.Value(-distance), Easing.Linear);
		}

		if (always)
			_scoreEnd.Add(Stop);
		else
			_stepEnd.Add(Stop);
	}

	private double Tour(TourStatement tour, double at)
	{
		TokenDeclaration? declared = null;
		if (tour.Token is not null)
			_ = _score.Tokens.TryGetValue(tour.Token, out declared);
		// one focus on the whole route, so the hops already taken stay lit
		var stops = tour.Path.Select(id => ResolveNode(id, tour.Line)).ToList();
		if (stops.Any(s => s is null))
			return at;
		_ = Focus(stops!, close: false, at);
		var t = at + (_spec.Shift * 0.5);
		for (var i = 0; i + 1 < stops.Count; i++)
		{
			var from = stops[i]!;
			var to = stops[i + 1]!;
			if (Route(from.Id, to.Id, tour.Line) is not { } route)
				continue;
			t = Send(
				route.Edge,
				route.Reverse,
				t,
				tour.Label ?? declared?.Label,
				tour.Role ?? declared?.Role,
				null,
				Speed.Normal,
				Fate.Arrives,
				to
			) + _spec.Beat;
		}
		return t;
	}

	private double Ping(PingStatement ping, double at)
	{
		var from = ResolveNode(ping.From, ping.Line);
		var to = ResolveNode(ping.To, ping.Line);
		if (from is null || to is null || Route(from.Id, to.Id, ping.Line) is not { } route)
			return at;
		var t = Send(route.Edge, route.Reverse, at, ping.Request, null, null, Speed.Normal, Fate.Arrives, to) + _spec.Beat;
		return Send(route.Edge, !route.Reverse, t, ping.Reply, null, null, Speed.Normal, Fate.Arrives, from);
	}

	// ---- resolution -----------------------------------------------------------------------------------------------

	private List<SceneTarget> Resolve(IReadOnlyList<TargetRef> targets)
	{
		var result = new List<SceneTarget>();
		foreach (var target in targets)
		{
			var resolved = target.IsGroup ? _scene.Group(target.Id) : ResolveNode(target.Id, target.Line, groups: true);
			if (resolved is not null)
				result.Add(resolved);
			else if (target.IsGroup)
				_diagnostics.Add(new(target.Line, $"There is no group '{target.Id}' in the diagram.{Known(_scene.Groups.Select(g => g.Id))}"));
		}
		return result;
	}

	private SceneTarget? ResolveNode(string id, int line, bool groups = false)
	{
		var mapped = MapStateId(id, line);
		if (_scene.Target(mapped) is { } target)
			return target;
		if (groups && _scene.Group(mapped) is { } group)
			return group;
		if (_scene.TargetByLabel(id) is { } labelled)
			return labelled;
		if (id.StartsWith('#') && !_scene.Targets.Any(t => t.Index > 0))
		{
			_diagnostics.Add(new(line, $"{id} numbers sequence messages, gantt tasks, timeline periods and commits; this diagram names its elements by id."));
			return null;
		}
		var known = _scene.Targets.Where(t => !t.Id.StartsWith('_')).Select(t => t.Id.StartsWith('#') ? $"\"{t.Label}\"" : t.Id);
		_diagnostics.Add(new(line, $"There is no node '{id}' in the diagram.{Known(known)}"));
		return null;
	}

	private string MapStateId(string id, int line)
	{
		_ = line;
		return id == "[*]" ? "_start" : id;
	}

	private List<SceneEdge> ResolveEdges(IReadOnlyList<EdgeRef> edges)
	{
		var result = new List<SceneEdge>();
		foreach (var edge in edges)
		{
			if (edge.IsMessage)
			{
				if (_scene.Kind != SceneKind.Sequence)
					_diagnostics.Add(new(edge.Line, $"{edge} names a sequence message, but this is not a sequence diagram."));
				else if (edge.Index is not { } n || n > _scene.Edges.Count)
					_diagnostics.Add(new(edge.Line, $"The diagram has {_scene.Edges.Count} messages, so there is no {edge}."));
				else
					result.Add(_scene.Edges[n - 1]);
				continue;
			}
			var candidates = _scene.Edges.Where(e => e.From == MapStateId(edge.From, edge.Line) && e.To == MapStateId(edge.To, edge.Line)).ToList();
			if (candidates.Count == 0)
			{
				_ = Route(edge.From, edge.To, edge.Line);
				continue;
			}
			if (edge.Index is { } index)
			{
				if (index > candidates.Count)
					_diagnostics.Add(new(edge.Line, $"There are {candidates.Count} edges {edge.From} -> {edge.To}, so there is no #{index}."));
				else
					result.Add(candidates[index - 1]);
			}
			else
				result.AddRange(candidates);
		}
		return result;
	}

	private static string Known(IEnumerable<string> ids)
	{
		var list = ids.Take(13).ToList();
		if (list.Count == 0)
			return "";
		return " Known: " + string.Join(", ", list.Take(12)) + (list.Count > 12 ? ", …" : "") + ".";
	}
}
