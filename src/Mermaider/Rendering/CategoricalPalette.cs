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
	/// <summary>The 12-color canonical sequence.</summary>
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
		"#86bcb6", // Light teal
		"#8cd17d", // Light green
	];

	/// <summary>Index-wrapped access — wraps around when <paramref name="i"/> exceeds length.</summary>
	internal static string At(int i) => Colors[i % Colors.Length];

	// Named semantic aliases for diagram types that use colors by role, not position.
	internal static string Blue => Colors[0];
	internal static string Orange => Colors[1];
	internal static string Red => Colors[2];
	internal static string Teal => Colors[3];
	internal static string Green => Colors[4];
	internal static string Yellow => Colors[5];
	internal static string Purple => Colors[6];
	internal static string Pink => Colors[7];
}
