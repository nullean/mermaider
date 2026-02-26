using System.Collections.Frozen;

namespace Mermaid.Theming;

/// <summary>Built-in theme palettes.</summary>
public static class Themes
{
	/// <summary>Default colors (zinc light).</summary>
	public static DiagramColors Default { get; } = new() { Bg = "#FFFFFF", Fg = "#27272A" };

	/// <summary>All 15 built-in themes.</summary>
	public static FrozenDictionary<string, DiagramColors> BuiltIn { get; } =
		new Dictionary<string, DiagramColors>
		{
			["zinc-light"] = new() { Bg = "#FFFFFF", Fg = "#27272A" },
			["zinc-dark"] = new() { Bg = "#18181B", Fg = "#FAFAFA" },
			["tokyo-night"] = new()
			{
				Bg = "#1a1b26", Fg = "#a9b1d6",
				Line = "#3d59a1", Accent = "#7aa2f7", Muted = "#565f89"
			},
			["tokyo-night-storm"] = new()
			{
				Bg = "#24283b", Fg = "#a9b1d6",
				Line = "#3d59a1", Accent = "#7aa2f7", Muted = "#565f89"
			},
			["tokyo-night-light"] = new()
			{
				Bg = "#d5d6db", Fg = "#343b58",
				Line = "#34548a", Accent = "#34548a", Muted = "#9699a3"
			},
			["catppuccin-mocha"] = new()
			{
				Bg = "#1e1e2e", Fg = "#cdd6f4",
				Line = "#585b70", Accent = "#cba6f7", Muted = "#6c7086"
			},
			["catppuccin-latte"] = new()
			{
				Bg = "#eff1f5", Fg = "#4c4f69",
				Line = "#9ca0b0", Accent = "#8839ef", Muted = "#9ca0b0"
			},
			["nord"] = new()
			{
				Bg = "#2e3440", Fg = "#d8dee9",
				Line = "#4c566a", Accent = "#88c0d0", Muted = "#616e88"
			},
			["nord-light"] = new()
			{
				Bg = "#eceff4", Fg = "#2e3440",
				Line = "#aab1c0", Accent = "#5e81ac", Muted = "#7b88a1"
			},
			["dracula"] = new()
			{
				Bg = "#282a36", Fg = "#f8f8f2",
				Line = "#6272a4", Accent = "#bd93f9", Muted = "#6272a4"
			},
			["github-light"] = new()
			{
				Bg = "#ffffff", Fg = "#1f2328",
				Line = "#d1d9e0", Accent = "#0969da", Muted = "#59636e"
			},
			["github-dark"] = new()
			{
				Bg = "#0d1117", Fg = "#e6edf3",
				Line = "#3d444d", Accent = "#4493f8", Muted = "#9198a1"
			},
			["solarized-light"] = new()
			{
				Bg = "#fdf6e3", Fg = "#657b83",
				Line = "#93a1a1", Accent = "#268bd2", Muted = "#93a1a1"
			},
			["solarized-dark"] = new()
			{
				Bg = "#002b36", Fg = "#839496",
				Line = "#586e75", Accent = "#268bd2", Muted = "#586e75"
			},
			["one-dark"] = new()
			{
				Bg = "#282c34", Fg = "#abb2bf",
				Line = "#4b5263", Accent = "#c678dd", Muted = "#5c6370"
			},
		}.ToFrozenDictionary();
}
