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
}
