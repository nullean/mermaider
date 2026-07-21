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
/// Strict <b>styling</b> configuration — enforces visual uniformity, not security.
/// (SVG sanitization is always on and independent of this; see <see cref="RenderOptions.SanitizeMode"/>.)
/// When set, source-authored styling is rejected — <c>classDef</c>, <c>style</c>, <c>linkStyle</c>,
/// C4 <c>Update*Style</c>, and <c>%%{init}%%</c>/frontmatter <c>theme</c>/<c>themeVariables</c> overrides —
/// so appearance is controlled by the host design system, permitting only pre-approved class names.
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
	/// When true, referencing a class name not in <see cref="AllowedClasses"/>
	/// throws <see cref="MermaidParseException"/>. When false, unknown class
	/// names are silently ignored (node gets default styling). Default: true.
	/// </summary>
	public bool RejectUnknownClasses { get; init; } = true;
}
