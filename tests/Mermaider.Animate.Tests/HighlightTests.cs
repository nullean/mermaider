using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AwesomeAssertions;
using Mermaider.Animate.Score;

namespace Mermaider.Animate.Tests;

/// <summary>Highlights: a standing emphasis for the whole animation, with or without steps.</summary>
public class HighlightTests
{
	private const string Flow =
		"""
		flowchart LR
		  client --> lb
		  lb --> api1
		  lb --> api2
		  api1 --> db
		  api2 --> db
		""";

	private static List<AnimationDiagnostic> Problems(string source)
	{
		try
		{
			_ = MermaidAnimator.Compile(source);
			return [];
		}
		catch (AnimationException e)
		{
			return [.. e.Diagnostics];
		}
	}

	private static string Style(string svg) => Regex.Match(svg, "<style>(.*?)</style>", RegexOptions.Singleline).Groups[1].Value;

	private static List<double> Keyframe(string svg, string cls, string property)
	{
		var name = Regex.Match(Style(svg), $@"(?m)^\.{cls} \{{ animation: ([^ ]+) ").Groups[1].Value;
		var body = Regex.Match(Style(svg), $@"@keyframes {name} \{{([^\n]*)\}}").Groups[1].Value;
		return [.. Regex.Matches(body, $@"{property}:(-?[\d.]+)").Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))];
	}

	[Test]
	public void Highlights_belong_to_the_score_not_to_a_step()
	{
		var lines = (Flow +
			"\nanimate\n  highlight api2 as fail\n  highlight path client -> lb -> api1 as ok\n  highlight flow lb -> api2\n  step \"x\"\n    pulse db\n").Split(
			'\n'
		);
		var diagnostics = new List<AnimationDiagnostic>();
		var score = ScoreParser.Parse(lines, ScoreParser.FindStart(lines), diagnostics);
		diagnostics.Should().BeEmpty();
		score
			.Highlights
			.Select(h => $"{h.Kind} {string.Join(",", h.Targets.Select(t => t.Id))}{string.Join("->", h.Route)} {h.Role}")
			.Should()
			.Equal("Node api2 Fail", "Path client->lb->api1 Ok", "Flow lb->api2 ");
		score.Steps.Should().ContainSingle("a highlight is no step of its own");

		Problems(Flow + "\nanimate\n  step \"x\"\n    highlight api1\n")
			.Should()
			.ContainSingle()
			.Which
			.Message
			.Should()
			.StartWith("highlight holds for the whole animation");
		Problems(Flow + "\nanimate\n  highlight path client -> nowhere\n").Should().ContainSingle();
		Problems(Flow + "\nanimate\n  highlight flow client -> db\n").Should().ContainSingle("there is no edge from client to db");
	}

	[Test]
	public void A_glow_breathes_in_whole_cycles_so_the_loop_has_no_seam()
	{
		var animation = MermaidAnimator.Compile(Flow + "\nanimate\n  highlight api2 as fail\n");
		animation.Steps.Should().BeEmpty("a score of highlights alone has no steps");
		var svg = animation.Svg;
		var halo = Regex.Match(svg, @"<rect [^>]*stroke-opacity=""0.22"" class=""(ma[0-9a-f]{8}-a\d+)""").Groups[1].Value;
		halo.Should().NotBeEmpty();
		var breath = Keyframe(svg, halo, "opacity");
		breath.Should().HaveCountGreaterThan(2).And.Contain(1);
		breath.First().Should().Be(breath.Last(), "the loop ends where it starts");

		// an even rhythm: the last breath ends with the loop, so from the last peak to the end is half a breath
		var name = Regex.Match(Style(svg), $@"(?m)^\.{halo} \{{ animation: ([^ ]+) ").Groups[1].Value;
		var body = Regex.Match(Style(svg), $@"@keyframes {name} \{{([^\n]*)\}}").Groups[1].Value;
		var peaks = Regex.Matches(body, @"([\d.]+)% \{ opacity:1[;\s]").Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
		var breaths = peaks.Zip(peaks.Skip(1), (x, y) => y - x).ToList();
		var cycle = breaths.Count > 0 ? breaths.Average() : peaks[0] * 2;
		(100 - peaks.Last()).Should().BeApproximately(cycle / 2, 0.5);
	}

	[Test]
	public void A_flow_moves_a_whole_number_of_its_dashes_per_loop()
	{
		var svg = MermaidAnimator.RenderSvg(Flow + "\nanimate\n  highlight flow client -> lb\n");
		var dashes = Regex.Match(svg, @"<path [^>]*stroke-dasharray=""([\d.]+) ([\d.]+)""[^>]*class=""(ma[0-9a-f]{8}-a\d+)""").Groups;
		dashes[3].Success.Should().BeTrue();
		var pattern = double.Parse(dashes[1].Value, CultureInfo.InvariantCulture) + double.Parse(dashes[2].Value, CultureInfo.InvariantCulture);
		var offsets = Keyframe(svg, dashes[3].Value, "stroke-dashoffset");
		offsets.Last().Should().BeNegative("it moves along the edge's direction");
		var turns = -offsets.Last() / pattern;
		turns.Should().BeApproximately(Math.Round(turns), 0.01);
	}

	[Test]
	public void A_lit_path_shows_without_moving_and_its_sheen_runs_the_route()
	{
		var svg = MermaidAnimator.RenderSvg(Flow + "\nanimate\n  highlight path client -> lb -> api1 as ok\n");
		var doc = XDocument.Parse(svg);
		var lit = doc
			.Descendants()
			.Where(e => e.Name.LocalName == "path" && (string?)e.Attribute("stroke-width") == "3.5" && e.Attribute("class") is null)
			.ToList();
		lit.Should().HaveCount(2, "one lit stroke per edge on the route");
		lit.Should().OnlyContain(e => e.Attribute("opacity") == null, "they are lit from the start");
		var sheen = doc.Descendants().Single(e => (string?)e.Attribute("stroke-dasharray") == "0.08 1.2");
		var offsets = Keyframe(svg, ((string)sheen.Attribute("class")!).Split(' ').Last(), "stroke-dashoffset");
		offsets.Should().Contain(-1, "the sheen runs to the end of the route");
	}

	[Test]
	public void Under_reduced_motion_a_highlight_holds_still()
	{
		var svg = MermaidAnimator.RenderSvg(Flow + "\nanimate\n  highlight api2 as fail\n  highlight flow client -> lb\n  highlight path lb -> api1\n");
		var reduced = Style(svg)[Style(svg).IndexOf("@media (prefers-reduced-motion: reduce)", StringComparison.Ordinal)..];
		var animated = Regex.Matches(Style(svg), @"(?m)^\.(ma[0-9a-f]{8}-a\d+) \{ animation:").Select(m => m.Groups[1].Value).ToList();
		animated.Should().NotBeEmpty();
		animated.Should().OnlyContain(cls => reduced.Contains($".{cls} {{ animation: none; }}"), "every highlight motion stops");
	}

	[Test]
	public void Highlights_run_beside_a_story_and_the_settle_leaves_them_be()
	{
		var animation = MermaidAnimator.Compile(
			Flow + "\nanimate\n  highlight api2 as fail\n  step \"A request\"\n    send client -> lb\n  step \"Another\"\n    send lb -> api1\n"
		);
		animation.Steps.Select(s => s.Caption).Should().Equal("A request", "Another");
		var svg = animation.Svg;
		var halo = Regex.Match(svg, @"<rect [^>]*stroke-opacity=""0.22"" class=""(ma[0-9a-f]{8}-a\d+)""").Groups[1].Value;
		var breath = Keyframe(svg, halo, "opacity");
		breath.First().Should().Be(breath.Last());
		breath.Count(v => v == 1).Should().BeGreaterThan(1, "it breathes the whole story long, not once");
	}

	private static XElement Pulled(string svg, string label) =>
		XDocument.Parse(svg).Descendants().Single(e => (string?)e.Attribute("data-label") == label && SvgClass(e).Contains("pie-slice"));

	private static string SvgClass(XElement e) => (string?)e.Attribute("class") ?? "";

	private static string? Moved(XElement slice) => (string?)slice.Parent?.Attribute("style") is { } style && style.Contains("translate(") ? style : null;

	[Test]
	public void A_bare_animate_highlights_the_type_instead_of_building_it()
	{
		var animation = MermaidAnimator.Compile(Flow + "\nanimate\n");
		animation.Steps.Should().BeEmpty("a bare animate on a flowchart is a highlight, not a story");

		// nothing waits to appear: the first frame already shows every node
		var first = XDocument.Parse(animation.RenderFrame(TimeSpan.Zero));
		first.Descendants().Where(e => SvgClass(e).Split(' ').Contains("node")).Should().HaveCount(5);
		first.Descendants().Should().NotContain(e => ((string?)e.Attribute("style") ?? "").Contains("opacity:0;"));

		// the longest path from where the flow starts runs: client -> lb -> api1 -> db, three edges of dashes
		var dashes = Regex.Matches(animation.Svg, @"stroke-dasharray=""9 [\d.]+""").Count;
		dashes.Should().Be(3);

		// a shortcut does not win: a -> d is found first, a -> b -> c -> d is longer
		var shortcut = MermaidAnimator.RenderSvg("flowchart LR\n  a --> d\n  a --> b\n  b --> c\n  c --> d\n\nanimate\n");
		Regex.Matches(shortcut, @"stroke-dasharray=""9 [\d.]+""").Count.Should().Be(3);

		foreach (var ordered in new[] { "sequenceDiagram\n  A->>B: hi\n\nanimate\n", "stateDiagram-v2\n  [*] --> A\n  A --> B\n\nanimate\n" })
			MermaidAnimator.Compile(ordered).Steps.Should().ContainSingle("where order is the content, a bare animate plays it");
	}

	[Test]
	public void Biggest_pulls_the_largest_slice_out_of_the_pie_and_lowest_the_smallest()
	{
		const string pie = "pie\n  \"A\" : 20\n  \"B\" : 50\n  \"C\" : 30\n\nanimate\n";
		var biggest = MermaidAnimator.Compile(pie);
		var frame = biggest.RenderFrame(biggest.Intro + TimeSpan.FromMilliseconds(600));
		Moved(Pulled(frame, "B")).Should().NotBeNull("a bare animate on a pie singles out its biggest slice");
		Moved(Pulled(frame, "A")).Should().BeNull();
		Moved(Pulled(frame, "C")).Should().BeNull();
		var peak = biggest.RenderFrame(biggest.Intro + TimeSpan.FromMilliseconds(1200));
		Moved(Pulled(peak, "B")).Should().NotBe(Moved(Pulled(frame, "B")), "out of the pie, it breathes further out and back");

		var lowest = MermaidAnimator.Compile(pie + "  highlight lowest\n");
		frame = lowest.RenderFrame(lowest.Intro + TimeSpan.FromMilliseconds(600));
		Moved(Pulled(frame, "A")).Should().NotBeNull();
		Moved(Pulled(frame, "B")).Should().BeNull("a written highlight replaces the type's default");
	}

	[Test]
	public void A_journey_glows_its_lowest_moment_as_a_failure()
	{
		var svg = MermaidAnimator.RenderSvg("journey\n  section S\n    Browse: 5: Me\n    Pay: 1: Me\n    Leave: 3: Me\n\nanimate\n");
		var halo = XDocument.Parse(svg).Descendants().Single(e => (string?)e.Attribute("stroke-opacity") == "0.22");
		var pay = XDocument
			.Parse(svg)
			.Descendants()
			.First(e => e.Name.LocalName == "text" && e.Value == "Pay")
			.Ancestors()
			.First(a => a.Attribute("data-score") != null);
		var box =
			(x: double.Parse((string)halo.Attribute("x")!, CultureInfo.InvariantCulture), w: double.Parse(
				(string)halo.Attribute("width")!,
				CultureInfo.InvariantCulture
			));
		var text = XDocument.Parse(svg).Descendants().First(e => e.Name.LocalName == "text" && e.Value == "Pay");
		var x = double.Parse((string)text.Attribute("x")!, CultureInfo.InvariantCulture);
		x.Should().BeInRange(box.x, box.x + box.w, "the halo is round Pay, the task scored 1");
		((string)halo.Attribute("stroke")!).Should().NotContain("accent", "it glows in the failure colour");
		pay.Should().NotBeNull();
	}

	[Test]
	public void A_sankey_runs_its_biggest_flow_inside_the_bands()
	{
		var svg = MermaidAnimator.RenderSvg("sankey-beta\nA,X,10\nB,X,30\nX,Y,25\nX,Z,15\n\nanimate\n");
		var doc = XDocument.Parse(svg);
		var pulses = doc.Descendants().Where(e => e.Attribute("clip-path") != null && e.Attribute("stroke-dasharray") != null).ToList();
		pulses.Should().HaveCount(2, "B -> X -> Y: from the source sending most, along the biggest link at each node");
		var clipped = pulses.Select(p => ((string)p.Attribute("clip-path")!)[5..^1]).Select(
			id => doc.Descendants().Single(e => (string?)e.Attribute("id") == id).Elements().Single().Attribute("d")!.Value
		);
		var bands = doc
			.Descendants()
			.Where(e => SvgClass(e) == "sankey-link")
			.ToDictionary(e => $"{e.Attribute("data-from")!.Value}{e.Attribute("data-to")!.Value}", e => e.Attribute("d")!.Value);
		clipped.Should().BeEquivalentTo([bands["BX"], bands["XY"]], "each pulse keeps to its band");
	}

	[Test]
	public void A_pick_on_a_diagram_without_data_says_where_it_works()
	{
		var problems = Problems(Flow + "\nanimate\n  highlight biggest\n");
		problems.Should().ContainSingle().Which.Message.Should().StartWith("highlight biggest picks by the diagram's data");
		Problems("journey\n  section S\n    Pay: 1: Me\n\nanimate\n  highlight biggest\n").Should().ContainSingle();
	}

	public static IEnumerable<string> Defaults() =>
		[
			Flow,
			"pie\n  \"A\" : 20\n  \"B\" : 50\n  \"C\" : 30\n",
			"journey\n  section S\n    Browse: 5: Me\n    Pay: 1: Me\n",
			"sankey-beta\nA,X,10\nB,X,30\nX,Y,25\n",
			"radar-beta\n  axis a[\"A\"], b[\"B\"], c[\"C\"]\n  curve x[\"X\"]{3, 4, 2}\n",
			"venn-beta\n  set A\n  set B\n  union A,B[\"Both\"]\n",
			"mindmap\n  root((R))\n    a\n    b\n",
			"xychart-beta\n  x-axis [a, b, c]\n  bar [3, 9, 4]\n",
			"packet-beta\n  0-15: \"One\"\n  16-63: \"Two\"\n",
		];

	/// <summary>
	/// A frame as the eye sees it: a dash offset counts within its dash pattern (a flow moved by whole patterns looks
	/// unmoved), and values are rounded, so one a hair from its end (the last frame is a moment before the loop restarts)
	/// compares equal.
	/// </summary>
	private static string Settled(string frame)
	{
		var doc = XDocument.Parse(frame);
		foreach (var e in doc.Descendants().Where(e => e.Attribute("stroke-dasharray") != null && e.Attribute("style") != null))
		{
			var pattern = ((string)e.Attribute("stroke-dasharray")!).Split(' ').Sum(v => double.Parse(v, CultureInfo.InvariantCulture));
			e.SetAttributeValue("style", Regex.Replace((string)e.Attribute("style")!, @"stroke-dashoffset:(-?[\d.]+)", m =>
			{
				var within = ((double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) % pattern) + pattern) % pattern;
				return $"stroke-dashoffset:{Num(Math.Min(within, pattern - within) < 0.5 ? 0 : within)}";
			}));
		}
		return Regex.Replace(doc.ToString(), @"-?\d+\.\d+", m => Num(double.Parse(m.Value, CultureInfo.InvariantCulture)));

		static string Num(double v) => Math.Round(v, 1).ToString(CultureInfo.InvariantCulture);
	}

	[Test]
	[MethodDataSource(nameof(Defaults))]
	public void A_default_highlight_loops_without_a_seam(string diagram)
	{
		var animation = MermaidAnimator.Compile(diagram + "\nanimate\n");
		animation.Steps.Should().BeEmpty();
		Settled(animation.RenderFrame(animation.Duration - TimeSpan.FromMilliseconds(1)))
			.Should()
			.Be(Settled(animation.RenderFrame(TimeSpan.Zero)), "the loop ends where it starts, so nothing jumps when it begins again");
	}
}
