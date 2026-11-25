using System.Globalization;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Mermaider.Models;

namespace Mermaider.Animate.Tests;

/// <summary>The player: the figure, its caption, and the story's contents beside it.</summary>
public class PlayerTests
{
	private const string Source =
		"""
		flowchart LR
		  a[A] --> b[B]
		animate
		  build
		  step "First <b>bold</b>"
		    send a -> b
		  step "Second"
		    mark b ok
		  step "Third"
		    pulse a
		""";

	private static string Ms(double ms) => Math.Round(ms).ToString(CultureInfo.InvariantCulture) + "ms";

	private static string SvgOf(string html) => html[html.IndexOf("<svg", StringComparison.Ordinal)..(html.IndexOf("</svg>", StringComparison.Ordinal) + 6)];

	[Test]
	public void The_player_is_one_radio_group_with_play_pause_and_two_states_per_step()
	{
		var html = MermaidAnimator.RenderPlayer(Source);
		var radios = Regex.Matches(html, "<input type=\"radio\" name=\"([^\"]+)\" id=\"([^\"]+)\"( checked)?>").ToList();
		radios.Should().HaveCount(2 + (2 * 3));
		radios.Select(r => r.Groups[1].Value).Distinct().Should().HaveCount(1);
		radios.Where(r => r.Groups[3].Success).Select(r => r.Groups[2].Value).Should().ContainSingle().Which.Should().EndWith("-play");
		// arrow keys walk the steps in order, playing from each
		radios.Skip(2).Take(3).Select(r => r.Groups[2].Value[^5..]).Should().Equal("from0", "from1", "from2");
		html.Should().NotContain("<script");
	}

	[Test]
	public void The_contents_list_every_step_in_order_and_each_jumps_there()
	{
		var html = MermaidAnimator.RenderPlayer(Source);
		var rows = Regex.Matches(html, @"<label class=""ma[0-9a-f]{8}-p-row"" for=""ma[0-9a-f]{8}-p-from(\d)"".*?-c"">([^<]*)</span></label>").ToList();
		rows.Select(r => r.Groups[1].Value).Should().Equal("0", "1", "2");
		rows.Select(r => r.Groups[2].Value).Should().Equal("First &lt;b&gt;bold&lt;/b&gt;", "Second", "Third");
	}

	[Test]
	public void The_caption_is_under_the_figure_and_not_drawn_in_the_svg()
	{
		var animation = MermaidAnimator.Compile(Source);
		var html = animation.RenderPlayer();
		animation.Svg.Should().Contain(">Second</text>", "the plain SVG keeps its captions");
		SvgOf(html).Should().NotContain(">Second</text>", "the player's figure leaves them to its caption");
		Regex
			.Matches(html, @"<span class=""ma[0-9a-f]{8}-p-line [^""]+""><span class=""ma[0-9a-f]{8}-p-n"">(\d)/3</span>([^<]*)</span>")
			.Select(m => $"{m.Groups[1].Value} {m.Groups[2].Value}")
			.Should()
			.Equal("1 First &lt;b&gt;bold&lt;/b&gt;", "2 Second", "3 Third");
	}

	[Test]
	public void Each_caption_shows_while_its_step_plays_on_the_story_clock()
	{
		var animation = MermaidAnimator.Compile(Source);
		var html = animation.RenderPlayer();
		var player = MermaidAnimator.Compile(Source).Steps; // same timing as the player's compile
		var line = Regex.Match(html, @"-p-line (ma[0-9a-f]{8}-p-h\d+)""><span[^>]*>2/3").Groups[1].Value;
		line.Should().NotBeEmpty();
		var frames = Regex.Match(html, $@"@keyframes {line} \{{([^\n]*)\}}").Groups[1].Value;
		var stops = Regex
			.Matches(frames, @"([\d.]+)% \{ opacity:(\d)")
			.Select(m => (Pct: double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), Opacity: m.Groups[2].Value))
			.ToList();
		stops.Select(s => s.Opacity).Should().Equal("0", "1", "0");

		// the story clock starts where the intro ends and spans the rest of the loop
		var clock = Regex.Match(html, $@"\.{line} \{{ opacity:0; animation: {line} (\d+)ms linear (\d+)ms infinite backwards").Groups;
		clock[1].Success.Should().BeTrue();
		var length = double.Parse(clock[1].Value, CultureInfo.InvariantCulture);
		var start = double.Parse(clock[2].Value, CultureInfo.InvariantCulture);
		var steps = player.Where(s => s.Caption is not null).ToList();
		(stops[1].Pct / 100 * length).Should().BeApproximately(steps[1].Start.TotalMilliseconds - start, 2);
		(stops[2].Pct / 100 * length).Should().BeApproximately(steps[2].Start.TotalMilliseconds - start, 2);
	}

	[Test]
	public void Play_from_a_step_starts_each_clock_at_that_step()
	{
		var animation = MermaidAnimator.Compile(Source);
		var html = animation.RenderPlayer();
		var svg = SvgOf(html);
		var intro = animation.Intro.TotalMilliseconds;
		intro.Should().BeGreaterThan(0);
		var second = animation.Steps.Where(s => s.Caption is not null).ElementAt(1).Start.TotalMilliseconds;
		// every animation an element runs is put at the step on its own clock: the intro's (it starts at 0, is long
		// finished and holds) or the story's (it starts as the intro ends), in the order the element lists them
		var lists = Regex.Matches(svg, @"(?m)^\.(ma[0-9a-f]{8}-a\d+) \{ animation: ([^;]+);").ToList();
		lists.Should().NotBeEmpty();
		foreach (var list in lists)
		{
			var cls = list.Groups[1].Value;
			var expected = list.Groups[2].Value.Split(", ").Select(a => a.Contains(" 0s 1 forwards") ? Ms(-second) : Ms(intro - second)).ToList();
			var rule = Regex.Match(html, $@"-from1:checked\) svg \.{cls}[,\s][^{{]*\{{ animation-delay: ([^;}}]+)");
			rule.Success.Should().BeTrue($"{cls} has a play-from delay");
			rule.Groups[1].Value.Trim().Should().Be(string.Join(", ", expected), cls);
		}
		// beside the figure everything is on the story clock
		html.Should().MatchRegex($@"-from1:checked\) \[class\*=""ma[0-9a-f]{{8}}-p-h""\], [^{{]+\{{ animation-delay: {Ms(intro - second)}; \}}");
	}

	[Test]
	public void Each_state_shows_only_the_buttons_that_act_in_it()
	{
		var html = MermaidAnimator.RenderPlayer(Source);

		string[] Buttons(string state) =>
			[
				.. Regex.Matches(html, $"<label class=\"b when-[^\"]*-{state}\" for=\"[^\"]*-([a-z]+\\d*)\" title=\"([^\"]+)\"").Select(
					m => $"{m.Groups[2].Value}>{m.Groups[1].Value}"
				)
			];

		Buttons("play").Should().Equal("Pause>pause");
		Buttons("pause").Should().Equal("Play>play");
		Buttons("from1").Should().Equal("Pause>hold1");
		Buttons("hold1").Should().Equal("Play>from1");
	}

	[Test]
	public void The_player_takes_the_diagrams_colours_and_type()
	{
		var html = MermaidAnimator.RenderPlayer(Source, new AnimateOptions { Render = new RenderOptions { Bg = "#101418", Fg = "#e6e8ee", Font = "Lato" } });
		var root = Regex.Match(html, "^<div class=\"(ma[0-9a-f]{8}-s)\" style=\"([^\"]*)\"").Groups;
		root[2].Value.Should().Contain("--bg:#101418").And.Contain("--fg:#e6e8ee").And.Contain("--_text-sec:");
		html.Should().MatchRegex($@"\.{root[1].Value} \{{ container-type: inline-size; font-family: [^;]*Lato");
	}

	[Test]
	public void A_long_story_is_a_window_that_follows_the_playback()
	{
		static string Story(int steps) =>
			"flowchart LR\n  a --> b\nanimate once\n" + string.Concat(Enumerable.Range(1, steps).Select(k => $"  step \"Step {k}\"\n    pulse a\n"));

		var short_ = MermaidAnimator.RenderPlayer(Story(3));
		short_
			.Should()
			.MatchRegex(@"-p-win"" style=""height: 96px""", "a list that fits shows every row whole, unfaded")
			.And
			.NotMatchRegex(@"-p-list ma[0-9a-f]{8}-p-h");

		var long_ = MermaidAnimator.RenderPlayer(Story(20));
		long_.Should().MatchRegex(@"-p-win slides"" style=""height: 288px""", "nine rows, however long the story, faded where they slide");
		var slide = Regex.Match(long_, @"-p-list (ma[0-9a-f]{8}-p-h\d+)").Groups[1].Value;
		slide.Should().NotBeEmpty();
		var offsets = Regex
			.Matches(Regex.Match(long_, $@"@keyframes {slide} \{{([^\n]*)\}}").Groups[1].Value, @"translateY\((-?[\d.]+)px\)")
			.Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
			.ToList();
		offsets.Should().BeInDescendingOrder("the list moves up as the story goes on");
		offsets.First().Should().Be(0);
		offsets.Last().Should().Be(-((20 * 32) - (9 * 32)), "the last step brings the end of the list into view, no further");
	}

	private static string Steps(int count, string header = "") =>
		$"flowchart LR\n  a --> b\nanimate{header}\n" + string.Concat(Enumerable.Range(1, count).Select(k => $"  step \"Step {k}\"\n    pulse a\n"));

	private static bool HasContents(string html) => html.Contains("-map\" aria-label=\"Story\"", StringComparison.Ordinal);

	[Test]
	public void A_story_of_one_or_two_steps_is_a_figure_and_its_caption_with_the_controls_at_its_end()
	{
		var two = MermaidAnimator.RenderPlayer(Steps(2));
		HasContents(two).Should().BeFalse("a list of two steps has nothing to find in it");
		two.Should().MatchRegex(
			@"(?s)<figcaption class=""ma[0-9a-f]{8}-p-cap"">.*<span class=""ma[0-9a-f]{8}-p-ctl""><label class=""b when-",
			"the controls end the caption line"
		);
		HasContents(MermaidAnimator.RenderPlayer(Steps(3))).Should().BeTrue();
	}

	[Test]
	public void The_score_and_the_host_choose_whether_the_steps_are_listed()
	{
		HasContents(MermaidAnimator.RenderPlayer(Steps(2, " contents"))).Should().BeTrue();
		HasContents(MermaidAnimator.RenderPlayer(Steps(5, " compact"))).Should().BeFalse();
		// the host overrides the score, as it does the motion and the mode
		HasContents(MermaidAnimator.RenderPlayer(Steps(5), new AnimateOptions { Contents = PlayerContents.Never })).Should().BeFalse();
		HasContents(MermaidAnimator.RenderPlayer(Steps(2, " compact"), new AnimateOptions { Contents = PlayerContents.Always })).Should().BeTrue();
	}

	[Test]
	public void The_figure_takes_the_diagrams_title_unless_the_diagram_draws_it()
	{
		var titled = MermaidAnimator.RenderPlayer("---\ntitle: Failover <a>\n---\n" + Steps(3));
		titled.Should().MatchRegex(@"<div class=""ma[0-9a-f]{8}-s-title"">Failover &lt;a&gt;</div>");
		MermaidAnimator.RenderPlayer(Steps(3)).Should().NotContain("-s-title\">");
		// a journey draws its title as its heading; the figure does not repeat it
		var journey = MermaidAnimator.RenderPlayer(
			"journey\n  title Checkout\n  section S\n    Pay: 2: Me\n\nanimate\n  step \"a\"\n    pulse #1\n  step \"b\"\n    pulse #1\n  step \"c\"\n    pulse #1\n"
		);
		journey.Should().NotContain("-s-title\">");
	}

	[Test]
	public void Folded_contents_keep_their_clock()
	{
		// display: none would stop the list's animations, and opening it would restart them out of step with the diagram
		var html = MermaidAnimator.RenderPlayer(Steps(5));
		var narrow = Regex.Match(html, @"@container \(max-width: 639\.98px\) \{([^\n]*)\}").Groups[1].Value;
		narrow.Should().Contain("-win { height: 0 !important; visibility: hidden; }").And.NotContain("display: none");
	}
}
