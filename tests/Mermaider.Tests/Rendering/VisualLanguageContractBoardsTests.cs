using System.Globalization;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Rendering;
using Mermaider.Theming;

namespace Mermaider.Tests.Rendering;

/// <summary>
/// (boards: block, mindmap, kanban, user journey, timeline.) The structural contract of the design system on the board
/// types: titles left-aligned with the preset mark, sections / columns as the shared container in families p1, p2 …,
/// nodes on the node recipe, the accent as the story line, and layout that never depends on the style preset. It pins
/// "uses the shared component", not hues.
/// </summary>
public partial class VisualLanguageContractBoardsTests
{
	private const string Block = "block-beta\ntitle Services\ncolumns 2\n  A[\"API\"] B[\"DB\"]\n  A -- \"reads\" --> B";
	private const string Mindmap = "mindmap\n  ((Root))\n    A\n      a1\n    B\n      b1";
	private const string Kanban = "kanban\ntitle Sprint\n  todo[To Do]\n    a[One]@{ priority: 'High' }\n    c[Three]\n  done[Done]\n    b[Two]";
	private const string Journey = "journey\n  title Day\n  section One\n    Good: 5: Me\n    Bad: 1: Me, Cat\n  section Two\n    Fine: 3: Me";
	private const string Timeline = "timeline\n  title History\n  section Early\n  2002 : LinkedIn\n  section Later\n  2010 : Instagram";

	private const string AccentPaint = "var(--accent, var(--fg))";

	[Test]
	[Arguments(Block)]
	[Arguments(Kanban)]
	[Arguments(Journey)]
	[Arguments(Timeline)]
	public void Titles_are_left_aligned_with_the_preset_mark(string source)
	{
		foreach (var style in new[] { DiagramStyle.Quiet, DiagramStyle.Blueprint, DiagramStyle.Tonal })
		{
			var svg = DesignContract.Render(source, style);
			var title = TitleText().Match(svg);
			title.Success.Should().BeTrue($"{style}: the title is drawn by the shared title component");
			double.Parse(title.Groups[1].Value, CultureInfo.InvariantCulture)
				.Should().Be(DesignSystem.BoardMargin + DesignSystem.TitleIndent, "the title starts at the page margin, after its mark");
			svg.Should().Contain("text-anchor=\"start\" font-size=\"var(--fs-l)\" font-weight=\"700\"");
		}
	}

	[Test]
	[Arguments(Block)]
	[Arguments(Mindmap)]
	[Arguments(Kanban)]
	[Arguments(Journey)]
	[Arguments(Timeline)]
	public void Layout_does_not_depend_on_the_style_preset(string source)
	{
		var quiet = ViewBox().Match(DesignContract.Render(source)).Value;
		DesignContract.Render(source, DiagramStyle.Blueprint).Should().Contain(quiet);
		DesignContract.Render(source, DiagramStyle.Tonal).Should().Contain(quiet);
	}

	[Test]
	[Arguments(Block)]
	[Arguments(Mindmap)]
	[Arguments(Kanban)]
	[Arguments(Journey)]
	[Arguments(Timeline)]
	public void Boards_are_tinted_never_translucent(string source) =>
		DesignContract.Render(source).Should().NotContain("opacity=\"0.", "boards are tinted, never translucent");

	[Test]
	public void Block_nodes_are_on_the_node_recipe_in_their_cluster_family()
	{
		var svg = DesignContract.Render(Block);

		svg.Should().Contain(DesignContract.NodeGradientStop(DesignContract.Cluster(0)), "connected blocks share the first cluster family");
		svg.Should().Contain(DesignContract.Outline(DesignContract.Cluster(0)));
		svg.Should().Contain("<g class=\"node\" data-id=\"A\"");
		svg.Should().Contain(DesignContract.Pill, "edge labels are the shared Quiet pill");
		svg.Should().Contain("stroke=\"" + DesignSystem.EdgeColor + "\"");
	}

	[Test]
	public void Mindmap_root_is_the_accent_and_each_branch_its_own_cluster_family()
	{
		var svg = DesignContract.Render(Mindmap);

		svg.Should().Contain($"stroke=\"{ColorFamily.Accent().Stroke}\"", "the root is drawn in the accent family");
		svg.Should().Contain(DesignContract.NodeGradientStop(DesignContract.Cluster(1)), "the first branch has its own family");
		svg.Should().Contain(DesignContract.NodeGradientStop(DesignContract.Cluster(2)), "the second branch has its own family");
		Regex.Count(svg, "class=\"mindmap-link\" d=\"[^\"]*\" fill=\"none\" stroke=\"" + Regex.Escape(DesignContract.Family(DesignContract.Cluster(1)).Stroke)).Should().Be(2, "a branch's links are in the branch family stroke");
	}

	[Test]
	public void Mindmap_links_are_rounded_orthogonal_with_the_preset_bend()
	{
		const string source = "mindmap\n  ((Root))\n    A\n      a1\n      a2\n    B\n      b1\n      b2";
		var quiet = string.Join(' ', LinkPath().Matches(DesignContract.Render(source)).Select(m => m.Groups[1].Value));
		var blueprint = string.Join(' ', LinkPath().Matches(DesignContract.Render(source, DiagramStyle.Blueprint)).Select(m => m.Groups[1].Value));

		quiet.Should().Contain("Q", "Quiet bends are rounded");
		blueprint.Should().NotContain("Q", "Blueprint bends are square");
		blueprint.Should().Contain(" L", "the bends are still there, just square");
		blueprint.Should().NotContain("C", "links are orthogonal, never S-curves");
	}

	[Test]
	public void Mindmap_branches_spread_to_both_sides_of_the_root()
	{
		var svg = MermaidRenderer.RenderSvg("mindmap\n  ((Root))\n    A\n      a1\n    B\n      b1\n    C\n      c1\n    D\n      d1");

		var rects = MyRegex().Matches(svg);
		var circle = Regex.Match(svg, "<circle cx=\"([0-9.]+)\"");
		var rootX = double.Parse(circle.Groups[1].Value, CultureInfo.InvariantCulture);
		var centres = rects.Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)
			+ (double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) / 2)).ToList();
		centres.Should().Contain(c => c < rootX, "some branches sit left of the root");
		centres.Should().Contain(c => c > rootX, "some branches sit right of the root");
	}

	[Test]
	public void Kanban_columns_are_the_shared_container_with_a_count_badge()
	{
		var svg = DesignContract.Render(Kanban);

		var first = DesignContract.Cluster(1);
		var second = DesignContract.Cluster(2);
		svg.Should().Contain("<g class=\"kanban-column\" data-id=\"todo\" data-count=\"2\"");
		svg.Should().Contain($"fill=\"{DesignContract.Band(first)}\"", "the header strip is the column family band");
		svg.Should().Contain(DesignContract.AccentMark, "the strip carries the accent mark");
		svg.Should().MatchRegex(DesignContract.InkTextRegex(first, "To Do"), "the column title is in the family ink");
		svg.Should().MatchRegex(DesignContract.InkTextRegex(first, "2"), "the item count is a badge in the family ink");
		svg.Should().MatchRegex(DesignContract.InkTextRegex(second, "Done"), "columns alternate families in document order");
		svg.Should().Contain("<g class=\"kanban-card\"", "cards keep the elevation class");
	}

	[Test]
	public void Kanban_priority_is_a_role_dot_and_a_word()
	{
		var svg = DesignContract.Render(Kanban);

		var warning = DesignContract.Family(Themes.Default.RoleColor(ColorRole.Warning));
		svg.Should().Contain("data-priority=\"high\"");
		svg.Should().Contain($"fill=\"{warning.Stroke}\" />", "the priority dot is in the role family");
		svg.Should().MatchRegex(Regex.Escape($"fill=\"{warning.Ink}\"") + "[^>]*>High</text>", "the priority word is written out in the role ink");
	}

	[Test]
	public void Journey_faces_are_in_the_section_family_on_the_accent_curve()
	{
		var svg = DesignContract.Render(Journey);

		var one = DesignContract.Family(DesignContract.Cluster(1));
		var two = DesignContract.Family(DesignContract.Cluster(2));
		svg.Should().Contain($"fill=\"{one.Soft}\" stroke=\"{one.Stroke}\"", "faces in section One are its soft fill and stroke");
		svg.Should().Contain($"fill=\"{two.Soft}\" stroke=\"{two.Stroke}\"", "faces in section Two take that section's family");
		svg.Should().Contain($"fill=\"{one.Ink}\"", "the face features are the family ink");
		svg.Should().NotContain(DesignContract.Family(Themes.Default.RoleColor(ColorRole.Success)).Stroke, "roles no longer carry sentiment");
		svg.Should().NotContain(DesignContract.Family(Themes.Default.RoleColor(ColorRole.Failure)).Stroke, "roles no longer carry sentiment");
		svg.Should().MatchRegex("<path class=\"journey-line\"[^>]*stroke=\"" + Regex.Escape(AccentPaint) + "\"", "the sentiment curve is the accent");
		svg.Should().MatchRegex(DesignContract.InkTextRegex(DesignContract.Cluster(1), "One"), "sections are the shared container");
	}

	[Test]
	public void Journey_actors_take_the_next_palette_families()
	{
		var svg = DesignContract.Render(Journey);

		// two sections (p1, p2): actors continue at p3, p4
		svg.Should().Contain("<g class=\"legend\">");
		svg.Should().Contain($"fill=\"{DesignContract.Cluster(3)}\" stroke=\"none\"", "the first actor's legend swatch");
		svg.Should().Contain($"fill=\"{DesignContract.Cluster(4)}\" stroke=\"none\"", "the second actor's legend swatch");
	}

	[Test]
	public void Timeline_sections_are_the_shared_container_and_the_axis_is_the_accent()
	{
		var svg = DesignContract.Render(Timeline);

		var first = DesignContract.Cluster(1);
		var second = DesignContract.Cluster(2);
		svg.Should().Contain($"fill=\"{DesignContract.Band(first)}\"", "a section header is the family band");
		svg.Should().MatchRegex(DesignContract.InkTextRegex(second, "Later"), "the section title is in the family ink");
		svg.Should().MatchRegex("<path class=\"timeline-axis\"[^>]*stroke=\"" + Regex.Escape(AccentPaint) + "\"", "the time axis is the accent story line");
		svg.Should().Contain(DesignContract.NodeGradientStop(first), "periods and events are nodes in the section family");
		svg.Should().Contain("<g class=\"node timeline-event\"");
	}

	[Test]
	public void Blueprint_and_tonal_restyle_the_containers()
	{
		var blueprint = DesignContract.Render(Kanban, DiagramStyle.Blueprint);
		blueprint.Should().Contain("stroke-dasharray=\"3 4\"", "Blueprint columns are dashed tabs");
		blueprint.Should().Contain(">TO DO</text>", "Blueprint tab titles are caps");
		DesignContract.Render(Timeline, DiagramStyle.Tonal).Should().Contain("rx=\"11\" ry=\"11\"", "Tonal section headers are chips");
	}

	[GeneratedRegex("<g class=\"diagram-title\">\\s*<[^>]+/>\\s*<text x=\"([0-9.]+)\"", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
	private static partial Regex TitleText();

	[GeneratedRegex("viewBox=\"[^\"]+\"", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
	private static partial Regex ViewBox();

	[GeneratedRegex("class=\"mindmap-link\" d=\"([^\"]+)\"", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
	private static partial Regex LinkPath();
	[GeneratedRegex("<rect x=\"([0-9.]+)\"[^>]*\\swidth=\"([0-9.]+)\"")]
	private static partial Regex MyRegex();
}
