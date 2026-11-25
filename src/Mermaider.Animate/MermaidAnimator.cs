using System.Xml.Linq;
using Mermaider.Animate.Compile;
using Mermaider.Animate.Scene;
using Mermaider.Animate.Score;
using Mermaider.Animate.Timeline;

namespace Mermaider.Animate;

/// <summary>
/// Renders a Mermaid diagram followed by an <c>animate</c> block as an animated SVG. The diagram renders exactly as
/// <see cref="MermaidRenderer"/> draws it; the animation only wraps its elements and adds an overlay, driven by CSS.
/// </summary>
/// <example>
/// <code>
/// flowchart LR
///   client --> api --> db
///
/// animate
///   step "A request arrives"
///     send client -> api "GET /orders"
/// </code>
/// </example>
public static class MermaidAnimator
{
	/// <summary>Whether the source carries an <c>animate</c> block.</summary>
	public static bool HasAnimation(string source) => ScoreParser.FindStart(SplitLines(source)) >= 0;

	/// <summary>Renders the animated SVG. A source without an <c>animate</c> block renders as its static diagram.</summary>
	/// <exception cref="AnimationException">The animate block has problems; every one is listed.</exception>
	public static string RenderSvg(string source, AnimateOptions? options = null) => Compile(source, options).Svg;

	/// <summary>
	/// Renders the animated SVG inside an HTML fragment with controls that need no script: Play, Pause and a chip per
	/// step that jumps to it. Embed the fragment inline in a page.
	/// </summary>
	/// <exception cref="AnimationException">The animate block has problems; every one is listed.</exception>
	public static string RenderPlayer(string source, AnimateOptions? options = null) => Compile(source, options).RenderPlayer();

	/// <summary>Compiles the animation, keeping its timeline so frames and steps can be read back.</summary>
	/// <exception cref="AnimationException">The animate block has problems; every one is listed.</exception>
	public static AnimatedDiagram Compile(string source, AnimateOptions? options = null)
	{
#if NET8_0_OR_GREATER
		ArgumentNullException.ThrowIfNull(source);
#else
		if (source is null)
			throw new ArgumentNullException(nameof(source));
#endif
		var lines = SplitLines(source);
		var start = ScoreParser.FindStart(lines);
		var diagramSource = start < 0 ? source : string.Join("\n", lines.Take(start));
		var svg = MermaidRenderer.RenderSvg(diagramSource, options?.Render);
		if (start < 0)
			return AnimatedDiagram.Static(svg);

		var diagnostics = new List<AnimationDiagnostic>();
		var score = ScoreParser.Parse(lines, start, diagnostics);
		var maxSteps = options?.MaxSteps ?? 64;
		if (score.Steps.Count > maxSteps)
			diagnostics.Add(new(score.Line, $"The score has {score.Steps.Count} steps; the limit is {maxSteps}."));
		if (diagnostics.Count > 0)
			throw new AnimationException(diagnostics);

		var motion = options?.Motion ?? score.Motion ?? MotionPreset.Calm;
		var mode = options?.Mode ?? score.Mode ?? PlaybackMode.Loop;
		var scene = SvgScene.Read(svg, diagramSource, Prefix(source, motion, mode));
		var compiler = new Compiler(
			scene,
			score,
			MotionSpec.For(motion),
			motion == MotionPreset.None ? PlaybackMode.Once : mode,
			RoleColors.From(options?.Render),
			diagnostics
		);
		var timeline = compiler.Compile();
		if (diagnostics.Count > 0)
			throw new AnimationException(diagnostics);

		return new AnimatedDiagram(scene, timeline, motion == MotionPreset.None ? PlaybackMode.Once : mode, still: motion == MotionPreset.None);
	}

	/// <summary>FNV-1a over the source and the options that change the output: stable across runs, distinct per diagram.</summary>
	private static string Prefix(string source, MotionPreset motion, PlaybackMode mode)
	{
		var hash = 2166136261u;
		foreach (var c in source + "|" + motion + "|" + mode)
		{
			hash ^= c;
			hash *= 16777619u;
		}
		return "ma" + hash.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
	}

	private static string[] SplitLines(string source) => source.Replace("\r\n", "\n").Split('\n');
}

/// <summary>A step of a compiled animation: its caption and when it plays.</summary>
public sealed record AnimationStep(string? Caption, TimeSpan Start, TimeSpan End);

/// <summary>A compiled animation: the animated SVG, its steps, and any frame of it as a static SVG.</summary>
public sealed class AnimatedDiagram
{
	private readonly SvgScene? _scene;
	private readonly CompiledTimeline? _timeline;
#if NET9_0_OR_GREATER
	private readonly Lock _gate = new();
#else
	private readonly object _gate = new();
#endif
	private readonly Dictionary<XElement, string> _classes = [];

	internal AnimatedDiagram(SvgScene scene, CompiledTimeline timeline, PlaybackMode mode, bool still)
	{
		_scene = scene;
		_timeline = timeline;
		Mode = mode;
		Duration = TimeSpan.FromMilliseconds(timeline.Duration);
		ScoreDuration = TimeSpan.FromMilliseconds(timeline.ScoreEnd);
		Intro = TimeSpan.FromMilliseconds(timeline.Loops ? timeline.StoryStart : 0);
		Steps = timeline.Steps.Select(s => new AnimationStep(s.Caption, TimeSpan.FromMilliseconds(s.Start), TimeSpan.FromMilliseconds(s.End))).ToList();
		Describe(scene.Root, Steps);
		Svg = still ? FrameAt(timeline.ScoreEnd) : Animated();
	}

	private AnimatedDiagram(string svg)
	{
		Svg = svg;
		Steps = [];
	}

	internal static AnimatedDiagram Static(string svg) => new(svg);

	/// <summary>
	/// The story, told to a screen reader: the steps' captions in order, after the description the author gave with
	/// <c>accDescr</c> if any. The diagram's own text stays readable as it is.
	/// </summary>
	private static void Describe(XElement root, IReadOnlyList<AnimationStep> steps)
	{
		var captions = steps.Select(s => s.Caption).OfType<string>().Where(c => c.Trim().Length > 0).ToList();
		if (captions.Count == 0)
			return;
		var told = new System.Text.StringBuilder(captions.Count == 1 ? "" : $"In {captions.Count} steps: ");
		for (var i = 0; i < captions.Count; i++)
		{
			var caption = captions[i].Trim();
			if (captions.Count > 1)
				_ = told.Append(i + 1).Append(". ");
			_ = told.Append(caption);
			if (caption[^1] is not ('.' or '!' or '?'))
				_ = told.Append('.');
			if (i < captions.Count - 1)
				_ = told.Append(' ');
		}
		var ns = root.Name.Namespace;
		if (root.Element(ns + "desc") is { } desc)
		{
			var given = desc.Value.TrimEnd();
			desc.Value = given.Length == 0 ? told.ToString() : given + (given[^1] is '.' or '!' or '?' ? " " : ". ") + told;
		}
		else if (root.Element(ns + "title") is { } title)
			title.AddAfterSelf(new XElement(ns + "desc", told.ToString()));
		else
			root.AddFirst(new XElement(ns + "desc", told.ToString()));
	}

	/// <summary>The animated SVG.</summary>
	public string Svg { get; }

	public PlaybackMode Mode { get; }

	/// <summary>One full cycle, including the rest and the fade between loops.</summary>
	public TimeSpan Duration { get; }

	/// <summary>
	/// How long the intro lasts: the leading caption-less <c>build</c> steps that a loop plays once before its story
	/// repeats. Zero when the animation has no intro or does not loop.
	/// </summary>
	public TimeSpan Intro { get; }

	/// <summary>When the last step ends.</summary>
	public TimeSpan ScoreDuration { get; }

	public IReadOnlyList<AnimationStep> Steps { get; }

	/// <summary>The animated SVG inside an HTML fragment with CSS-only Play, Pause and step controls.</summary>
	public string RenderPlayer() =>
		_scene is null || Mode == PlaybackMode.Scrub || !Steps.Any(s => s.Caption is not null)
			? $"<figure>\n{Svg}\n</figure>"
			: Player.Render(this, _scene.Prefix);

	internal CompiledTimeline? Timeline => _timeline;

	/// <summary>Every animated element's class with how many channels it animates, in the order its animations are listed.</summary>
	internal List<(string Class, int Channels)> AnimatedClasses()
	{
		lock (_gate)
		{
			return _timeline!
				.Channels
				.Where(c => c.IsAnimated)
				.GroupBy(c => c.Element)
				.Where(g => _classes.ContainsKey(g.Key))
				.Select(g => (_classes[g.Key], g.Count()))
				.ToList();
		}
	}

	/// <summary>Every animated element's class and its declarations at <paramref name="time"/>, for a still state in CSS.</summary>
	internal List<(string Class, string Declarations)> StylesAt(double time)
	{
		lock (_gate)
		{
			return _timeline!
				.Channels
				.Where(c => c.IsAnimated)
				.GroupBy(c => c.Element)
				.Where(g => _classes.ContainsKey(g.Key))
				.Select(g => (_classes[g.Key], string.Join("; ", g.Select(c => c.Css(c.ValueAt(time))))))
				.ToList();
		}
	}

	/// <summary>The animation at <paramref name="time"/> as a static SVG with no stylesheet animation: for posters, print and tests.</summary>
	public string RenderFrame(TimeSpan time) => _timeline is null ? Svg : FrameAt(time.TotalMilliseconds);

	private string Animated()
	{
		lock (_gate)
		{
			var scene = _scene!;
			var css = CssEmitter.Emit(_timeline!.Channels, _timeline.Duration, _timeline.StoryStart, _timeline.Loops, Mode, scene.Prefix, _classes) + Print();
			var root = scene.Root;
			var rootClass = (string?)root.Attribute("class");
			var style = new XElement(SvgScene.Ns + "style", "\n" + css);
			root.AddFirst(style);
			root.SetAttributeValue("class", (rootClass is null ? "" : rootClass + " ") + scene.Prefix + "-root");
			try
			{
				return root.ToString(SaveOptions.DisableFormatting);
			}
			finally
			{
				style.Remove();
				root.SetAttributeValue("class", rootClass);
			}
		}
	}

	/// <summary>
	/// On paper the diagram shows how the story ends, the frame a poster shows, not whichever frame was playing when the
	/// page was printed.
	/// </summary>
	private string Print()
	{
		var end = _timeline!.ScoreEnd;
		var sb = new System.Text.StringBuilder("@media print {\n");
		foreach (var group in _timeline.Channels.Where(c => c.IsAnimated).GroupBy(c => c.Element).Where(g => _classes.ContainsKey(g.Key)))
			_ = sb
				.Append("  .")
				.Append(_classes[group.Key])
				.Append(" { animation: none; ")
				.Append(string.Join("; ", group.Select(c => c.Css(c.ValueAt(end)))))
				.Append("; }\n");
		return sb.Append("}\n").ToString();
	}

	private string FrameAt(double time)
	{
		lock (_gate)
		{
			var saved = new List<(XElement Element, string? Style)>();
			foreach (var group in _timeline!.Channels.Where(c => c.IsAnimated).GroupBy(c => c.Element))
			{
				var element = group.Key;
				saved.Add((element, (string?)element.Attribute("style")));
				var parts = group.Select(c => c.Css(c.ValueAt(time))).ToList();
				if (group.FirstOrDefault(c => c.Property == Property.Transform) is { } transform)
					parts.Add($"transform-box:view-box;transform-origin:{Num.F(transform.Origin.X)}px {Num.F(transform.Origin.Y)}px");
				element.SetAttributeValue("style", string.Join(";", parts));
			}
			try
			{
				return _scene!.Root.ToString(SaveOptions.DisableFormatting);
			}
			finally
			{
				foreach (var (element, style) in saved)
					element.SetAttributeValue("style", style);
			}
		}
	}
}
