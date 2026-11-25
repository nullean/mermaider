using System.Text.RegularExpressions;
using System.Xml.Linq;
using AwesomeAssertions;

namespace Mermaider.Animate.Tests;

/// <summary>Bars, lines, slices, curves and flows.</summary>
public partial class ChartTests
{
	private const string Bars =
		"""
		xychart-beta
		  x-axis [A, B, C]
		  y-axis 0 --> 100
		  bar [20, 40, 80]
		  line [10, 50, 70]
		""";

	[GeneratedRegex(@"scale\(([\d.]+)(?:,([\d.]+))?\)")]
	private static partial Regex Scale();

	[GeneratedRegex("(^|;)opacity:0(;|$)")]
	private static partial Regex Hidden();

	[GeneratedRegex(@"(^|;)opacity:0\.22(;|$)")]
	private static partial Regex Dimmed();

	private static double ScaleY(XElement bar)
	{
		var style = bar.AncestorsAndSelf().Select(a => (string?)a.Attribute("style") ?? "").First(s => s.Contains("scale(", StringComparison.Ordinal));
		var m = Scale().Match(style);
		return double.Parse(m.Groups[2].Success ? m.Groups[2].Value : m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
	}

	private static XElement Bar(XDocument doc, int index) =>
		doc.Descendants().Single(
			e => ((string?)e.Attribute("class") ?? "").Contains("xy-bar") && (string?)e.Attribute("data-index") == index.ToString(
				System.Globalization.CultureInfo.InvariantCulture
			)
		);

	[Test]
	public void Bars_grow_from_nothing_and_set_scales_one_to_its_new_value()
	{
		var animation = MermaidAnimator.Compile(Bars + "\nanimate once\n  build\n  step \"up\"\n    set #2 60\n");
		ScaleY(Bar(XDocument.Parse(animation.RenderFrame(TimeSpan.Zero)), 1)).Should().Be(0);
		var end = XDocument.Parse(animation.RenderFrame(animation.Duration));
		ScaleY(Bar(end, 1)).Should().Be(1);
		ScaleY(Bar(end, 2)).Should().BeApproximately(1.5, 0.001, "60 is one and a half times 40");
		end.Descendants().Where(e => e.Name.LocalName == "text").Select(e => e.Value).Should().Contain("60");
	}

	[Test]
	public void A_line_draws_and_its_points_land_as_it_reaches_them()
	{
		var animation = MermaidAnimator.Compile(Bars + "\nanimate once\n");
		var points = XDocument
			.Parse(animation.RenderFrame(TimeSpan.Zero))
			.Descendants()
			.Where(e => e.Name.LocalName == "circle" && e.Parent?.Parent is { } g && ((string?)g.Attribute("class") ?? "").Contains("xy-line"))
			.ToList();
		points.Should().HaveCount(3).And.OnlyContain(p => p.AncestorsAndSelf().Any(a => Hidden().IsMatch((string?)a.Attribute("style") ?? "")));
	}

	[Test]
	public void Set_belongs_to_xy_charts()
	{
		var act = () => MermaidAnimator.Compile("pie\n  \"A\" : 1\n  \"B\" : 2\n\nanimate\n  set #1 5\n");
		act.Should().Throw<AnimationException>().Which.Diagnostics.Single().Message.Should().Contain("xy chart");
	}

	[Test]
	public void Pie_slices_sweep_in_order_with_their_percentages()
	{
		var animation = MermaidAnimator.Compile("pie\n  \"A\" : 50\n  \"B\" : 30\n  \"C\" : 20\n\nanimate once\n");

		double Revealed(TimeSpan t, string label)
		{
			var doc = XDocument.Parse(animation.RenderFrame(t));
			var slice = doc.Descendants().Single(
				e => (string?)e.Attribute("data-label") == label && ((string?)e.Attribute("class") ?? "").Contains("pie-slice")
			);
			var mask = ((string)slice.Attribute("mask")!)[5..^1];
			var path = doc.Descendants().Single(e => (string?)e.Attribute("id") == mask).Elements().Single();
			return 1 - double.Parse(
				Regex.Match((string?)path.Attribute("style") ?? "stroke-dashoffset:0", @"stroke-dashoffset:(-?[\d.]+)").Groups[1].Value,
				System.Globalization.CultureInfo.InvariantCulture
			);
		}

		Revealed(TimeSpan.Zero, "A").Should().Be(0);
		var end = animation.ScoreDuration;
		new[] { "A", "B", "C" }.Select(l => Revealed(end, l)).Should().OnlyContain(v => Math.Abs(v - 1) < 0.01);
		// A finishes before C starts
		var halfway = TimeSpan.FromMilliseconds(animation.ScoreDuration.TotalMilliseconds * 0.35);
		Revealed(halfway, "A").Should().BeGreaterThan(Revealed(halfway, "C"));
	}

	[Test]
	public void Radar_curves_grow_out_of_the_centre()
	{
		var animation = MermaidAnimator.Compile("radar-beta\n  axis a[\"A\"], b[\"B\"], c[\"C\"]\n  curve x[\"X\"]{3, 4, 2}\n\nanimate once\n");
		var curve = XDocument.Parse(animation.RenderFrame(TimeSpan.Zero)).Descendants().Single(e => (string?)e.Attribute("data-curve") == "x");
		ScaleY(curve).Should().Be(0);
	}

	[Test]
	public void Trace_lights_every_flow_between_two_nodes_and_dims_the_rest()
	{
		const string flows = """
			sankey-beta
			  A,B,10
			  A,C,5
			  B,D,10
			  C,E,5
			""";
		var animation = MermaidAnimator.Compile(flows + "\nanimate once\n  step \"t\"\n    trace A -> D\n");
		var frame = XDocument.Parse(animation.RenderFrame(animation.Steps[0].End - TimeSpan.FromMilliseconds(100)));

		bool IsDimmed(string id) =>
			frame
				.Descendants()
				.Single(e => (string?)e.Attribute("data-id") == id)
				.AncestorsAndSelf()
				.Any(a => Dimmed().IsMatch((string?)a.Attribute("style") ?? ""));

		IsDimmed("A").Should().BeFalse();
		IsDimmed("B").Should().BeFalse();
		IsDimmed("D").Should().BeFalse();
		IsDimmed("C").Should().BeTrue();
		IsDimmed("E").Should().BeTrue();
		var act = () => MermaidAnimator.Compile(flows + "\nanimate\n  trace D -> A\n");
		act.Should().Throw<AnimationException>().Which.Diagnostics.Single().Message.Should().Be("Nothing flows from D to A.");
	}
}
