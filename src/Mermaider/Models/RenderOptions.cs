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

	/// <summary>Canvas padding in px around flowchart and state diagrams (other diagram types use their own margins). Default: 16.</summary>
	public double? Padding { get; init; }

	/// <summary>Spacing between sibling nodes in flowchart and state diagrams. Default: 36.</summary>
	public double? NodeSpacing { get; init; }

	/// <summary>Spacing between layers in flowchart and state diagrams. Default: 40.</summary>
	public double? LayerSpacing { get; init; }

	/// <summary>Use rounded corners on edge paths. Default: true (radius 6px).</summary>
	public bool RoundedEdges { get; init; } = true;

	/// <summary>Render with transparent background. Default: true.</summary>
	public bool Transparent { get; init; } = true;

	/// <summary>
	/// The visual style preset every diagram type is painted in: <see cref="DiagramStyle.Quiet"/> (tinted, soft gradient),
	/// <see cref="DiagramStyle.Blueprint"/> (outline-first technical drawing) or <see cref="DiagramStyle.Tonal"/> (filled tonal blocks).
	/// Presets only change paint; layout and the colour inputs are the same for all three. Default: Quiet.
	/// </summary>
	public DiagramStyle Style { get; init; } = DiagramStyle.Quiet;

	/// <summary>Fill boxes, containers and bars with a subtle top-to-bottom gradient. When false every fill is flat. Default: true.</summary>
	public bool Gradient { get; init; } = true;

	/// <summary>
	/// Strength of every tint derived from a colour family (node fills, header bands, container bodies, borders), 0.5–1.5.
	/// Lower values keep dark themes from looking muddy. Default: 1.
	/// </summary>
	public double? Tint { get; init; }

	/// <summary>Drop-shadow strength: 0 none, 1 boxes and containers, 2 adds an ambient shadow. Ignored by Blueprint. Default: 1.</summary>
	public int? Elevation { get; init; }

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

	/// <summary>Colour of an ordinary box (first cluster of nodes, entities and classes). Default: the theme's default box colour (every built-in theme sets one; slate in the zinc themes); without one, the first palette colour that is not a role hue.</summary>
	public string? Default { get; init; }

	/// <summary>Semantic role colour for success. Nodes with the class <c>success</c> use it; automatic colouring avoids its hue. Default: the theme's success colour; without one, palette green.</summary>
	public string? Success { get; init; }

	/// <summary>Semantic role colour for failure (class <c>failure</c>). Default: the theme's failure colour; without one, palette red.</summary>
	public string? Failure { get; init; }

	/// <summary>Semantic role colour for warning (class <c>warning</c>). Default: the theme's warning colour; without one, palette yellow.</summary>
	public string? Warning { get; init; }

	/// <summary>Semantic role colour for information (class <c>info</c>). Default: the theme's info colour; without one, palette blue.</summary>
	public string? Info { get; init; }


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
	/// Optional callback invoked once per diagram with all SVG elements and attributes stripped by
	/// the always-on sanitizer when <see cref="SanitizeMode"/> is <see cref="Models.SanitizeMode.Strip"/>.
	/// The list contains every violation found in one render call. Not called when there are no
	/// violations, and not called in Block mode (an exception is thrown instead).
	/// </summary>
	public Action<IReadOnlyList<SvgViolation>>? OnSanitized { get; init; }

	/// <summary>
	/// Resource limits for the parse + layout + render pipeline.
	/// Defaults to <see cref="ResourceLimits.Default"/> — generous on-by-default
	/// limits that reject pathological inputs while accommodating real-world diagrams.
	/// Set to <see cref="ResourceLimits.Unlimited"/> to disable all checks for trusted input.
	/// </summary>
	public ResourceLimits Limits { get; init; } = ResourceLimits.Default;
}
