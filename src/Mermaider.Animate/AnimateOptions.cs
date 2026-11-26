namespace Mermaider.Animate;

/// <summary>How an animation advances.</summary>
public enum PlaybackMode
{
	/// <summary>Plays, rests on the last frame, settles back to how the story began and plays again.</summary>
	Loop,

	/// <summary>Plays to the end and holds the last frame.</summary>
	Once,

	/// <summary>
	/// Progress follows the diagram scrolling through the viewport (<c>animation-timeline: view()</c>). Browsers without
	/// scroll-driven animations play it once instead. Only an inline SVG scrolls with the page: as an <c>&lt;img&gt;</c>
	/// the timeline is inactive and the static diagram shows.
	/// </summary>
	Scrub,
}

/// <summary>The motion preset: every duration, easing and rest comes from it, never from the score.</summary>
public enum MotionPreset
{
	/// <summary>Unhurried, the default.</summary>
	Calm,

	/// <summary>About a third faster, with shorter rests.</summary>
	Crisp,

	/// <summary>No motion at all: the result is the final frame as a static SVG.</summary>
	None,
}

/// <summary>Whether the player lists the story's steps beside the figure.</summary>
public enum PlayerContents
{
	/// <summary>
	/// The list shows when there is something to find in it: three or more captioned steps, or endings to choose
	/// between. Shorter stories show the figure and its caption alone.
	/// </summary>
	Auto,

	/// <summary>The list always shows (<c>animate contents</c>).</summary>
	Always,

	/// <summary>
	/// The figure and its caption alone, the controls at the caption's end (<c>animate compact</c>). A score with endings
	/// still lists them: choosing one needs the list.
	/// </summary>
	Never,
}

/// <summary>Options for <see cref="MermaidAnimator"/>.</summary>
public sealed record AnimateOptions
{
	/// <summary>Options for the underlying diagram render (theme, style, colours, limits).</summary>
	public RenderOptions? Render { get; init; }

	/// <summary>Overrides the preset the score names. The host decides the motion style, as it decides the colours.</summary>
	public MotionPreset? Motion { get; init; }

	/// <summary>Overrides the playback mode the score names.</summary>
	public PlaybackMode? Mode { get; init; }

	/// <summary>Overrides whether the player lists the steps, which the score can name (<c>contents</c>, <c>compact</c>).</summary>
	public PlayerContents? Contents { get; init; }

	/// <summary>
	/// The most steps a score may produce (default 64). A longer score is rejected rather than rendered, like any
	/// other resource limit.
	/// </summary>
	public int MaxSteps { get; init; } = 64;
}
