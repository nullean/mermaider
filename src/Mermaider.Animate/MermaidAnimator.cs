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
		var animation = Compile(source, options, drawCaptions: true);
		// the player shows captions beside the figure, so it plays a compile that leaves them out of the SVG
		animation.ForPlayer = () => Compile(source, options, drawCaptions: false);
		return animation;
	}

	internal static AnimatedDiagram Compile(string source, AnimateOptions? options, bool drawCaptions)
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
		foreach (var story in score.Scenarios.Count == 0 ? [score] : score.Scenarios.Select(score.For))
			if (story.Steps.Count > maxSteps)
				diagnostics.Add(new(score.Line, $"The score has {story.Steps.Count} steps; the limit is {maxSteps}."));
		if (diagnostics.Count > 0)
			throw new AnimationException(diagnostics);

		var motion = options?.Motion ?? score.Motion ?? MotionPreset.Calm;
		var mode = options?.Mode ?? score.Mode ?? PlaybackMode.Loop;

		AnimatedDiagram One(ScoreDocument story, string prefix, ScoreScenario? scenario)
		{
			var scene = SvgScene.Read(svg, diagramSource, prefix);
			var compiler = new Compiler(
				scene,
				story,
				MotionSpec.For(motion),
				motion == MotionPreset.None ? PlaybackMode.Once : mode,
				RoleColors.From(options?.Render),
				diagnostics,
				drawCaptions,
				next => MermaidRenderer.RenderSvg(next, options?.Render)
			);
			var timeline = compiler.Compile();
			return new AnimatedDiagram(
				scene,
				timeline,
				motion == MotionPreset.None ? PlaybackMode.Once : mode,
				still: motion == MotionPreset.None,
				scenario?.Name
			)
			{
				ScenarioRole = scenario?.Role,
				SharedSteps = scenario is null ? 0 : score.Steps.Count,
				Colors = RoleColors.From(options?.Render),
				Contents = options?.Contents ?? score.Contents ?? PlayerContents.Auto,
			};
		}

		if (score.Scenarios.Count == 0)
		{
			var animation = One(score, Prefix(source + (drawCaptions ? "" : "|player"), motion, mode), null);
			return diagnostics.Count > 0 ? throw new AnimationException(diagnostics) : animation;
		}

		// each ending is its own animation of the same diagram, with its own prefix so they can share a page; a problem in
		// the shared beginning is reported once, not once per ending
		var endings = score
			.Scenarios
			.Select(
				(s, i) => new AnimatedScenario(s.Name, One(score.For(s), Prefix(source + "|scenario " + i + (drawCaptions ? "" : "|player"), motion, mode), s))
			)
			.ToList();
		if (diagnostics.Count > 0)
			throw new AnimationException([.. diagnostics.Distinct()]);
		return endings[0].Animation.WithScenarios(endings);
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

/// <summary>One ending of a score with scenarios: its name and its animation, shared beginning included.</summary>
public sealed record AnimatedScenario(string Name, AnimatedDiagram Animation);

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

	internal AnimatedDiagram(SvgScene scene, CompiledTimeline timeline, PlaybackMode mode, bool still, string? scenario = null)
	{
		Scenario = scenario;
		_scene = scene;
		_timeline = timeline;
		Mode = mode;
		Duration = TimeSpan.FromMilliseconds(timeline.Duration);
		ScoreDuration = TimeSpan.FromMilliseconds(timeline.ScoreEnd);
		Intro = TimeSpan.FromMilliseconds(timeline.Loops ? timeline.StoryStart : 0);
		Steps = timeline.Steps.Select(s => new AnimationStep(s.Caption, TimeSpan.FromMilliseconds(s.Start), TimeSpan.FromMilliseconds(s.End))).ToList();
		Describe(scene.Root, Steps, scenario);
		Svg = still ? FrameAt(timeline.ScoreEnd) : Animated();
	}

	private AnimatedDiagram(string svg)
	{
		Svg = svg;
		Steps = [];
	}

	internal static AnimatedDiagram Static(string svg) => new(svg);

	/// <summary>
	/// The score's endings, each a complete animation, when it has scenarios; empty otherwise. This animation is the
	/// first of them: its <see cref="Svg"/> plays the first ending, and <see cref="RenderPlayer"/> offers every one.
	/// </summary>
	public IReadOnlyList<AnimatedScenario> Scenarios { get; private set; } = [];

	/// <summary>The name of the ending this animation plays, when the score has scenarios.</summary>
	public string? Scenario { get; }

	/// <summary>How the ending turns out, when its scenario says (<c>as fail</c>).</summary>
	internal Role? ScenarioRole { get; init; }

	internal RoleColors? Colors { get; init; }

	/// <summary>Whether the player lists the steps: the host's choice, else the score's, else automatic.</summary>
	internal PlayerContents Contents { get; init; }

	/// <summary>The same animation compiled for the player: captions beside the figure instead of in a band above it.</summary>
	internal Func<AnimatedDiagram>? ForPlayer { get; set; }

	internal AnimatedDiagram WithScenarios(IReadOnlyList<AnimatedScenario> scenarios)
	{
		Scenarios = scenarios;
		return this;
	}

	/// <summary>
	/// The story, told to a screen reader: the steps' captions in order, after the description the author gave with
	/// <c>accDescr</c> if any. The diagram's own text stays readable as it is.
	/// </summary>
	private static void Describe(XElement root, IReadOnlyList<AnimationStep> steps, string? scenario)
	{
		var captions = steps.Select(s => s.Caption).OfType<string>().Where(c => c.Trim().Length > 0).ToList();
		if (captions.Count == 0)
			return;
		var told = new System.Text.StringBuilder(scenario is null ? "" : scenario.TrimEnd('.') + ". ");
		_ = told.Append(captions.Count == 1 ? "" : $"In {captions.Count} steps: ");
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

	/// <summary>One full cycle, including the rest and the settle between loops.</summary>
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
	public string RenderPlayer() => _scene is null ? $"<figure>\n{Svg}\n</figure>" : StoryPlayer.Render(ForPlayer?.Invoke() ?? this);

	/// <summary>The prefix every class and keyframe of this animation starts with.</summary>
	internal string Prefix => _scene?.Prefix ?? "";

	/// <summary>For an ending: how many of its steps are the beginning every ending shares.</summary>
	internal int SharedSteps { get; init; }

	internal CompiledTimeline? Timeline => _timeline;

	/// <summary>
	/// Every animated element's class with where each of its animations' clocks starts, in the order its <c>animation</c>
	/// list names them. Under reduced motion an element runs the start of that list, so the same delays fit it.
	/// </summary>
	internal List<(string Class, string Starts)> AnimatedClasses()
	{
		lock (_gate)
		{
			var t = _timeline!;
			return CssEmitter
				.Plan(t.Channels, t.Duration, t.StoryStart, t.Loops)
				.Where(g => _classes.ContainsKey(g.Element))
				.Select(g => (_classes[g.Element], Starts(g.Entries)))
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

	private static string Starts(IEnumerable<CssEmitter.Entry> entries) =>
		string.Join(",", entries.Select(e => e.Start.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));

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
