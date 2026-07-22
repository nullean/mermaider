namespace Mermaider.Models;

/// <summary>
/// A named style class for diagram nodes. When <see cref="Fill"/> is provided,
/// CSS rules are emitted inside the SVG. When only <see cref="Name"/> is set
/// (no colors), the class is treated as external — the SVG element gets the
/// class attribute but styling is expected from an external stylesheet.
/// </summary>
public sealed record DiagramClass
{
	/// <summary>Class name usable via <c>:::name</c> or <c>class A name</c>.</summary>
	public required string Name { get; init; }

	/// <summary>Node background color (light mode). Null = external class.</summary>
	public string? Fill { get; init; }

	/// <summary>Node border color (light mode).</summary>
	public string? Stroke { get; init; }

	/// <summary>Node text color (light mode).</summary>
	public string? Color { get; init; }

	/// <summary>Node background color (dark mode). Auto-derived from <see cref="Fill"/> if null.</summary>
	public string? DarkFill { get; init; }

	/// <summary>Node border color (dark mode). Auto-derived from <see cref="Stroke"/> if null.</summary>
	public string? DarkStroke { get; init; }

	/// <summary>Node text color (dark mode). Auto-derived from <see cref="Color"/> if null.</summary>
	public string? DarkColor { get; init; }

	/// <summary>Whether this is an external class (no colors — styling comes from external CSS).</summary>
	internal bool IsExternal => Fill is null && Stroke is null && Color is null;
}

/// <summary>
/// Controls what the always-on SVG sanitizer does when the rendered output contains
/// content outside the element/attribute allowlist. Sanitization itself is not optional —
/// this only selects the reaction to a violation. See <see cref="RenderOptions.SanitizeMode"/>.
/// </summary>
public enum SanitizeMode
{
	/// <summary>
	/// Remove disallowed content from well-formed SVG and return the cleaned document.
	/// Malformed XML returns <see cref="MermaidRenderer.FallbackSvg"/>.
	/// </summary>
	Strip,

	/// <summary>Throw <see cref="MermaidSvgException"/> when any SVG violation is found.</summary>
	Block
}

/// <summary>
/// Controls how <see cref="StrictStylingOptions"/> reacts when source-authored styling
/// directives (<c>classDef</c>, <c>style</c>, <c>linkStyle</c>, C4 <c>Update*Style</c>,
/// <c>%%{init}%%</c> theme overrides) or unknown class references are encountered.
/// </summary>
public enum StrictStylingMode
{
	/// <summary>
	/// Silently strip disallowed styling directives and unknown class references and
	/// continue rendering. Dropped items are reported via
	/// <see cref="StrictStylingOptions.OnStripped"/> when a callback is provided.
	/// This is the default — it mirrors <see cref="SanitizeMode.Strip"/>.
	/// </summary>
	Strip,

	/// <summary>
	/// Throw <see cref="MermaidParseException"/> on the first disallowed directive or
	/// unknown class reference (the original strict-mode behavior).
	/// </summary>
	Block
}

/// <summary>Classifies what kind of styling directive was stripped in strip mode.</summary>
public enum StrictStylingViolationKind
{
	/// <summary>A <c>classDef</c> directive was stripped.</summary>
	ClassDefDirective,

	/// <summary>A <c>style</c> (per-node inline style) directive was stripped.</summary>
	StyleDirective,

	/// <summary>A <c>linkStyle</c> directive was stripped.</summary>
	LinkStyleDirective,

	/// <summary>A C4 <c>Update*Style</c> directive was stripped.</summary>
	UpdateStyleDirective,

	/// <summary>A class reference (<c>:::name</c> or <c>class A name</c>) to an unknown class was stripped.</summary>
	UnknownClassReference,

	/// <summary>A source-authored <c>theme</c> or <c>themeVariables</c> override was stripped.</summary>
	ThemeOverride,
}

/// <summary>Describes a single styling directive or class reference that was stripped in strip mode.</summary>
public sealed record StrictStylingViolation
{
	/// <summary>What kind of directive or reference was stripped.</summary>
	public required StrictStylingViolationKind Kind { get; init; }

	/// <summary>Human-readable description of what was stripped and why.</summary>
	public required string Message { get; init; }

	/// <summary>1-based line number of the offending source line. 0 when not line-scoped (e.g. theme override).</summary>
	public int Line { get; init; }

	/// <summary>The offending source text (line content or class name).</summary>
	public required string Source { get; init; }
}

/// <summary>
/// Strict <b>styling</b> configuration — enforces visual uniformity, not security.
/// (SVG sanitization is always on and independent of this; see <see cref="RenderOptions.SanitizeMode"/>.)
/// When set, source-authored styling is controlled — <c>classDef</c>, <c>style</c>, <c>linkStyle</c>,
/// C4 <c>Update*Style</c>, and <c>%%{init}%%</c>/frontmatter <c>theme</c>/<c>themeVariables</c> overrides —
/// so appearance is controlled by the host design system, permitting only pre-approved class names.
/// <para>
/// In <see cref="StrictStylingMode.Strip"/> mode (the default) disallowed directives are silently
/// dropped and the diagram still renders; in <see cref="StrictStylingMode.Block"/> mode they throw
/// <see cref="MermaidParseException"/>.
/// </para>
/// </summary>
public sealed record StrictStylingOptions
{
	/// <summary>
	/// Allowed class definitions with theme-aware colors.
	/// Nodes can reference these via <c>:::name</c> or <c>class A name</c>.
	/// Classes without colors act as external references.
	/// </summary>
	public IReadOnlyList<DiagramClass> AllowedClasses { get; init; } = [];

	/// <summary>
	/// How to react when a disallowed styling directive or unknown class reference is found.
	/// Default: <see cref="StrictStylingMode.Strip"/> (drop and continue, mirroring
	/// <see cref="SanitizeMode.Strip"/>).
	/// </summary>
	public StrictStylingMode Mode { get; init; } = StrictStylingMode.Strip;

	/// <summary>
	/// Optional callback invoked once per diagram with all styling directives and class references
	/// that were stripped when <see cref="Mode"/> is <see cref="StrictStylingMode.Strip"/>. The list
	/// contains every violation found in one render call, ordered by source line. Not called when
	/// there are no violations, and not called in <see cref="StrictStylingMode.Block"/> mode.
	/// </summary>
	public Action<IReadOnlyList<StrictStylingViolation>>? OnStripped { get; init; }
}
