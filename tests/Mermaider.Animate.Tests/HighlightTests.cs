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
}
