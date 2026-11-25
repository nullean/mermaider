using System.Text.RegularExpressions;
using System.Xml.Linq;
using AwesomeAssertions;
using Mermaider.Animate.Score;

namespace Mermaider.Animate.Tests;

/// <summary>One diagram, several endings: scenarios share a beginning, and the player chooses between them.</summary>
public class ScenarioTests
{
	private const string Flow = """
		flowchart LR
		  client --> lb
		  lb --> api1
		  lb --> api2
		""";

	private const string Endings = Flow +
		"""

		animate
		  build
		  step "A request arrives"
		    send client -> lb

		  scenario "Happy path"
		    step "API 1 answers"
		      send lb -> api1

		  scenario "API 1 is down"
		    step "API 1 fails"
		      mark api1 fail
		    step "API 2 takes it"
		      send lb -> api2
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

	[Test]
	public void Steps_before_the_first_scenario_are_shared_and_each_scenario_keeps_its_own()
	{
		var lines = Endings.Split('\n');
		var diagnostics = new List<AnimationDiagnostic>();
		var score = ScoreParser.Parse(lines, ScoreParser.FindStart(lines), diagnostics);
		diagnostics.Should().BeEmpty();
		score.Steps.Select(s => s.Caption).Should().Equal(null, "A request arrives");
		score.Scenarios.Select(s => s.Name).Should().Equal("Happy path", "API 1 is down");
		score
			.Scenarios
			.Select(s => s.Steps.Select(x => x.Caption))
			.Should()
			.BeEquivalentTo([["API 1 answers"], new[] { "API 1 fails", "API 2 takes it" }], o => o.WithStrictOrdering());
		score.For(score.Scenarios[1]).Steps.Select(s => s.Caption).Should().Equal(null, "A request arrives", "API 1 fails", "API 2 takes it");
	}

	[Test]
	[Arguments("  scenario\n    step \"x\"\n      send client -> lb\n  scenario \"b\"\n    step \"y\"\n      send client -> lb\n", "A scenario line takes one quoted name")]
	[Arguments("  scenario \"a\"\n    step \"x\"\n      send client -> lb\n  scenario \"a\"\n    step \"y\"\n      send client -> lb\n", "There is already a scenario called \"a\".")]
	[Arguments("  scenario \"a\"\n  scenario \"b\"\n    step \"y\"\n      send client -> lb\n", "The scenario \"a\" has no steps.")]
	[Arguments("  scenario \"a\"\n    step \"y\"\n      send client -> lb\n", "A score with one scenario tells one story")]
	public void A_malformed_scenario_says_what_is_wrong(string score, string message) =>
		Problems(Flow + "\nanimate\n" + score).Should().ContainSingle().Which.Message.Should().StartWith(message);

	[Test]
	public void Each_ending_is_its_own_animation_and_the_first_is_the_svg()
	{
		var animation = MermaidAnimator.Compile(Endings);
		animation.Scenarios.Select(s => s.Name).Should().Equal("Happy path", "API 1 is down");
		animation.Scenarios.Select(s => s.Animation.Steps.Count(x => x.Caption is not null)).Should().Equal(2, 3);
		animation.Scenarios[0].Animation.Should().BeSameAs(animation, "the animation is its first ending");
		animation.Scenario.Should().Be("Happy path");
		MermaidAnimator.RenderSvg(Endings).Should().Be(animation.Svg);

		// each ending has its own prefix, so all of them can share a page
		var prefixes = animation.Scenarios.Select(s => Regex.Match(s.Animation.Svg, "class=\"(ma[0-9a-f]{8})-root\"").Groups[1].Value).ToList();
		prefixes.Should().OnlyHaveUniqueItems().And.NotContain("");
	}

	[Test]
	public void A_problem_in_the_shared_beginning_is_reported_once()
	{
		var source = Endings.Replace("send client -> lb", "send client -> nowhere");
		var line = Array.FindIndex(source.Split('\n'), l => l.Contains("nowhere", StringComparison.Ordinal)) + 1;
		Problems(source).Should().ContainSingle("both endings compile the shared step").Which.Line.Should().Be(line);
	}

	[Test]
	public void Each_ending_tells_its_own_name_and_steps_to_a_screen_reader()
	{
		var animation = MermaidAnimator.Compile(Endings);
		XNamespace svg = "http://www.w3.org/2000/svg";
		animation
			.Scenarios
			.Select(s => XDocument.Parse(s.Animation.Svg).Root!.Element(svg + "desc")!.Value)
			.Should()
			.Equal(
				"Happy path. In 2 steps: 1. A request arrives. 2. API 1 answers.",
				"API 1 is down. In 3 steps: 1. A request arrives. 2. API 1 fails. 3. API 2 takes it."
			);
	}

	[Test]
	public void The_player_offers_every_ending_behind_one_row_of_tabs()
	{
		var html = MermaidAnimator.RenderPlayer(Endings);
		var radios = Regex.Matches(html, "<input type=\"radio\" name=\"(ma[0-9a-f]{8}-s)\" id=\"([^\"]+)\"( checked)?>").ToList();
		radios.Should().HaveCount(2, "one tab per ending");
		radios.Where(r => r.Groups[3].Success).Should().ContainSingle().Which.Groups[2].Value.Should().EndWith("-s-0");
		Regex.Matches(html, @"<label for=""ma[0-9a-f]{8}-s-\d"">([^<]+)</label>").Select(m => m.Groups[1].Value).Should().Equal("Happy path", "API 1 is down");

		// each ending's own player, once, in its panel; a panel shows only while its tab is chosen
		var s = radios[0].Groups[1].Value;
		html.Should().Contain($".{s} > .{s}-end {{ display: none; }}");
		html.Should().Contain($".{s}:has(#{s}-1:checked) > .{s}-end-1 {{ display: block; }}");
		Regex.Matches(html, "<figure class=\"ma[0-9a-f]{8}-p\"").Count.Should().Be(2);
		html.Should().NotContain("<script");
	}

	[Test]
	public void A_score_without_scenarios_has_none_and_its_player_has_no_tabs()
	{
		var plain = MermaidAnimator.Compile(Flow + "\nanimate\n  step \"x\"\n    send client -> lb\n");
		plain.Scenarios.Should().BeEmpty();
		plain.Scenario.Should().BeNull();
		plain.RenderPlayer().Should().NotContain("-s-0");
	}
}
