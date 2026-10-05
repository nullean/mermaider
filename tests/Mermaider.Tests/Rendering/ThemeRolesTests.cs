using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Theming;

namespace Mermaider.Tests.Rendering;

/// <summary>Shipped themes define every colour role, and the line colour follows the default box border unless set.</summary>
public class ThemeRolesTests
{
	public static IEnumerable<string> ThemeNames() => Themes.BuiltIn.Keys.Order();

	[Test]
	[MethodDataSource(nameof(ThemeNames))]
	public void Every_shipped_theme_sets_all_colour_roles_explicitly(string name)
	{
		var theme = Themes.BuiltIn[name];

		theme.Default.Should().NotBeNullOrEmpty();
		theme.Success.Should().NotBeNullOrEmpty();
		theme.Failure.Should().NotBeNullOrEmpty();
		theme.Warning.Should().NotBeNullOrEmpty();
		theme.Info.Should().NotBeNullOrEmpty();
		theme.Line.Should().BeNull("shipped themes let lines follow the default box border");
	}

	[Test]
	[MethodDataSource(nameof(ThemeNames))]
	public void The_line_colour_is_the_default_box_border(string name)
	{
		var theme = Themes.BuiltIn[name];
		var border = new ColorFamily("p0", theme.Default!).Stroke;

		theme.ResolvedLine.Should().Be(border);
		var svg = MermaidRenderer.RenderSvg("flowchart TD\n  A --> B", new RenderOptions { Bg = theme.Bg, Fg = theme.Fg, Default = theme.Default });
		svg.Should().Contain($"--line:{border}", "lines are drawn in the border colour of an ordinary box");
		svg.Should().Contain($"stroke=\"{border}\"", "the default box outline is that same colour");
	}

	[Test]
	public void A_line_colour_from_the_caller_wins()
	{
		var svg = MermaidRenderer.RenderSvg("flowchart TD\n  A --> B", new RenderOptions { Line = "#ff8800" });

		svg.Should().Contain("--line:#ff8800");
	}

	[Test]
	public void Lines_follow_a_custom_default_box_colour()
	{
		var svg = MermaidRenderer.RenderSvg("flowchart TD\n  A --> B", new RenderOptions { Default = "#7f3fbf" });

		svg.Should().Contain($"--line:{new ColorFamily("p0", "#7f3fbf").Stroke}");
	}
}
