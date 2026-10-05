using System.Globalization;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Theming;

namespace Mermaider.Tests.Rendering;

/// <summary>
/// The chart half of the design system: one palette rule (solid mark = series base, region = 20% area + series outline,
/// grid soft, axis line 1.25), the per-preset chart marks (solid / outline / fat), left-aligned titles, and the per-type
/// rules from the design (accent invest quadrant, treemap value ramp + hottest tile, venn labels that never collide).
/// </summary>
public partial class ChartsDesignTests
{
	private static string Series(int i) => Themes.Default.PaletteAt(i);

	private static ColorFamily SeriesFamily(int i) => new("d", Series(i));

	private const string Pie = "pie\n  title Pets\n  \"Dogs\" : 386\n  \"Cats\" : 85\n  \"Rats\" : 15";
	private const string Xy = "xychart-beta\n  title \"Sales\"\n  x-axis [a, b, c]\n  y-axis \"Rev\" 0 --> 10\n  bar [2, 5, 8]\n  line [2, 5, 8]";
	private const string Quadrant = "quadrantChart\n  title Skills\n  quadrant-1 Invest\n  quadrant-2 Maintain\n  quadrant-3 Drop\n  quadrant-4 Phase out\n  Rust: [0.3, 0.7]";
	private const string Radar = "radar-beta\n  title Compare\n  axis A, B, C\n  curve c1[\"One\"]{1, 2, 3}\n  max 5";
	private const string Sankey = "sankey-beta\nA,B,10\nB,C,6";
	private const string Treemap = "treemap-beta\n  \"Technology\"\n    \"Frontend\": 30\n    \"Backend\": 40\n    \"DevOps\": 15\n  \"Business\"\n    \"Sales\": 25\n    \"Marketing\": 20";
	private const string Venn = "venn-beta\n  set A[\"Design\"]\n  set B[\"Engineering\"]\n  set C[\"Product\"]\n  union A, B[\"Design Systems\"]\n  union B, C[\"Technical PM\"]\n  union A, C[\"UX Research\"]";

	[Test]
	[Arguments(Pie)]
	[Arguments(Xy)]
	[Arguments(Quadrant)]
	[Arguments(Radar)]
	public void Chart_titles_are_left_aligned_with_the_preset_mark(string source)
	{
		foreach (var style in Enum.GetValues<DiagramStyle>())
		{
			var svg = DesignContract.Render(source, style);
			svg.Should().MatchRegex("class=\"diagram-title\">[\\s\\S]*?text-anchor=\"start\"", $"{style} titles start at the left padding");
		}
	}

	[Test]
	public void Pie_slices_are_solid_series_with_page_separators_and_labels_outside()
	{
		var svg = DesignContract.Render(Pie);

		svg.Should().Contain($"fill=\"{Series(0)}\" stroke=\"var(--bg)\" stroke-width=\"2.5\"");
		svg.Should().Contain("stroke=\"var(--_line)\" stroke-width=\"1.25\" stroke-linecap=\"round\"", "outside labels hang on leader lines");
		svg.Should().Contain(">79.42%</text>");
		svg.Should().MatchRegex("font-size=\"var\\(--fs-xs\\)\" font-weight=\"400\" fill=\"var\\(--_text-muted\\)\" dy=\"[\\d.]+\">79.42%</text>", "the legend value is meta text");
	}

	[Test]
	public void Pie_outline_preset_is_a_wireframe_and_fat_preset_is_a_donut_with_a_centre_figure()
	{
		var outline = DesignContract.Render(Pie, DiagramStyle.Blueprint);
		outline.Should().Contain($"fill=\"{SeriesFamily(0).Mix(20)}\" stroke=\"{Series(0)}\"");

		var fat = DesignContract.Render(Pie, DiagramStyle.Tonal);
		fat.Should().Contain(">79%</text>", "the donut centre carries the largest share");
		fat.Should().MatchRegex(@"<path d=""M[^""]+ A 128 128 [^""]+ L [^""]+ A [\d.]+ [\d.]+ 0 [01] 0 ", "slices are ring segments");
	}

	[Test]
	public void Xy_bars_are_gradient_series_marks_on_a_soft_grid_with_one_axis_line()
	{
		var svg = DesignContract.Render(Xy);

		svg.Should().Contain($"stop-color=\"{Series(0)}\"", "Quiet bars use the series gradient");
		svg.Should().Contain("stroke=\"var(--_line-soft)\" stroke-width=\"1\"");
		svg.Should().Contain("stroke=\"var(--_line)\" stroke-width=\"1.25\"");
		svg.Should().Contain($"stroke=\"{Series(1)}\" stroke-width=\"2\"", "the line series takes the next series colour");

		var flat = DesignContract.Render(Xy, gradient: false);
		flat.Should().NotContain("linearGradient");
		flat.Should().Contain($"fill=\"{Series(0)}\"");
	}

	[Test]
	public void Xy_marks_follow_the_preset()
	{
		DesignContract.Render(Xy, DiagramStyle.Blueprint).Should()
			.Contain($"fill=\"{SeriesFamily(0).Mix(20)}\" stroke=\"{Series(0)}\" stroke-width=\"1.25\"", "outline bars: area tint + series stroke")
			.And.Contain("<rect x=", "square points / bars");
		DesignContract.Render(Xy, DiagramStyle.Tonal).Should()
			.Contain($"stroke=\"{Series(1)}\" stroke-width=\"3\"", "fat smooth line")
			.And.Contain(" C ", "the fat line is a smooth curve");
	}

	[Test]
	public void Quadrant_invest_cell_is_the_accent_and_the_rest_are_neutral_panels()
	{
		var svg = DesignContract.Render(Quadrant);
		var accent = ColorFamily.Accent();

		Group(svg, "data-quadrant=\"1\"").Should().Contain($"fill=\"{accent.Tint()}\" stroke=\"{accent.Edge}\"");
		foreach (var q in new[] { "2", "3", "4" })
			Group(svg, $"data-quadrant=\"{q}\"").Should().Contain("fill=\"var(--_group-fill)\"");

		svg.Should().Contain("letter-spacing=\"0.08em\"", "quadrant names are eyebrows");
		svg.Should().Contain(">INVEST</text>");
		svg.Should().Contain("data-label=\"Invest\"", "the author's text is kept as data");
		svg.Should().Contain("fill=\"var(--accent, var(--fg))\" stroke=\"var(--bg)\"", "points are accent markers");
	}

	[Test]
	public void Radar_curves_are_area_regions_with_series_outlines_on_a_soft_graticule()
	{
		var svg = DesignContract.Render(Radar);

		svg.Should().Contain($"fill=\"{Series(0)}\" fill-opacity=\"0.2\" stroke=\"{Series(0)}\"");
		svg.Should().Contain("<circle cx=\"").And.Contain("fill=\"none\" stroke=\"var(--_line-soft)\"");
	}

	[Test]
	[Arguments(DiagramStyle.Quiet, "0.4")]
	[Arguments(DiagramStyle.Blueprint, "0.22")]
	[Arguments(DiagramStyle.Tonal, "0.55")]
	public void Sankey_ribbons_blend_source_to_target(DiagramStyle style, string opacity)
	{
		var svg = DesignContract.Render(Sankey, style);

		svg.Should().MatchRegex($"<stop offset=\"0\" stop-color=\"{Regex.Escape(Series(0))}\" stop-opacity=\"{Regex.Escape(opacity)}\" /><stop offset=\"1\" stop-color=\"{Regex.Escape(Series(1))}\"");

		var flat = DesignContract.Render(Sankey, style, gradient: false);
		flat.Should().NotContain("linearGradient");
		flat.Should().Contain($"fill-opacity=\"{opacity}\"");
	}

	[Test]
	public void Sankey_labels_sit_outside_the_node_bars_with_a_value_line()
	{
		var svg = DesignContract.Render(Sankey);

		svg.Should().Contain("paint-order=\"stroke\"", "labels are haloed over the ribbons");
		svg.Should().MatchRegex(">A</text>\\s*<text [^>]*fill=\"var\\(--_text-muted\\)\"[^>]*>10</text>");
	}

	[Test]
	public void Treemap_tiles_ramp_by_value_and_the_hottest_tile_gets_the_accent_dot()
	{
		var svg = DesignContract.Render(Treemap);

		// 10% + 26% × v / vmax of the section colour
		Group(svg, "data-label=\"Backend\"").Should().Contain($"fill=\"{SeriesFamily(0).Mix(36)}\"");
		Group(svg, "data-label=\"DevOps\"").Should().Contain($"fill=\"{SeriesFamily(0).Mix(10 + (26 * 15 / 40.0))}\"");
		Group(svg, "data-label=\"Sales\"").Should().Contain($"fill=\"{SeriesFamily(1).Mix(10 + (26 * 25 / 40.0))}\"");

		Group(svg, "data-label=\"Backend\"").Should().Contain("fill=\"var(--accent, var(--fg))\"", "the largest tile carries the accent dot");
		Group(svg, "data-label=\"Frontend\"").Should().NotContain("var(--accent");

		svg.Should().Contain(">40</text>").And.Contain(">31%</text>", "big numeral + share");
		svg.Should().Contain(">85</text>", "the section header carries the section total");
	}

	[Test]
	public void Treemap_child_tiles_start_below_the_section_header()
	{
		foreach (var style in Enum.GetValues<DiagramStyle>())
		{
			var svg = DesignContract.Render(Treemap, style);
			var section = MyRegex().Match(svg);
			section.Success.Should().BeTrue();
			var sectionY = double.Parse(section.Groups[1].Value, CultureInfo.InvariantCulture);
			foreach (Match tile in Regex.Matches(svg, "class=\"treemap-tile\"[^>]*>\\s*<rect x=\"[^\"]+\" y=\"([^\"]+)\""))
			{
				var y = double.Parse(tile.Groups[1].Value, CultureInfo.InvariantCulture);
				(y - sectionY).Should().BeGreaterThanOrEqualTo(36, $"{style}: tiles never overlap the section name / total");
			}
		}
	}

	[Test]
	public void Venn_sets_are_area_regions_and_pair_labels_never_overlap()
	{
		var svg = DesignContract.Render(Venn);

		svg.Should().Contain($"fill=\"{Series(0)}\" fill-opacity=\"0.2\" stroke=\"{Series(0)}\"");
		svg.Should().Contain(">UX</tspan>").And.Contain(">Research</tspan>", "long intersection labels wrap to two lines");

		var ux = TextX(svg, "UX");
		var ds = TextX(svg, "Design");
		var halfWidths = (Mermaider.Text.TextMetrics.MeasureTextWidth("Research", 14, 500) / 2) + (Mermaider.Text.TextMetrics.MeasureTextWidth("Systems", 14, 500) / 2);
		Math.Abs(ds.X - ux.X).Should().BeGreaterThan(halfWidths, "the two upper pair labels sit side by side without colliding");
	}

	private static (double X, double Y) TextX(string svg, string firstLine)
	{
		var m = Regex.Match(svg, "<text x=\"([^\"]+)\" y=\"([^\"]+)\"[^>]*><tspan[^>]*>" + Regex.Escape(firstLine) + "</tspan>");
		m.Success.Should().BeTrue($"'{firstLine}' is a two-line label");
		return (double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
	}

	private static string Group(string svg, string marker)
	{
		var start = svg.IndexOf(marker, StringComparison.Ordinal);
		start.Should().BeGreaterThan(-1, $"'{marker}' is rendered");
		var end = svg.IndexOf("</g>", start, StringComparison.Ordinal);
		return svg[start..end];
	}

	[GeneratedRegex("class=\"treemap-section\" data-label=\"Technology\">\\s*<rect x=\"[^\"]+\" y=\"([^\"]+)\"")]
	private static partial Regex MyRegex();
}
