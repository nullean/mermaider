using System.Text.RegularExpressions;
using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Theming;

namespace Mermaider.Tests.Rendering;

/// <summary>
/// Design-system contract for the time and tree diagrams (gantt, gitgraph, packet, treeview): every colour comes from a
/// family, the accent marks only the "look here" items, titles are left-aligned with the preset mark, and the presets
/// change paint only.
/// </summary>
public partial class TimeDesignTests
{
	private const string Gantt = """
		gantt
		title Shipping this file
		dateFormat  YYYY-MM-DD
		section Render
		Spike the renderer :done, a1, 2026-07-07, 1d
		Print this page    :active, a2, after a1, 1d
		section Polish
		Update tests       :crit, after a2, 12h
		Update docs        : 6h
		Ship               :milestone, m1, after a2, 0d
		""";

	private const string Git = """
		gitGraph
		commit id: "init"
		branch feature
		checkout feature
		commit id: "f1"
		checkout main
		merge feature id: "merge" tag: "v1.0"
		commit id: "hi" type: HIGHLIGHT
		""";

	private const string Packet = """
		packet
		title UDP Header
		0-15: "Source Port"
		16-31: "Dest Port"
		32-63: "Length"
		""";

	private const string Tree = """
		treeView-beta
		    repo/
		        src/
		            main.ts :::highlight ## entry
		        docs/
		            guide.md
		        README.md
		""";

	private static readonly ColorFamily P0 = DesignContract.Family(DesignContract.Cluster(0));
	private static readonly ColorFamily P1 = DesignContract.Family(DesignContract.Cluster(1));
	private const string AccentStroke = "stroke=\"var(--accent, var(--fg))\"";

	[Test]
	public void Gantt_title_is_left_aligned_with_the_preset_mark()
	{
		var svg = DesignContract.Render(Gantt);

		svg.Should().Contain("<g class=\"diagram-title\">");
		svg.Should().MatchRegex("text-anchor=\"start\" font-size=\"var\\(--fs-l\\)\" font-weight=\"700\"[^>]*>Shipping this file<");
	}

	[Test]
	public void Gantt_done_is_family_soft_with_outline_and_active_is_solid_with_an_accent_ring()
	{
		var svg = DesignContract.Render(Gantt);

		svg.Should().Contain($"fill=\"{P0.Soft}\" stroke=\"{P0.Stroke}\"", "done = section family soft + outline");
		svg.Should().Contain($"fill=\"{DesignContract.Cluster(0)}\" />", "active = solid section family base");
		svg.Should().MatchRegex("fill=\"none\" " + Regex.Escape(AccentStroke) + " stroke-width=\"1.5\"", "active carries the accent ring");
	}

	[Test]
	public void Gantt_critical_uses_the_failure_role_and_planned_the_section_family()
	{
		var svg = DesignContract.Render(Gantt);

		svg.Should().Contain($"fill=\"{Themes.Default.RoleColor(ColorRole.Failure)}\"", "critical = failure role");
		svg.Should().Contain($"fill=\"{DesignContract.Cluster(1)}\"", "planned = the second section's family");
	}

	[Test]
	public void Gantt_sections_are_eyebrows_in_family_ink_on_a_neutral_panel_with_a_state_legend()
	{
		var svg = DesignContract.Render(Gantt);

		svg.Should().MatchRegex(DesignContract.InkTextRegex(DesignContract.Cluster(0), "RENDER"));
		svg.Should().Contain("letter-spacing=\"0.08em\"");
		svg.Should().Contain("class=\"plot-panel\"").And.Contain("fill=\"var(--_group-fill)\"");
		svg.Should().Contain("stroke=\"var(--_line-soft)\"", "gridlines are soft");
		svg.Should().Contain("<g class=\"legend\">");
		foreach (var state in new[] { ">Done<", ">Active<", ">Critical<", ">Planned<" })
			svg.Should().Contain(state);
	}

	[Test]
	public void Gantt_bar_radius_follows_the_preset_but_geometry_does_not()
	{
		var quiet = DesignContract.Render(Gantt);
		var tonal = DesignContract.Render(Gantt, DiagramStyle.Tonal);
		var blueprint = DesignContract.Render(Gantt, DiagramStyle.Blueprint);

		tonal.Should().Contain("height=\"20\" rx=\"10\"", "Tonal bars are pills");
		blueprint.Should().Contain("height=\"20\" rx=\"1.5\"", "Blueprint bars are near-square");
		blueprint.Should().Contain($"stroke=\"{DesignContract.Cluster(1)}\"", "Blueprint planned bars are outlined");
		ViewBox(quiet).Should().Be(ViewBox(tonal)).And.Be(ViewBox(blueprint));
	}

	[Test]
	public void Gitgraph_lanes_are_family_strokes_with_fork_and_merge_connectors()
	{
		var svg = DesignContract.Render(Git);

		svg.Should().Contain($"data-kind=\"fork\" fill=\"none\" stroke=\"{P1.Stroke}\"", "the fork is drawn in the new branch's family");
		svg.Should().Contain($"data-kind=\"merge\" fill=\"none\" stroke=\"{P1.Stroke}\"", "the merge is drawn in the merged branch's family");
		svg.Should().Contain($"data-kind=\"lane\" fill=\"none\" stroke=\"{P0.Stroke}\"");
	}

	[Test]
	public void Gitgraph_branch_names_are_family_badges_and_tags_accent_badges()
	{
		var svg = DesignContract.Render(Git);
		var accent = ColorFamily.Accent();

		svg.Should().Contain($"fill=\"{P0.Band}\"", "branch badge in the lane family");
		svg.Should().Contain($"fill=\"{accent.Band}\"", "tag badge in the accent");
		svg.Should().MatchRegex(Regex.Escape($"fill=\"{accent.Ink}\"") + "[^>]*>v1\\.0<");
	}

	[Test]
	public void Gitgraph_highlight_and_head_use_the_accent_and_ids_are_mono_in_blueprint()
	{
		var svg = DesignContract.Render(Git);
		svg.Should().Contain("fill=\"var(--accent, var(--fg))\" stroke=\"var(--bg)\"", "the highlight commit is an accent point");
		svg.Should().Contain("data-head=\"true\"");

		var blueprint = DesignContract.Render(Git, DiagramStyle.Blueprint);
		blueprint.Should().MatchRegex("class=\"mono\"[^>]*>init<");
		blueprint.Should().Contain(">[main]<", "Blueprint badges are bracketed");
	}

	[Test]
	public void Packet_fields_are_node_cells_in_alternating_families_with_meta_bit_numbers()
	{
		var svg = DesignContract.Render(Packet);

		svg.Should().Contain(DesignContract.NodeGradientStop(DesignContract.Cluster(0)));
		svg.Should().Contain(DesignContract.NodeGradientStop(DesignContract.Cluster(1)));
		svg.Should().MatchRegex("fill=\"var\\(--_text-muted\\)\"[^>]*>15<", "bit numbers are meta text");
		svg.Should().Contain("<g class=\"diagram-title\">");

		var tonal = DesignContract.Render(Packet, DiagramStyle.Tonal);
		tonal.Should().Contain($"fill=\"{P0.Soft}\" stroke=\"none\"", "Tonal cells are soft blocks");
		ViewBox(svg).Should().Be(ViewBox(tonal));
	}

	[Test]
	public void Treeview_levels_map_to_type_roles_and_branches_to_families()
	{
		var svg = DesignContract.Render(Tree);
		var p1 = DesignContract.Cluster(1);

		svg.Should().MatchRegex("font-size=\"var\\(--fs-m\\)\"[^>]*>repo<", "level 0 is a heading");
		svg.Should().MatchRegex(DesignContract.InkTextRegex(p1, "src"), "level 1 is a subheading in its family ink");
		svg.Should().MatchRegex("font-weight=\"400\" fill=\"var\\(--_text\\)\"[^>]*>guide\\.md<", "deeper files are body text");
		svg.Should().Contain($"stroke=\"{DesignContract.Family(p1).Ink}\"", "glyphs take the branch family");
	}

	[Test]
	public void Treeview_branches_round_with_the_preset_radius()
	{
		var quiet = DesignContract.Render(Tree);
		var blueprint = DesignContract.Render(Tree, DiagramStyle.Blueprint);

		quiet.Should().MatchRegex("class=\"treeview-branches\"[\\s\\S]*<path d=\"M[^\"]* Q");
		var branches = blueprint[blueprint.IndexOf("treeview-branches", StringComparison.Ordinal)..];
		branches[..branches.IndexOf("</g>", StringComparison.Ordinal)].Should().NotContain(" Q", "Blueprint corners are square");
		ViewBox(quiet).Should().Be(ViewBox(blueprint));
	}

	[Test]
	public void No_literal_colours_reach_paint_outside_families()
	{
		foreach (var source in new[] { Gantt, Git, Packet, Tree })
		{
			var svg = DesignContract.Render(source);
			var body = svg[(svg.IndexOf("</style>", StringComparison.Ordinal) + 8)..];
			// hex values only ever appear as a family base inside color-mix / gradient stops / solid family marks
			foreach (Match m in MyRegex().Matches(body))
			{
				var hex = m.Groups[2].Value;
				var families = Enumerable.Range(0, 8).Select(DesignContract.Cluster).Append(Themes.Default.RoleColor(ColorRole.Failure));
				families.Should().Contain(c => string.Equals(c, hex, StringComparison.OrdinalIgnoreCase), $"{hex} must be a family base");
			}
		}
	}

	private static string ViewBox(string svg) => Regex.Match(svg, "viewBox=\"([^\"]+)\"").Groups[1].Value;
	[GeneratedRegex("(fill|stroke|stop-color)=\"(#[0-9A-Fa-f]{6})\"")]
	private static partial Regex MyRegex();
}
