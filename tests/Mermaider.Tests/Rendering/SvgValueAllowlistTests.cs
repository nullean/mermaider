using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

public class SvgValueAllowlistTests
{
	[Test]
	public void Local_reference_allowlist_accepts_only_none_or_a_local_fragment_url()
	{
		string[] allowed =
		[
			"none",
			"url(#a)",
			"url(#_marker)",
			"url(#gradient-1)",
			"url(#a.b:c_d-9)",
		];
		string[] rejected =
		[
			"", " none", "none ", "url(#)", "url(#9marker)", "url( #marker)",
			"url(#marker )", "URL(#marker)", "url(https://attacker.invalid/x#marker)",
			"url(data:image/svg+xml;base64,AAAA)", "#marker", "url(#marker);display:none",
			"url(#marker\n)", "url(#marker))", new('a', 513),
		];

		foreach (var value in allowed)
			SvgValueAllowlist.IsAllowedLocalReference(value).Should().BeTrue($"'{value}' is in the local-reference grammar");
		foreach (var value in rejected)
			SvgValueAllowlist.IsAllowedLocalReference(value).Should().BeFalse($"'{value}' is outside the local-reference grammar");
	}

	[Test]
	public void Paint_allowlist_accepts_colors_and_local_fragment_urls()
	{
		string[] allowed =
		[
			"#fff", " transparent ", "currentColor", "rgb(1 2 3 / .5)",
			"var(--_node-fill)", "url(#gradient)", " url(#gradient) ",
		];
		string[] rejected =
		[
			"", " ", "url(https://attacker.invalid/x)", "url(data:image/png;base64,AAAA)",
			"var(--user-color)", "red;display:none", "red!important", "url(#gradient) red",
			"javascript:alert(1)", "expression(alert(1))", "\"/><script>alert(1)</script>",
			new('a', 513),
		];

		foreach (var value in allowed)
			SvgValueAllowlist.IsAllowedPaint(value).Should().BeTrue($"'{value}' is an allowed paint");
		foreach (var value in rejected)
			SvgValueAllowlist.IsAllowedPaint(value).Should().BeFalse($"'{value}' is not an allowed paint");
	}

	[Test]
	public void Color_allowlist_covers_each_explicit_color_form()
	{
		string[] allowed =
		[
			"#000", "#abcd", "#A1b2C3", "#01020304",
			"red", "transparent", "currentColor", "light-goldenrod-yellow",
			"rgb(0, 128, 255)", "RGB(0 128 255 / 50%)", "rgba(0,0,0,.5)",
			"hsl(120 100% 50%)", "hsla(120,100%,50%,.5)", "hwb(120 0% 0% / .5)",
			"lab(50% 20 -10)", "lch(50% 30 120)", "oklab(.5 .1 -.1)",
			"oklch(.5 .1 120)", "color(display-p3 1 0 0 / .5)",
			"var(--bg)", "var(--fg)", "var(--accent)", "var(--_text)",
			"var(--_accent-fill)",
			"color-mix(in srgb, #fff 0%, var(--bg))",
			"color-mix(in srgb, var(--accent, var(--fg)) 100%, var(--bg))",
		];

		foreach (var value in allowed)
			SvgValueAllowlist.IsAllowedColor(value).Should().BeTrue($"'{value}' is in an allowed color form");
	}

	[Test]
	public void Color_allowlist_rejects_every_other_syntax_surface()
	{
		string[] rejected =
		[
			"", " ", "#", "#12", "#12345", "#1234567", "#123456789", "#ggg",
			"-moz-binding", "--custom", "red blue", "red!important", "red;fill:blue",
			"url(#paint)", "url(https://attacker.invalid/x)", "var(--custom)",
			"var(--fg, red)", "rgb()", "rgb(var(--fg))", "rgb(1 2 3);display:none",
			"rgb(1 2 3\\29 ;display:none)", "expression(alert(1))", "javascript:alert(1)",
			"color-mix(in srgb, red 50%, var(--bg));display:none",
			"\"/><script>alert(1)</script>", new('a', 513),
		];

		foreach (var value in rejected)
			SvgValueAllowlist.IsAllowedColor(value).Should().BeFalse($"'{value}' is outside every allowed color form");
	}

	[Test]
	public void Hex_color_allowlist_accepts_only_three_four_six_or_eight_digits()
	{
		string[] allowed = ["#000", "#AbCd", "#012345", "#01234567", " #fff "];
		string[] rejected = ["", "000", "#0", "#00", "#00000", "#0000000", "#000000000", "#xyz", "#fff;", new('f', 10_000)];

		foreach (var value in allowed)
			SvgValueAllowlist.IsAllowedHexColor(value).Should().BeTrue($"'{value}' has an allowed hexadecimal width");
		foreach (var value in rejected)
			SvgValueAllowlist.IsAllowedHexColor(value).Should().BeFalse($"'{value}' does not have an allowed hexadecimal width");
	}

	[Test]
	public void Color_mix_allowlist_accepts_only_the_renderer_shape_and_bounded_percentage()
	{
		string[] allowed =
		[
			"color-mix(in srgb, red 0%, var(--bg))",
			"color-mix(in srgb, #01020304 .5%, var(--bg))",
			"color-mix(in srgb, rgb(1 2 3 / .5) 50.25%, var(--bg))",
			"color-mix(in srgb, var(--fg) 100.000%, var(--bg))",
			"color-mix(in srgb, var(--accent, var(--fg)) 8%, var(--bg))",
		];
		string[] rejected =
		[
			"", "color-mix(in srgb, red 50%, white)", "color-mix(in hsl, red 50%, var(--bg))",
			"Color-mix(in srgb, red 50%, var(--bg))", "color-mix(in srgb, red -1%, var(--bg))",
			"color-mix(in srgb, red +1%, var(--bg))", "color-mix(in srgb, red 1e2%, var(--bg))",
			"color-mix(in srgb, red NaN%, var(--bg))", "color-mix(in srgb, red Infinity%, var(--bg))",
			"color-mix(in srgb, red 100.001%, var(--bg))", "color-mix(in srgb, red 50, var(--bg))",
			"color-mix(in srgb, var(--custom) 50%, var(--bg))",
			"color-mix(in srgb, url(#paint) 50%, var(--bg))",
			"color-mix(in srgb, red 50%, var(--bg));display:none",
			" color-mix(in srgb, red 50%, var(--bg))", new('a', 513),
		];

		foreach (var value in allowed)
			SvgValueAllowlist.IsAllowedColorMix(value).Should().BeTrue($"'{value}' matches the generated color-mix form");
		foreach (var value in rejected)
			SvgValueAllowlist.IsAllowedColorMix(value).Should().BeFalse($"'{value}' is outside the generated color-mix form");
	}

	[Test]
	public void Root_style_allowlist_accepts_only_theme_variables_and_the_fixed_background()
	{
		string[] allowed =
		[
			"--bg:#fff;--fg:#111",
			" --bg: #fff ; --fg: rgb(1 2 3) ; ",
			"--bg:var(--bg);--fg:var(--fg);--line:red;--accent:#1234;--muted:hsl(1 2% 3%);--surface:transparent;--border:currentColor",
			"--accent:color-mix(in srgb, var(--fg) 50%, var(--bg));background:var(--bg)",
			"background:var(--bg)",
		];
		string[] rejected =
		[
			"", ";", "color:red", "--unknown:red", "--BG:red", "background:red",
			"background:url(https://attacker.invalid/x)", "--bg:", ":red", "--bg:red!important",
			"--bg:red;display:none", "--bg:url(#paint)", "--bg:var(--custom)",
			"--bg:red:blue", "--bg:red/*x*/", "--bg:red\n;position:fixed", new('a', 4_097),
		];

		foreach (var value in allowed)
			SvgValueAllowlist.IsAllowedRootStyle(value).Should().BeTrue($"'{value}' contains only allowed root declarations");
		foreach (var value in rejected)
			SvgValueAllowlist.IsAllowedRootStyle(value).Should().BeFalse($"'{value}' contains an unapproved root declaration");
	}
}
