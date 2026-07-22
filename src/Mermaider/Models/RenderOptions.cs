using Mermaider.Layout;

namespace Mermaider.Models;

/// <summary>Options for rendering a Mermaid diagram to SVG.</summary>
public sealed record RenderOptions
{
	/// <summary>Background color (hex or CSS variable). Default: "#FFFFFF".</summary>
	public string? Bg { get; init; }

	/// <summary>Foreground / primary text color. Default: "#27272A".</summary>
	public string? Fg { get; init; }

	/// <summary>Edge/connector color override.</summary>
	public string? Line { get; init; }

	/// <summary>Arrow heads, highlights color override.</summary>
	public string? Accent { get; init; }

	/// <summary>Secondary text, edge labels color override.</summary>
	public string? Muted { get; init; }

	/// <summary>Node fill tint color override.</summary>
	public string? Surface { get; init; }

	/// <summary>Node/group stroke color override.</summary>
	public string? Border { get; init; }

	/// <summary>Font family for all text. Default: "Inter".</summary>
	public string? Font { get; init; }

	/// <summary>
	/// Monospace font family for code-style text (e.g. ER attribute types, Class member signatures).
	/// When null, falls back to the built-in system monospace stack.
	/// </summary>
	public string? MonoFont { get; init; }

	/// <summary>Base font size (--fs-m). Allows px, rem, em, or percent values. Default: "1rem".</summary>
	public string? FontSize { get; init; }

	/// <summary>Ratio for small text (--fs-s). Default: 0.875.</summary>
	public double? FontSizeSmall { get; init; }

	/// <summary>Ratio for extra-small text (--fs-xs). Default: 0.75.</summary>
	public double? FontSizeExtraSmall { get; init; }

	/// <summary>Ratio for large text (--fs-l). Default: 1.125.</summary>
	public double? FontSizeLarge { get; init; }

	/// <summary>Canvas padding in px. Default: 40.</summary>
	public double? Padding { get; init; }

	/// <summary>Horizontal spacing between sibling nodes. Default: 28.</summary>
	public double? NodeSpacing { get; init; }

	/// <summary>Vertical spacing between layers. Default: 48.</summary>
	public double? LayerSpacing { get; init; }

	/// <summary>Use rounded corners on edge paths. Default: true (radius 6px).</summary>
	public bool RoundedEdges { get; init; } = true;

	/// <summary>Render with transparent background. Default: true.</summary>
	public bool Transparent { get; init; } = true;

	/// <summary>
	/// Override the layout provider for this render call only.
	/// When <c>null</c>, uses the global provider set via
	/// <see cref="MermaidRenderer.SetLayoutProvider"/>.
	/// </summary>
	public IGraphLayoutProvider? LayoutProvider { get; init; }

	/// <summary>
	/// Which diagram types this renderer will accept. Defaults to <see cref="DiagramTypes.All"/>.
	/// Diagrams whose detected type is not in this set throw <see cref="MermaidParseException"/>.
	/// </summary>
	public DiagramTypes AllowedDiagrams { get; init; } = DiagramTypes.All;

	/// <summary>
	/// Override the categorical data palette used by color-encoded diagram types
	/// (pie, sankey, timeline, radar, gitgraph, mindmap, venn, journey, packet, xychart, treemap).
	/// When null, the theme's built-in data palette is used (dark themes ship a brighter variant).
	/// </summary>
	public string[]? DataPalette { get; init; }

	/// <summary>
	/// Enable strict styling to enforce visual uniformity (not a security feature —
	/// SVG output is always sanitized regardless; see <see cref="SanitizeMode"/>).
	/// When set, source-authored styling directives are rejected and only pre-approved
	/// class names are allowed on nodes. See <see cref="StrictStylingOptions"/>.
	/// </summary>
	public StrictStylingOptions? Strict { get; init; }

	/// <summary>
	/// How the always-on SVG sanitizer reacts to disallowed content in the rendered
	/// output. Sanitization is non-optional — every rendered SVG is validated against
	/// the element/attribute allowlist regardless of this value; this only selects
	/// whether a violation is stripped or throws <see cref="MermaidSvgException"/>.
	/// Malformed XML in strip mode returns <see cref="MermaidRenderer.FallbackSvg"/>.
	/// Default: <see cref="Models.SanitizeMode.Strip"/>.
	/// </summary>
	public SanitizeMode SanitizeMode { get; init; } = SanitizeMode.Strip;

	/// <summary>
	/// Optional callback invoked for each SVG element or attribute stripped by the always-on
	/// sanitizer when <see cref="SanitizeMode"/> is <see cref="Models.SanitizeMode.Strip"/>.
	/// Use this to log or accumulate sanitizer violations without having to set
	/// <see cref="Models.SanitizeMode.Block"/>. Not called in Block mode (an exception is thrown instead).
	/// </summary>
	public Action<SvgViolation>? OnSanitized { get; init; }

	/// <summary>
	/// Resource limits for the parse + layout + render pipeline.
	/// Defaults to <see cref="ResourceLimits.Default"/> — generous on-by-default
	/// limits that reject pathological inputs while accommodating real-world diagrams.
	/// Set to <see cref="ResourceLimits.Unlimited"/> to disable all checks for trusted input.
	/// </summary>
	public ResourceLimits Limits { get; init; } = ResourceLimits.Default;
}
