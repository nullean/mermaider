using AwesomeAssertions;
using Mermaider.Rendering;
using Mermaider.Theming;

namespace Mermaider.Tests.Rendering;

/// <summary>
/// (boards diagram types.) The structural contract of the shared visual language (see <see cref="VisualLanguage"/>): the same cluster colour is a
/// darker border plus a light tint of the same hue, subgraphs/namespaces carry their title in the border colour on a plain
/// box without a header band, and every edge label is the same pill. It deliberately does not pin which hue is chosen,
/// so palette changes do not break it, but it does fail when one diagram type stops looking like the others.
/// </summary>
public class VisualLanguageContractBoardsTests
{
	private static string Cluster0 => Themes.Default.PaletteAt(0);

	[Test]
	public void Mindmap_branches_take_their_own_cluster_colour_with_tinted_nodes()
	{
		var svg = MermaidRenderer.RenderSvg("mindmap\n  ((Root))\n    A\n      a1\n    B\n      b1");

		var first = Themes.Default.AutoPaletteAt(1);
		var second = Themes.Default.AutoPaletteAt(2);
		svg.Should().Contain($"fill=\"{VisualLanguage.Tint(first, VisualLanguage.NodeTint)}\" stroke=\"{VisualLanguage.Border(first)}\"");
		svg.Should().Contain($"fill=\"{VisualLanguage.Tint(second, VisualLanguage.NodeTint)}\" stroke=\"{VisualLanguage.Border(second)}\"", "the second branch has its own colour");
		svg.Should().NotContain("opacity=\"0.7\"", "nodes are tinted, not translucent");
	}

	[Test]
	public void Mindmap_branches_spread_to_both_sides_of_the_root()
	{
		var svg = MermaidRenderer.RenderSvg("mindmap\n  ((Root))\n    A\n      a1\n    B\n      b1\n    C\n      c1\n    D\n      d1");

		var rects = System.Text.RegularExpressions.Regex.Matches(svg, "<rect x=\"([0-9.]+)\"[^>]*\\swidth=\"([0-9.]+)\"");
		var circle = System.Text.RegularExpressions.Regex.Match(svg, "<circle cx=\"([0-9.]+)\"");
		var rootX = double.Parse(circle.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
		var centres = rects.Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
			+ (double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) / 2)).ToList();
		centres.Should().Contain(c => c < rootX, "some branches sit left of the root");
		centres.Should().Contain(c => c > rootX, "some branches sit right of the root");
	}

	[Test]
	public void Kanban_columns_are_tinted_groups_with_titles_in_the_border_colour()
	{
		var svg = MermaidRenderer.RenderSvg("kanban\n  todo[To Do]\n    a[One]@{ priority: 'High' }\n  done[Done]\n    b[Two]");

		var first = Themes.Default.AutoPaletteAt(0);
		svg.Should().Contain($"fill=\"{VisualLanguage.GroupFill(first, 0)}\" stroke=\"{VisualLanguage.GroupBorder(first)}\"");
		svg.Should().Contain($"fill=\"{VisualLanguage.GroupBorder(first)}\">To Do</text>", "the column title is drawn in the border colour");
		svg.Should().Contain(Themes.Default.RoleColor(ColorRole.Warning), "priority High reads through the warning role colour");
	}

	[Test]
	public void Journey_sections_are_tinted_and_faces_read_through_role_colours()
	{
		var svg = MermaidRenderer.RenderSvg("journey\n  section One\n    Good: 5: Me\n    Bad: 1: Me");

		var first = Themes.Default.AutoPaletteAt(0);
		svg.Should().Contain($"stroke=\"{VisualLanguage.Border(first)}\"");
		svg.Should().Contain($"stroke=\"{VisualLanguage.Border(Themes.Default.RoleColor(ColorRole.Success))}\"", "a score of 5 is a success face");
		svg.Should().Contain($"stroke=\"{VisualLanguage.Border(Themes.Default.RoleColor(ColorRole.Failure))}\"", "a score of 1 is a failure face");
	}

	[Test]
	public void Timeline_sections_are_tinted_groups_with_titles_in_the_border_colour()
	{
		var svg = MermaidRenderer.RenderSvg("timeline\n  section Early\n  2002 : LinkedIn\n  section Later\n  2010 : Instagram");

		var first = Themes.Default.AutoPaletteAt(0);
		var second = Themes.Default.AutoPaletteAt(1);
		svg.Should().Contain($"fill=\"{VisualLanguage.GroupFill(first, 0)}\" stroke=\"{VisualLanguage.GroupBorder(first)}\"");
		svg.Should().Contain($"fill=\"{VisualLanguage.GroupBorder(second)}\">Later</text>", "the section title is drawn in the border colour");
		svg.Should().NotContain("opacity=\"0.15\"", "events are tinted, not translucent");
	}
}
