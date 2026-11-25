using System.Globalization;
using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace Mermaider.Animate.Tests;

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

	[Test]
	public void The_player_is_one_radio_group_with_play_pause_and_three_states_per_step()
	{
		var html = MermaidAnimator.RenderPlayer(Source);
		var radios = Regex.Matches(html, "<input type=\"radio\" name=\"([^\"]+)\" id=\"([^\"]+)\"( checked)?>").ToList();
		radios.Should().HaveCount(2 + (3 * 3));
		radios.Select(r => r.Groups[1].Value).Distinct().Should().HaveCount(1);
		radios.Where(r => r.Groups[3].Success).Select(r => r.Groups[2].Value).Should().ContainSingle().Which.Should().EndWith("-play");
		// arrow keys walk the stills in order
		radios.Skip(2).Take(3).Select(r => r.Groups[2].Value[^6..]).Should().Equal("still0", "still1", "still2");
		html.Should().NotContain("<script");
	}

	[Test]
	public void Captions_are_encoded_in_the_step_dots()
	{
		var html = MermaidAnimator.RenderPlayer(Source);
		html.Should().Contain("aria-label=\"Step 1: First &lt;b&gt;bold&lt;/b&gt;\"");
	}

	[Test]
	public void A_still_is_the_frame_at_the_end_of_its_step()
	{
		var animation = MermaidAnimator.Compile(Source);
		var html = animation.RenderPlayer();
		var mark = Regex.Match(animation.Svg, "<g opacity=\"0\" class=\"([^\"]+)\"><rect[^>]*fill-opacity=\"0.14\"").Groups[1].Value;
		mark.Should().NotBeEmpty();
		html.Should().MatchRegex($@"-still0:checked\) svg \.{mark} \{{ opacity:0; \}}");
		html.Should().MatchRegex($@"-still1:checked\) svg \.{mark} \{{ opacity:1; \}}");
		html.Should().MatchRegex(@"-still1:checked\) svg \[class\*=""ma[0-9a-f]{8}-a""\] \{ animation: none; \}");
	}

	[Test]
	public void Play_from_a_step_starts_each_clock_at_that_step()
	{
		var animation = MermaidAnimator.Compile(Source);
		var html = animation.RenderPlayer();
		var intro = animation.Intro.TotalMilliseconds;
		intro.Should().BeGreaterThan(0);
		var second = animation.Steps.Where(s => s.Caption is not null).ElementAt(1).Start.TotalMilliseconds;
		// every animation an element runs is put at the step on its own clock: the intro's (it starts at 0, is long
		// finished and holds) or the story's (it starts as the intro ends), in the order the element lists them
		var lists = Regex.Matches(animation.Svg, @"(?m)^\.(ma[0-9a-f]{8}-a\d+) \{ animation: ([^;]+);").ToList();
		lists.Should().NotBeEmpty();
		var clocks = new HashSet<string>();
		foreach (var list in lists)
		{
			var cls = list.Groups[1].Value;
			var expected = list.Groups[2].Value.Split(", ").Select(a => a.Contains(" 0s 1 forwards") ? Ms(-second) : Ms(intro - second)).ToList();
			clocks.UnionWith(expected);
			var rule = Regex.Match(html, $@"-from1:checked\) svg \.{cls}[,\s][^{{]*\{{ animation-delay: ([^;}}]+)");
			rule.Success.Should().BeTrue($"{cls} has a play-from delay");
			rule.Groups[1].Value.Trim().Should().Be(string.Join(", ", expected), cls);
		}
		clocks.Should().BeEquivalentTo([Ms(-second), Ms(intro - second)], "the score has both an intro and a story");

		var once = MermaidAnimator.Compile(Source.Replace("animate\n", "animate once\n"));
		var start = once.Steps.Where(s => s.Caption is not null).ElementAt(1).Start.TotalMilliseconds;
		once.RenderPlayer().Should().Contain($"animation-delay: {Ms(-start)};").And.NotContain($"{Ms(-start)}, {Ms(0)}");
	}

	[Test]
	public void The_controls_drop_below_only_where_they_would_overlap_the_longest_caption()
	{
		static AnimationStep Step(string caption) => new(caption, TimeSpan.Zero, TimeSpan.FromSeconds(1));

		var shortCaptions = Player.Breakpoint([Step("Go"), Step("Stop")], 600);
		var longCaptions = Player.Breakpoint([Step("Go"), Step("A much longer caption that fills the band")], 600);
		longCaptions.Should().BeGreaterThan(shortCaptions);
		shortCaptions.Should().BeLessThan(600, "short captions leave room at the diagram's own size");
		Player.Breakpoint([Step(new string('x', 90))], 600).Should().BeGreaterThan(10000, "a caption as wide as the diagram always leaves the controls below");
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
		Buttons("still0").Should().Equal("Play from this step>from0", "Next step>still1");
		Buttons("still1").Should().Equal("Previous step>still0", "Play from this step>from1", "Next step>still2");
		Buttons("still2").Should().Equal("Previous step>still1", "Play from this step>from2");
		Buttons("from1").Should().Equal("Pause>hold1");
		Buttons("hold1").Should().Equal("Play>from1");
	}
}
