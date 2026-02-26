namespace Mermaid.Models;

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

/// <summary>Controls how the SVG sanitizer handles disallowed content.</summary>
public enum SvgSanitizeMode
{
	/// <summary>Silently remove disallowed elements and attributes from the output.</summary>
	Strip,

	/// <summary>Throw <see cref="MermaidParseException"/> on the first disallowed element or attribute.</summary>
	Block
}

/// <summary>
/// Strict mode configuration. Disallows <c>classDef</c> and <c>style</c> directives
/// in Mermaid source, permitting only pre-approved class names.
/// Optionally sanitizes the final SVG output against an element/attribute allowlist.
/// </summary>
public sealed record StrictModeOptions
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

	/// <summary>
	/// When set, a final pass over the rendered SVG ensures only allowlisted
	/// elements and attributes are present. Default: <see cref="SvgSanitizeMode.Strip"/>.
	/// Set to <c>null</c> to disable sanitization entirely.
	/// </summary>
	public SvgSanitizeMode? Sanitize { get; init; } = SvgSanitizeMode.Strip;
}
