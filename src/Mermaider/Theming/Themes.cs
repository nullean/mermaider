using System.Collections.Frozen;
using Mermaider.Rendering;

namespace Mermaider.Theming;

/// <summary>Built-in theme palettes.</summary>
public static class Themes
{
	/// <summary>Default colors (zinc light).</summary>
	public static DiagramColors Default { get; } = WithLine(new()
	{
		Bg = "#FFFFFF", Fg = "#27272A", Accent = "#3b82f6",
		Default = "#64748b", Success = CategoricalPalette.Green, Failure = CategoricalPalette.Red,
		Warning = CategoricalPalette.Yellow, Info = CategoricalPalette.Blue,
	});

	/// <summary>The canonical 12-color data palette used when a theme does not specify its own.</summary>
	public static string[] DefaultDataPalette { get; } = CategoricalPalette.Colors;

	// Shared brightened data palette for dark backgrounds — generated once.
	private static readonly string[] DarkDataPalette = CategoricalPalette.BrightColors;

	/// <summary>All 15 built-in themes.</summary>
	public static FrozenDictionary<string, DiagramColors> BuiltIn { get; } =
		new Dictionary<string, DiagramColors>
		{
			["zinc-light"] = new()
			{
				Bg = "#FFFFFF", Fg = "#27272A", Accent = "#3b82f6",
				Default = "#64748b", Success = CategoricalPalette.Green, Failure = CategoricalPalette.Red,
				Warning = CategoricalPalette.Yellow, Info = CategoricalPalette.Blue,
			},
			["zinc-dark"] = new()
			{
				Bg = "#18181B", Fg = "#FAFAFA", Accent = "#60a5fa",
				Default = "#94a3b8", Success = DarkDataPalette[4], Failure = DarkDataPalette[2],
				Warning = DarkDataPalette[5], Info = DarkDataPalette[0],
				DataPalette = DarkDataPalette
			},
			["tokyo-night"] = new()
			{
				Bg = "#1a1b26", Fg = "#a9b1d6",
				Accent = "#7aa2f7", Muted = "#7982aa",
				Default = "#7982aa", Success = "#9ece6a", Failure = "#f7768e", Warning = "#e0af68", Info = "#7dcfff",
				DataPalette = DarkDataPalette
			},
			["tokyo-night-storm"] = new()
			{
				Bg = "#24283b", Fg = "#a9b1d6",
				Accent = "#7aa2f7", Muted = "#7982aa",
				Default = "#7982aa", Success = "#9ece6a", Failure = "#f7768e", Warning = "#e0af68", Info = "#7dcfff",
				DataPalette = DarkDataPalette
			},
			["tokyo-night-light"] = new()
			{
				Bg = "#d5d6db", Fg = "#343b58",
				Accent = "#34548a", Muted = "#9699a3",
				Default = "#6c6e8a", Success = "#485e30", Failure = "#8c4351", Warning = "#8f5e15", Info = "#166775",
			},
			["catppuccin-mocha"] = new()
			{
				Bg = "#1e1e2e", Fg = "#cdd6f4",
				Accent = "#cba6f7", Muted = "#9399b2",
				Default = "#9399b2", Success = "#a6e3a1", Failure = "#f38ba8", Warning = "#f9e2af", Info = "#89dceb",
				DataPalette = DarkDataPalette
			},
			["catppuccin-latte"] = new()
			{
				Bg = "#eff1f5", Fg = "#4c4f69",
				Accent = "#8839ef", Muted = "#9ca0b0",
				Default = "#7c7f93", Success = "#40a02b", Failure = "#d20f39", Warning = "#df8e1d", Info = "#04a5e5",
			},
			["nord"] = new()
			{
				Bg = "#2e3440", Fg = "#d8dee9",
				Accent = "#88c0d0", Muted = "#8896af",
				Default = "#8896af", Success = "#a3be8c", Failure = "#bf616a", Warning = "#ebcb8b", Info = "#81a1c1",
				DataPalette = DarkDataPalette
			},
			["nord-light"] = new()
			{
				Bg = "#eceff4", Fg = "#2e3440",
				Accent = "#5e81ac", Muted = "#7b88a1",
				Default = "#7b88a1", Success = "#8fb075", Failure = "#bf616a", Warning = "#d08770", Info = "#5e81ac",
			},
			["dracula"] = new()
			{
				Bg = "#282a36", Fg = "#f8f8f2",
				Accent = "#bd93f9", Muted = "#8899c7",
				Default = "#8899c7", Success = "#50fa7b", Failure = "#ff5555", Warning = "#f1fa8c", Info = "#8be9fd",
				DataPalette = DarkDataPalette
			},
			["github-light"] = new()
			{
				Bg = "#ffffff", Fg = "#1f2328",
				Accent = "#0969da", Muted = "#59636e",
				Default = "#59636e", Success = "#1a7f37", Failure = "#d1242f", Warning = "#9a6700", Info = "#0969da",
			},
			["github-dark"] = new()
			{
				Bg = "#0d1117", Fg = "#e6edf3",
				Accent = "#4493f8", Muted = "#9198a1",
				Default = "#9198a1", Success = "#3fb950", Failure = "#f85149", Warning = "#d29922", Info = "#4493f8",
				DataPalette = DarkDataPalette
			},
			["solarized-light"] = new()
			{
				Bg = "#fdf6e3", Fg = "#657b83",
				Accent = "#268bd2", Muted = "#93a1a1",
				Default = "#839496", Success = "#859900", Failure = "#dc322f", Warning = "#b58900", Info = "#2aa198",
			},
			["solarized-dark"] = new()
			{
				Bg = "#002b36", Fg = "#839496",
				Accent = "#268bd2", Muted = "#839496",
				Default = "#93a1a1", Success = "#859900", Failure = "#dc322f", Warning = "#b58900", Info = "#2aa198",
				DataPalette = DarkDataPalette
			},
			["one-dark"] = new()
			{
				Bg = "#282c34", Fg = "#abb2bf",
				Accent = "#c678dd", Muted = "#828997",
				Default = "#828997", Success = "#98c379", Failure = "#e06c75", Warning = "#e5c07b", Info = "#61afef",
				DataPalette = DarkDataPalette
			},
		}.ToFrozenDictionary(kv => kv.Key, kv => WithLine(kv.Value));

	/// <summary>
	/// Every built-in theme spells out its line colour: the border of an ordinary box (the <c>Default</c> role mixed 74% into
	/// the foreground, the same mix the renderer outlines boxes with), so lines and boxes read as one family.
	/// </summary>
	private static DiagramColors WithLine(DiagramColors theme) =>
		theme with { Line = ColorUtils.MixHex(theme.Default!, theme.Fg, ColorFamily.StrokeRatio / 100) };
}
