using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace Mermaider.Animate.Tests;

public class PlayerTests
{
	private const string Source =
		"""
		flowchart LR
		  a[A] --> b[B]
		animate once
		  step "First <b>bold</b>"
		    send a -> b
		  step "Second"
		    mark b ok
		""";

	[Test]
	public void The_player_has_play_pause_and_a_chip_per_step_as_one_radio_group()
	{
		var html = MermaidAnimator.RenderPlayer(Source);
		var names = Regex.Matches(html, "<input type=\"radio\" id=\"[^\"]+\" name=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
		names.Should().HaveCount(4).And.OnlyContain(n => n == names[0]);
		html.Should().Contain("-play\" name=").And.Contain("▶ Play").And.Contain("❚❚ Pause");
		html.Should().NotContain("<script");
	}

	[Test]
	public void Captions_are_encoded_in_the_chips()
	{
		var html = MermaidAnimator.RenderPlayer(Source);
		html.Should().Contain("<b>1</b>First &lt;b&gt;bold&lt;/b&gt;</label>");
	}

	[Test]
	public void A_chosen_step_is_the_frame_at_the_end_of_that_step()
	{
		var animation = MermaidAnimator.Compile(Source);
		var html = animation.RenderPlayer();

		// the mark appears in step 2: still at 0 when step 1 is chosen, at 1 when step 2 is
		var mark = Regex.Match(animation.Svg, "<g opacity=\"0\" class=\"([^\"]+)\"><rect[^>]*fill-opacity=\"0.14\"").Groups[1].Value;
		mark.Should().NotBeEmpty();
		html.Should().MatchRegex($@"-s0:checked\) svg \.{mark} \{{ opacity:0; \}}");
		html.Should().MatchRegex($@"-s1:checked\) svg \.{mark} \{{ opacity:1; \}}");
		html.Should().MatchRegex(@"-s1:checked\) svg \[class\*=""ma[0-9a-f]{8}-a""\] \{ animation: none; \}");
	}
}
