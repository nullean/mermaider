using System.Text.RegularExpressions;
using System.Xml.Linq;
using AwesomeAssertions;
using Mermaider.Animate.Timeline;

namespace Mermaider.Animate.Tests;

/// <summary>What the stylesheet leaves out, and why the browser still plays the same frames.</summary>
public class CssEmitterTests
{
	// a loop of 10 s whose intro takes the first 2 s
	private const double Duration = 10000;
	private const double Intro = 2000;

	private static XElement Element() => new(XName.Get("g", "http://www.w3.org/2000/svg"));

	private static Channel Opacity(XElement element) => new(element, Property.Opacity, Channel.Value(0), ReducedMotion.Cut);

	private static Channel Rise(XElement element) => new(element, Property.Transform, Channel.Transform(0, 6, 1), ReducedMotion.Off);

	private static string Emit(params Channel[] channels) =>
		CssEmitter.Emit(channels, Duration, Intro, loops: true, PlaybackMode.Loop, "t", new Dictionary<XElement, string>());

	[Test]
	public void A_channel_still_during_the_intro_plays_on_the_story_clock_alone_and_fills_the_intro()
	{
		var mark = Opacity(Element());
		mark.Animate(4000, 300, Channel.Value(1), Easing.Enter);

		var entries = CssEmitter.Plan([mark], Duration, Intro, loops: true).Single().Entries;
		entries.Should().ContainSingle().Which.Timing.Should().Be("8000ms linear 2000ms infinite backwards");
	}

	[Test]
	public void A_channel_still_during_the_story_holds_the_intro_s_last_frame()
	{
		var node = Opacity(Element());
		node.Animate(500, 300, Channel.Value(1), Easing.Enter);

		var entries = CssEmitter.Plan([node], Duration, Intro, loops: true).Single().Entries;
		entries.Should().ContainSingle().Which.Timing.Should().Be("2000ms linear 0s 1 forwards");
	}

	[Test]
	public void A_channel_that_moves_in_both_runs_on_both_clocks()
	{
		var node = Opacity(Element());
		node.Animate(500, 300, Channel.Value(1), Easing.Enter);
		node.Animate(5000, 300, Channel.Value(0.22), Easing.Enter);

		CssEmitter
			.Plan([node], Duration, Intro, loops: true)
			.Single()
			.Entries
			.Select(e => e.Timing)
			.Should()
			.Equal("2000ms linear 0s 1 forwards", "8000ms linear 2000ms infinite");
	}

	[Test]
	public void Content_changes_come_first_so_the_reduced_motion_list_starts_the_full_one()
	{
		// the rise is listed first and plays only in the intro; the opacity plays in both. Under reduced motion only the
		// opacity runs, so unless it comes first its story animation would take the rise's intro delay in the player.
		var element = Element();
		var rise = Rise(element);
		rise.Animate(500, 300, Channel.Transform(0, 0, 1), Easing.Enter);
		var opacity = Opacity(element);
		opacity.Animate(500, 300, Channel.Value(1), Easing.Enter);
		opacity.Animate(5000, 300, Channel.Value(0.22), Easing.Enter);

		var entries = CssEmitter.Plan([rise, opacity], Duration, Intro, loops: true).Single().Entries;
		entries.Select(e => e.Channel).Should().Equal(opacity, opacity, rise);
		var reduced = entries.Where(e => e.Channel.Reduced == ReducedMotion.Cut).Select(e => e.Start);
		reduced.Should().Equal(entries.Select(e => e.Start).Take(reduced.Count()));
	}

	[Test]
	public void Identical_keyframes_are_written_once_and_shared()
	{
		var a = Opacity(Element());
		var b = Opacity(Element());
		foreach (var channel in new[] { a, b })
			channel.Animate(4000, 300, Channel.Value(1), Easing.Enter);

		var css = Emit(a, b);
		Regex.Matches(css, "@keyframes ").Should().HaveCount(2, "one for the change and one for its reduced-motion cut");
		Regex.Matches(css, @"(?m)^\.t-a\d+ \{ animation: (t-k\d+) ").Select(m => m.Groups[1].Value).Distinct().Should().ContainSingle();
	}

	[Test]
	public void A_keyframe_between_two_that_hold_the_same_value_is_left_out()
	{
		var node = Opacity(Element());
		node.Animate(3000, 300, Channel.Value(1), Easing.Enter);
		node.Animate(5000, 300, Channel.Value(1), Easing.Enter);
		node.Animate(7000, 300, Channel.Value(0), Easing.Exit);

		var css = Emit(node);
		var keyframes = Regex.Match(css, @"@keyframes t-k0 \{([^\n]*)\}").Groups[1].Value;
		// 0%, the change to 1, held until the change back to 0 starts, and the end: the no-op change at 5 s writes nothing
		Regex.Matches(keyframes, @"[\d.]+% \{").Select(m => m.Value).Should().Equal("0% {", "12.5% {", "16.25% {", "62.5% {", "66.25% {", "100% {");
	}
}
