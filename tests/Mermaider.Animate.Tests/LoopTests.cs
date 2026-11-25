using System.Text.RegularExpressions;
using System.Xml.Linq;
using AwesomeAssertions;

namespace Mermaider.Animate.Tests;

/// <summary>How a loop begins, resets and repeats, and how an animation waits for its reader.</summary>
public partial class LoopTests
{
	private const string Flow = """
		flowchart LR
		  a[A] --> b[B]
		  b --> c[C]
		""";

	private const string Story = Flow +
		"""

		animate
		  build
		  step "one"
		    send a -> b "hi"
		    mark b fail
		    badge b "down" as fail
		  step "two"
		    fill c 3/4
		    cut b -> c
		""";

	[GeneratedRegex("(^|;)opacity:0(;|$)")]
	private static partial Regex Hidden();

	private static string StyleOf(string svg) => XDocument.Parse(svg).Root!.Elements().First(e => e.Name.LocalName == "style").Value;

	[Test]
	public void The_build_plays_once_and_the_story_repeats_after_it()
	{
		var animation = MermaidAnimator.Compile(Story);
		animation.Intro.Should().BeGreaterThan(TimeSpan.Zero);
		var intro = Math.Round(animation.Intro.TotalMilliseconds);
		var story = Math.Round(animation.Duration.TotalMilliseconds - animation.Intro.TotalMilliseconds);
		var style = StyleOf(animation.Svg);
		style.Should().Contain($"{intro}ms linear 0s 1 forwards").And.Contain($"{story}ms linear {intro}ms infinite");
	}

	[Test]
	public void A_pass_ends_where_the_story_began_so_the_loop_is_seamless()
	{
		var animation = MermaidAnimator.Compile(Story);
		animation.RenderFrame(animation.Duration).Should().Be(animation.RenderFrame(animation.Intro));
	}

	[Test]
	public void After_the_intro_the_diagram_never_leaves_the_screen()
	{
		var animation = MermaidAnimator.Compile(Story);
		for (var t = animation.Intro.TotalMilliseconds; t <= animation.Duration.TotalMilliseconds; t += 100)
		{
			var nodes = XDocument
				.Parse(animation.RenderFrame(TimeSpan.FromMilliseconds(t)))
				.Descendants()
				.Where(e => ((string?)e.Attribute("class") ?? "").Split(' ').Contains("node"));
			nodes.Should().OnlyContain(n => n.AncestorsAndSelf().All(a => !Hidden().IsMatch((string?)a.Attribute("style") ?? "")), $"at {t}ms");
		}
	}

	[Test]
	public void A_loop_with_nothing_after_its_build_plays_once()
	{
		var animation = MermaidAnimator.Compile(Flow + "\nanimate\n");
		StyleOf(animation.Svg).Should().NotContain("infinite");
		animation.Intro.Should().Be(TimeSpan.Zero);
	}

	[Test]
	public void Once_keeps_the_final_state()
	{
		var animation = MermaidAnimator.Compile(Story.Replace("animate\n", "animate once\n"));
		var end = XDocument.Parse(animation.RenderFrame(animation.Duration));
		var badge = end.Descendants().Single(e => e.Name.LocalName == "text" && e.Value == "down");
		badge.AncestorsAndSelf().Should().OnlyContain(a => !Hidden().IsMatch((string?)a.Attribute("style") ?? ""));
	}

	[Test]
	public void Loops_wait_for_the_reader_where_triggers_are_supported()
	{
		var style = StyleOf(MermaidAnimator.RenderSvg(Story));
		var block = style[style.IndexOf("@supports (timeline-trigger", StringComparison.Ordinal)..];
		block.Should().MatchRegex(@"-root \{ timeline-trigger: --ma[0-9a-f]{8} view\(\) entry 30% exit 0%; \}");

		// every animation of an element gets its own trigger entry, or the ones after the first start on load
		foreach (Match rule in Regex.Matches(style, @"(?m)^\.(ma[0-9a-f]{8}-a\d+) \{ animation: ([^;]+);"))
		{
			var animations = rule.Groups[2].Value.Split(',').Length;
			var trigger = Regex.Match(block, $@"[ ,]\.{rule.Groups[1].Value}[ ,][^{{]*\{{ animation-trigger: ([^;]+);");
			trigger.Success.Should().BeTrue(rule.Groups[1].Value);
			trigger.Groups[1].Value.Split(',').Length.Should().Be(animations, rule.Groups[1].Value);
		}

		StyleOf(MermaidAnimator.RenderSvg(Story.Replace("animate\n", "animate scrub\n"))).Should().NotContain("timeline-trigger");
	}

	[Test]
	public void The_first_beat_draws_the_eye_to_its_subject()
	{
		var animation = MermaidAnimator.Compile(Story);
		var story = animation.Steps.First(s => s.Caption is not null);
		var frame = XDocument.Parse(animation.RenderFrame(story.Start + TimeSpan.FromMilliseconds(200)));
		var a = frame.Descendants().Single(e => (string?)e.Attribute("data-id") == "a");
		((string?)a.Parent!.Attribute("style") ?? "").Should().MatchRegex(@"scale\(1\.0[1-9]");
	}
}
