namespace Mermaider.Rendering;

/// <summary>
/// Single source of truth for the categorical data palette used across all diagram types
/// that encode data via color (pie, treemap, sankey, radar, timeline, mindmap, venn,
/// xychart, gitgraph, journey, packet).
///
/// The 12-color sequence is the Tableau "Color Blind 10" extended with two additional
/// accessible entries. All diagram types pick colors via <see cref="At"/> so any future
/// palette change propagates everywhere automatically.
/// </summary>
internal static class CategoricalPalette
{
	/// <summary>The 12-color canonical sequence — tuned for light backgrounds.</summary>
	internal static readonly string[] Colors =
	[
		"#4e79a7", // Blue
		"#f28e2b", // Orange
		"#e15759", // Red
		"#76b7b2", // Teal
		"#59a14f", // Green
		"#edc948", // Yellow
		"#b07aa1", // Purple
		"#ff9da7", // Pink
		"#9c755f", // Brown
		"#bab0ac", // Gray
		"#86bcb6", // LightTeal
		"#8cd17d", // LightGreen
	];

	/// <summary>Brightened variants of <see cref="Colors"/> for use on dark backgrounds.</summary>
	internal static readonly string[] BrightColors = [.. Colors.Select(c => Theming.ColorUtils.AdjustLightness(c, 0.18))];

	/// <summary>Index-wrapped access — wraps around when <paramref name="i"/> exceeds length.</summary>
	internal static string At(int i) => Colors[i % Colors.Length];

	// Named semantic aliases — base palette (good on light backgrounds)
	internal static string Blue => Colors[0];
	internal static string Orange => Colors[1];
	internal static string Red => Colors[2];
	internal static string Teal => Colors[3];
	internal static string Green => Colors[4];
	internal static string Yellow => Colors[5];
	internal static string Purple => Colors[6];
	internal static string Pink => Colors[7];
	internal static string Brown => Colors[8];
	internal static string Gray => Colors[9];
	internal static string LightTeal => Colors[10];
	internal static string LightGreen => Colors[11];

	// Dark variants — same hue, ~20% lower lightness (suitable for strokes and borders)
	internal static readonly string BlueDark = Theming.ColorUtils.AdjustLightness(Colors[0], -0.20);
	internal static readonly string OrangeDark = Theming.ColorUtils.AdjustLightness(Colors[1], -0.20);
	internal static readonly string RedDark = Theming.ColorUtils.AdjustLightness(Colors[2], -0.20);
	internal static readonly string TealDark = Theming.ColorUtils.AdjustLightness(Colors[3], -0.20);
	internal static readonly string GreenDark = Theming.ColorUtils.AdjustLightness(Colors[4], -0.20);
	internal static readonly string YellowDark = Theming.ColorUtils.AdjustLightness(Colors[5], -0.20);
	internal static readonly string PurpleDark = Theming.ColorUtils.AdjustLightness(Colors[6], -0.20);
	internal static readonly string PinkDark = Theming.ColorUtils.AdjustLightness(Colors[7], -0.20);
	internal static readonly string BrownDark = Theming.ColorUtils.AdjustLightness(Colors[8], -0.20);
	internal static readonly string GrayDark = Theming.ColorUtils.AdjustLightness(Colors[9], -0.20);
	internal static readonly string LightTealDark = Theming.ColorUtils.AdjustLightness(Colors[10], -0.20);
	internal static readonly string LightGreenDark = Theming.ColorUtils.AdjustLightness(Colors[11], -0.20);
}
