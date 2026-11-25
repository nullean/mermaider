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
	public void The_player_offers_every_ending_on_its_story_map()
	{
		var html = MermaidAnimator.RenderPlayer(Endings.Replace("scenario \"API 1 is down\"", "scenario \"API 1 is down\" as fail"));
		var radios = Regex.Matches(html, "<input type=\"radio\" name=\"(ma[0-9a-f]{8}-s)\" id=\"([^\"]+)\"( checked)?>").ToList();
		radios.Should().HaveCount(2, "one per ending");
		radios.Where(r => r.Groups[3].Success).Should().ContainSingle().Which.Groups[2].Value.Should().EndWith("-s-0");
		var s = radios[0].Groups[1].Value;

		// each ending is a unit, shown while its radio is checked
		html.Should().Contain($".{s} > .{s}-end {{ display: none; }}").And.Contain($".{s}:has(#{s}-1:checked) > .{s}-end-1 {{ display: block; }}");
		Regex.Matches(html, $"<div class=\"{s}-end {s}-end-\\d\">").Count.Should().Be(2);

		// every unit's map offers both endings, in their role's colours, with the chosen one open
		var endings = Regex.Matches(html, $@"<label class=""ma[0-9a-f]{{8}}-p-ending( open)?"" for=""{s}-(\d)"" style=""--r: ([^""]+)""").ToList();
		endings.Should().HaveCount(4);
		endings
			.Select(e => $"{e.Groups[2].Value}{(e.Groups[1].Success ? "*" : "")} {e.Groups[3].Value}")
			.Should()
			.Equal("0* var(--_line)", "1 var(--fail)", "0 var(--_line)", "1* var(--fail)");
		html.Should().NotContain("<script");
	}

	[Test]
	public void Endings_meet_at_one_junction_and_the_chosen_route_lights_when_the_story_reaches_it()
	{
		var html = MermaidAnimator.RenderPlayer(Endings);
		// in each ending's unit: a junction, every branch leaving it, the chosen route drawn over them
		var forks = Regex.Matches(html, @"<svg class=""ma[0-9a-f]{8}-p-forks""[^>]*>(.*?)</svg>").Select(m => m.Groups[1].Value).ToList();
		forks.Should().HaveCount(2);
		foreach (var fork in forks)
		{
			var branches = Regex.Matches(fork, @"<path d=""M14,([\d.]+) C").Select(m => m.Groups[1].Value).ToList();
			branches.Should().HaveCount(2, "one per ending").And.OnlyContain(y => y == branches[0], "every branch leaves the same point");
			fork.Should().Contain("rotate(45", "the junction is the flowchart's decision diamond");
			Regex
				.Match(fork, @"<path class=""(ma[0-9a-f]{8}-p-h\d+)"" d=""M14,[\d.]+ V[\d.]+ L14,")
				.Success
				.Should()
				.BeTrue("the chosen route runs from the trunk through the junction");
		}
		html.Should().Contain("2 ways it can go");
		// a compact score with endings still lists them: choosing one needs the list
		MermaidAnimator.RenderPlayer(Endings.Replace("animate\n", "animate compact\n")).Should().Contain("-map\" aria-label=\"Story\"");
	}

	[Test]
	[Arguments("as ok", "Ok")]
	[Arguments("as fail", "Fail")]
	[Arguments("", null)]
	public void A_scenario_can_say_how_it_ends(string role, string? expected)
	{
		var lines = Endings.Replace("scenario \"API 1 is down\"", $"scenario \"API 1 is down\" {role}").Split('\n');
		var diagnostics = new List<AnimationDiagnostic>();
		var score = ScoreParser.Parse(lines, ScoreParser.FindStart(lines), diagnostics);
		diagnostics.Should().BeEmpty();
		score.Scenarios[1].Role?.ToString().Should().Be(expected);
		score.Scenarios[0].Role.Should().BeNull();
	}

	[Test]
	public void An_unknown_ending_role_is_reported()
	{
		Problems(Endings.Replace("scenario \"API 1 is down\"", "scenario \"API 1 is down\" as doom"))
			.Should()
			.ContainSingle()
			.Which
			.Message
			.Should()
			.StartWith("Unknown role 'doom'");
		Problems(Endings.Replace("scenario \"API 1 is down\"", "scenario \"API 1 is down\" fail"))
			.Should()
			.ContainSingle()
			.Which
			.Message
			.Should()
			.StartWith("A scenario line takes one quoted name");
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
