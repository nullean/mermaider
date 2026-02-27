namespace Mermaider.Theming;

/// <summary>
/// Diagram color configuration.
/// <para>
/// Required: <see cref="Bg"/> + <see cref="Fg"/> give a clean mono diagram.
/// Optional enrichment colors bring in richer color from themes or custom palettes.
/// </para>
/// </summary>
public sealed record DiagramColors
{
	/// <summary>Background color.</summary>
	public required string Bg { get; init; }

	/// <summary>Foreground / primary text color.</summary>
	public required string Fg { get; init; }

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
}
