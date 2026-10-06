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

	/// <summary>Semantic role colour for success (done, passed, healthy). Optional: defaults to the palette green.</summary>
	public string? Success { get; init; }

	/// <summary>Semantic role colour for failure (error, rejected). Optional: defaults to the palette red.</summary>
	public string? Failure { get; init; }

	/// <summary>Semantic role colour for warning (caution, degraded). Optional: defaults to the palette yellow.</summary>
	public string? Warning { get; init; }

	/// <summary>Semantic role colour for information (note, neutral highlight). Optional: defaults to the palette blue.</summary>
	public string? Info { get; init; }

	/// <summary>
	/// Role colour of an ordinary box: the first cluster of flowchart / state nodes, ER entities and class boxes, and anything that
	/// is not singled out by a modifier or role. Optional: defaults to the first data-palette colour that is not a reserved role hue.
	/// </summary>
	public string? Default { get; init; }

	/// <summary>
	/// The line colour diagrams draw connectors, axes and rules in: <see cref="Line"/> when set, otherwise the border of an
	/// ordinary box (the <see cref="Default"/> role's outline stage), so lines and boxes read as one family.
	/// </summary>
	internal string ResolvedLine => Line is { Length: > 0 } line
		? line
		: ColorFamily.Mix(RoleColor(ColorRole.Default), ColorFamily.StrokeRatio, "var(--fg)");

	/// <summary>Returns color <paramref name="i"/> from the active data palette, wrapping around.</summary>
	internal string PaletteAt(int i)
	{
		var palette = DataPalette ?? Rendering.CategoricalPalette.Colors;
		return palette[i % palette.Length];
	}

	/// <summary>The resolved colour of a semantic role: the caller's value, else the matching hue of the default palette (brightened on dark backgrounds).</summary>
	internal string RoleColor(ColorRole role)
	{
		if (role == ColorRole.Default)
			return Default is { Length: > 0 } d ? d : ReservedFreePalette()[0];

		var custom = role switch
		{
			ColorRole.Success => Success,
			ColorRole.Failure => Failure,
			ColorRole.Warning => Warning,
			_ => Info,
		};
		if (custom is { Length: > 0 })
			return custom;

		var fallback = role switch
		{
			ColorRole.Success => Rendering.CategoricalPalette.Green,
			ColorRole.Failure => Rendering.CategoricalPalette.Red,
			ColorRole.Warning => Rendering.CategoricalPalette.Yellow,
			_ => Rendering.CategoricalPalette.Blue,
		};
		return ColorUtils.IsDark(Bg) ? ColorUtils.AdjustLightness(fallback, 0.18) : fallback;
	}

	/// <summary>The data palette without any hue that reads as a reserved role (success, failure, warning); greys and non-hex entries stay.</summary>
	private string[] ReservedFreePalette()
	{
		var palette = DataPalette ?? Rendering.CategoricalPalette.Colors;
		var reserved = new List<double>(3);
		foreach (var role in new[] { ColorRole.Success, ColorRole.Failure, ColorRole.Warning })
		{
			if (ColorUtils.TryHueSaturation(RoleColor(role), out var hue, out var sat) && sat > 0.12)
				reserved.Add(hue);
		}

		var kept = palette.Where(c =>
			!ColorUtils.TryHueSaturation(c, out var hue, out var sat)
			|| sat <= 0.12
			|| reserved.All(r => ColorUtils.HueDistance(hue, r) > RoleHueWindow)).ToArray();
		return kept.Length > 0 ? kept : palette;
	}

	/// <summary>
	/// What entity-like auto colouring (flowchart / state clusters, ER entities, class boxes, subgraphs) draws from: the
	/// <see cref="Default"/> role colour first (so the first cluster is always the default box colour), then the data palette without
	/// role hues and without a near-duplicate of the default. A cluster therefore never looks like a verdict.
	/// </summary>
	internal string[] AutoPalette()
	{
		var free = ReservedFreePalette();
		var def = RoleColor(ColorRole.Default);
		if (!ColorUtils.TryHueSaturation(def, out var defHue, out var defSat) || defSat <= 0.12)
			return free.Length > 0 && free[0] == def ? free : [def, .. free.Where(c => c != def)];

		var rest = free.Where(c =>
			c != def
			&& (!ColorUtils.TryHueSaturation(c, out var hue, out var sat)
				|| sat <= 0.12
				|| ColorUtils.HueDistance(hue, defHue) > RoleHueWindow)).ToArray();
		return [def, .. rest];
	}

	/// <summary>How close (degrees of hue) a palette entry may be to a role colour before auto colouring skips it.</summary>
	internal const double RoleHueWindow = 15;

	/// <summary>Colour <paramref name="i"/> of <see cref="AutoPalette"/>, wrapping around.</summary>
	internal string AutoPaletteAt(int i)
	{
		var palette = AutoPalette();
		return palette[i % palette.Length];
	}
}

/// <summary>Semantic colour roles; diagram source can opt in with the class names <c>success</c>, <c>failure</c>, <c>warning</c>, <c>info</c>.</summary>
internal enum ColorRole
{
	Default,
	Success,
	Failure,
	Warning,
	Info,
}
