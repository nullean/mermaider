using AwesomeAssertions;
using Mermaider.Parsing;

namespace Mermaider.Native.Tests;

public class NativeApiTests
{
	[Test]
	public void The_style_in_the_options_changes_the_picture_and_the_layout_is_the_same()
	{
		const string source = "graph TD\n  A-->B";
		var quiet = NativeApi.RenderSvg(source, """{"style":"quiet"}""");
		var blueprint = NativeApi.RenderSvg(source, """{"style":"blueprint"}""");
		blueprint.Should().NotBe(quiet);
		ViewBox(blueprint).Should().Be(ViewBox(quiet));
	}

	private static string ViewBox(string svg)
	{
		var start = svg.IndexOf("viewBox=\"", StringComparison.Ordinal) + 9;
		return svg[start..svg.IndexOf('"', start)];
	}

	[Test]
	public void Text_that_is_no_diagram_is_a_parse_error()
	{
		var act = () => NativeApi.RenderSvg("this is not mermaid", null);
		act.Should().Throw<MermaidParseException>();
	}

	private const string Animated =
		"flowchart LR\n  client[Client] --> lb[Balancer] --> api[API]\n\nanimate\n  step \"A request\"\n    tour client -> lb -> api \"GET\"\n  step \"It fails\"\n    mark api fail\n";

	[Test]
	public void A_diagram_with_an_animate_block_plays_with_css_keyframes_and_one_without_does_not()
	{
		var animated = NativeApi.RenderSvg(Animated, null);
		animated.Should().Contain("@keyframes").And.Contain("animation:");
		NativeApi.RenderSvg("graph LR\n  A-->B", null).Should().NotContain("@keyframes");
	}

	[Test]
	public void The_motion_and_the_mode_are_options_and_a_mistake_in_them_is_an_error()
	{
		NativeApi.RenderSvg(Animated, """{"motion":"still","mode":"once"}""").Should().NotBeNullOrEmpty();
		var act = () => NativeApi.RenderSvg(Animated, """{"motion":"jerky"}""");
		act.Should().Throw<ArgumentException>().Which.Message.Should().Contain("unknown motion 'jerky'");
		var mode = () => NativeApi.RenderSvg(Animated, """{"mode":"sideways"}""");
		mode.Should().Throw<ArgumentException>().Which.Message.Should().Contain("unknown mode 'sideways'");
	}
}
