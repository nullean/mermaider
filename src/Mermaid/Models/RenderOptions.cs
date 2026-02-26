namespace Mermaid.Models;

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

	/// <summary>Canvas padding in px. Default: 40.</summary>
	public double? Padding { get; init; }

	/// <summary>Horizontal spacing between sibling nodes. Default: 28.</summary>
	public double? NodeSpacing { get; init; }

	/// <summary>Vertical spacing between layers. Default: 48.</summary>
	public double? LayerSpacing { get; init; }

	/// <summary>Render with transparent background. Default: false.</summary>
	public bool Transparent { get; init; }

	/// <summary>
	/// Enable strict mode. When set, <c>classDef</c> and <c>style</c> directives
	/// are rejected, and only pre-approved class names are allowed on nodes.
	/// </summary>
	public StrictModeOptions? Strict { get; init; }
}
