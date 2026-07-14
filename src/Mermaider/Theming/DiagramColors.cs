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

	/// <summary>
	/// Categorical data palette used by color-encoded diagram types (pie, sankey, timeline, etc.).
	/// When null, falls back to <see cref="Rendering.CategoricalPalette.Colors"/>.
	/// </summary>
	public string[]? DataPalette { get; init; }

	/// <summary>Returns color <paramref name="i"/> from the active data palette, wrapping around.</summary>
	internal string PaletteAt(int i)
	{
		var palette = DataPalette ?? Rendering.CategoricalPalette.Colors;
		return palette[i % palette.Length];
	}
}
