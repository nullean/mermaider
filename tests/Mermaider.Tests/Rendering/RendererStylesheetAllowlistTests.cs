using System.Text;
using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Rendering;
using Mermaider.Theming;

namespace Mermaider.Tests.Rendering;

public class RendererStylesheetAllowlistTests
{
	[Test]
	public void Accepts_each_stylesheet_shape_the_renderer_can_generate()
	{
		var strict = new StrictStylingOptions
		{
			AllowedClasses =
			[
				new DiagramClass
				{
					Name = "approved_class-1",
					Fill = "#123",
					Stroke = "#4567",
					Color = "#89ABCD",
					DarkFill = "#01020304",
					DarkStroke = "#AABBCC",
					DarkColor = "#fff",
				},
			],
		};

		var stylesheets = new[]
		{
			EmitStylesheet(),
			EmitStylesheet(font: "system-ui", monoFont: "monospace"),
			EmitStylesheet(font: "Inter Display", monoFont: "JetBrains Mono"),
			EmitStylesheet(font: "A'B\n{}\\💥", monoFont: "M;/*x*/", fontScale: FontScale.From(new RenderOptions
			{
				FontSize = "12.5px",
				FontSizeExtraSmall = 0.5,
				FontSizeSmall = 0.75,
				FontSizeLarge = 2,
			})),
			EmitStylesheet(strict: strict),
		};

		foreach (var stylesheet in stylesheets)
			RendererStylesheetAllowlist.IsAllowed(stylesheet).Should().BeTrue("the stylesheet came from the renderer's normalized emitter");
	}

	[Test]
	public void Rejects_mutations_of_every_fixed_stylesheet_section()
	{
		var valid = EmitStylesheet();
		var mutations = new[]
		{
			valid.Replace("  text {", "  body {", StringComparison.Ordinal),
			valid.Replace("font-family: system-ui", "font-family: url(https://attacker.invalid/font)", StringComparison.Ordinal),
			valid.Replace("  svg {", "  svg, body {", StringComparison.Ordinal),
			valid.Replace("--_text:          var(--fg);", "--_text:          red;", StringComparison.Ordinal),
			valid.Replace("--_line:", "--_evil:", StringComparison.Ordinal),
			valid.Replace("--fs-xs: 0.75rem;", "--fs-xs: calc(1rem + 1px);", StringComparison.Ordinal),
			valid.Replace("--fs-s:  0.875rem;", "--fs-s:  -1rem;", StringComparison.Ordinal),
			valid.Replace("drop-shadow(0 1px 3px rgba(0,0,0,.07))", "url(https://attacker.invalid/filter)", StringComparison.Ordinal),
			valid.Replace(".subgraph, .kanban-column", ".subgraph, body", StringComparison.Ordinal),
			valid.Replace("\n", "\r\n", StringComparison.Ordinal),
			valid + "  body { display: none; }\n",
			valid[..valid.LastIndexOf('\n')],
		};

		foreach (var stylesheet in mutations)
			RendererStylesheetAllowlist.IsAllowed(stylesheet).Should().BeFalse("a fixed renderer stylesheet line was changed");
	}

	[Test]
	public void Accepts_every_generic_font_keyword_the_emitter_can_output()
	{
		string[] keywords =
		[
			"serif", "sans-serif", "monospace", "cursive", "fantasy", "system-ui",
			"ui-serif", "ui-sans-serif", "ui-monospace", "ui-rounded", "emoji", "math", "fangsong",
			"SANS-SERIF",
		];

		foreach (var keyword in keywords)
		{
			var stylesheet = EmitStylesheet(font: keyword, monoFont: keyword);
			RendererStylesheetAllowlist.IsAllowed(stylesheet).Should().BeTrue($"'{keyword}' is emitted as an unquoted generic family");
		}
	}

	[Test]
	public void Strict_class_rules_require_safe_selectors_hex_colors_and_matching_dark_rules()
	{
		var valid = EmitStylesheet(strict: new StrictStylingOptions
		{
			AllowedClasses =
			[
				new DiagramClass { Name = "safe", Fill = "#123", Stroke = "#456", Color = "#789" },
			],
		});

		RendererStylesheetAllowlist.IsAllowed(valid).Should().BeTrue();

		var mutations = new[]
		{
			valid.Replace(".cls-safe rect", ".cls-safe:hover rect", StringComparison.Ordinal),
			valid.Replace(".cls-safe polygon", ".cls-other polygon", StringComparison.Ordinal),
			valid.Replace("fill: #123", "fill: red", StringComparison.Ordinal),
			valid.Replace("stroke: #456", "stroke: url(#paint)", StringComparison.Ordinal),
			valid.Replace("text { fill: #789; }", "text { fill: #789; display: none; }", StringComparison.Ordinal),
			valid.Replace("  @media (prefers-color-scheme: dark) {", "  @media all {", StringComparison.Ordinal),
			valid.Replace("    .cls-safe", "  .cls-safe", StringComparison.Ordinal),
			valid.Replace("\n  @media (prefers-color-scheme: dark) {", "", StringComparison.Ordinal),
		};

		foreach (var stylesheet in mutations)
			RendererStylesheetAllowlist.IsAllowed(stylesheet).Should().BeFalse("strict class CSS is a closed generated grammar");
	}

	[Test]
	public void Standalone_sanitizer_still_removes_an_otherwise_valid_renderer_stylesheet()
	{
		var stylesheet = EmitStylesheet();
		var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\"><style>{stylesheet}</style><rect/></svg>";

		var standalone = SvgSanitizer.Sanitize(svg);
		var rendererOutput = SvgSanitizer.SanitizeRendererOutput(svg);

		standalone.HasViolations.Should().BeTrue();
		standalone.Svg.Should().NotContain("<style");
		rendererOutput.HasViolations.Should().BeFalse();
	}

	private static string EmitStylesheet(
		string? font = null,
		string? monoFont = null,
		FontScale? fontScale = null,
		StrictStylingOptions? strict = null)
	{
		var sb = new StringBuilder();
		StyleBlock.AppendStyleBlock(sb, font, strict, fontScale, monoFont);
		var emitted = sb.ToString();
		var start = emitted.IndexOf("<style>", StringComparison.Ordinal) + "<style>".Length;
		var end = emitted.IndexOf("</style>", StringComparison.Ordinal);
		return emitted[start..end];
	}
}
