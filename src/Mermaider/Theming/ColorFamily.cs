using System.Globalization;

namespace Mermaider.Theming;

/// <summary>
/// A colour family: one base colour and the stages every renderer derives from it. Clusters (<c>p0…</c>), the neutral
/// (<c>n</c>), the accent (<c>a</c>), the semantic roles (<c>s f w i</c>) all go through the same derivation, so a shape
/// never asks "which colour?", only "which family?". Every stage is a <c>color-mix(in srgb, …)</c> against the page
/// background or foreground, so it re-themes with <c>--bg</c> / <c>--fg</c>.
/// </summary>
internal sealed record ColorFamily
{
	/// <summary>Short stable key used for gradient ids (<c>p0</c>, <c>n</c>, <c>a</c>, <c>s</c>, …).</summary>
	internal string Key { get; }

	/// <summary>The base colour: a palette / role hex, <c>var(--_line)</c> for the neutral, the accent variable for the accent.</summary>
	internal string Base { get; }

	private readonly double _tint;

	internal ColorFamily(string key, string baseColor, double tint = 1.0)
	{
		Key = key;
		Base = baseColor;
		_tint = tint;
	}

	/// <summary>The accent as a colour value usable inside <c>color-mix</c> (falls back to the foreground).</summary>
	internal const string AccentBase = "var(--accent, var(--fg))";

	internal const string NeutralBase = "var(--_line)";

	internal static ColorFamily Accent(double tint = 1.0) => new("a", AccentBase, tint);

	internal static ColorFamily Neutral(double tint = 1.0) => new("n", NeutralBase, tint);

	// Stage ratios (percent of the base mixed into bg, or into fg for stroke / ink), from the token spec.
	internal const double TopRatio = 15;
	internal const double BotRatio = 7;
	internal const double FlatRatio = 11;
	internal const double BandRatio = 22;
	internal const double SoftRatio = 26;
	internal const double TintRatio = 6;
	internal const double TintStep = 3;
	internal const double EdgeRatio = 42;
	internal const double StrokeRatio = 74;
	internal const double InkRatio = 44;
	internal const double WashAccentRatio = 8;

	/// <summary>Opacity of the area stage (regions that overlap: radar, venn, sankey).</summary>
	internal const double AreaOpacity = 0.2;

	/// <summary>Node gradient start.</summary>
	internal string Top => ToBg(TopRatio * _tint);

	/// <summary>Node gradient end.</summary>
	internal string Bot => ToBg(BotRatio * _tint);

	/// <summary>Node fill when gradients are off.</summary>
	internal string Flat => ToBg(FlatRatio * _tint);

	/// <summary>Header band, badge fill, tag pill.</summary>
	internal string Band => ToBg(BandRatio * _tint);

	/// <summary>Tonal fill, treemap leaf, gantt done.</summary>
	internal string Soft => ToBg(SoftRatio * _tint);

	/// <summary>Container body at nesting depth <paramref name="depth"/> (+3% per level).</summary>
	internal string Tint(int depth = 0) => ToBg((TintRatio + (TintStep * depth)) * _tint);

	/// <summary>Container border, soft rules.</summary>
	internal string Edge => ToBg(EdgeRatio * _tint);

	/// <summary>Node outline, bar outline, markers (mixed towards the foreground so it holds 3:1 in both modes).</summary>
	internal string Stroke => ToFg(StrokeRatio);

	/// <summary>Titles in containers, stereotypes, badge text (≥ 4.5:1 on the band).</summary>
	internal string Ink => ToFg(InkRatio);

	/// <summary>A tint of this family at an arbitrary ratio (value ramps, heat).</summary>
	internal string Mix(double percent) => ToBg(percent);

	private string ToBg(double percent) => Mix(Base, percent, "var(--bg)");

	private string ToFg(double percent) => Mix(Base, percent, "var(--fg)");

	internal static string Mix(string color, double percent, string target)
	{
		var p = Math.Clamp(Math.Round(percent, 1), 0, 100).ToString("0.#", CultureInfo.InvariantCulture);
		return $"color-mix(in srgb, {color} {p}%, {target})";
	}
}
