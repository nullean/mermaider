using System.Globalization;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Rendering;
using Mermaider.Theming;

namespace Mermaider.Tests.Rendering;

/// <summary>
/// (systems diagram types: sequence, architecture, C4.) The contract of the shared design system: participants, services
/// and C4 boxes are nodes on the node recipe in their family, frames / groups / boundaries are the shared container,
/// edges use the shared line colour and markers, and layout never depends on the style preset.
/// </summary>
public partial class VisualLanguageContractSystemsTests
{
	private static string Cluster0 => DesignContract.Cluster(0);

	private static readonly ColorFamily Accent = ColorFamily.Accent();
	private static readonly ColorFamily Neutral = ColorFamily.Neutral();

	private const string FullSequence = """
		sequenceDiagram
		  actor U as User
		  participant F as Frontend
		  participant B as Backend
		  U->>F: Click login
		  F->>+B: POST /auth
		  Note right of B: Hash & verify
		  alt Valid
		    B-->>F: 200 JWT
		  else Invalid
		    B-->>F: 401 Unauthorized
		  end
		  B-->>-F: Done
		""";

	private const string Context = """
		C4Context
		title System Context diagram for Internet Banking System
		Person(customer, "Banking Customer", "A customer of the bank, with personal bank accounts.")
		System(banking, "Internet Banking System", "Allows customers to view accounts and make payments.")
		System_Ext(mail, "E-mail System", "The internal Microsoft Exchange e-mail system.")
		SystemDb_Ext(mainframe, "Mainframe Banking System", "Stores core banking information.")
		Rel(customer, banking, "Uses")
		Rel(banking, mail, "Sends e-mails", "SMTP")
		Rel(banking, mainframe, "Uses")
		""";

	private const string Architecture = """
		architecture-beta
		group api(cloud)[API]
		service db(database)[Database] in api
		service server(server)[Server] in api
		db:R --> L:server
		""";

	private static string ViewBox(string svg) => MyRegex().Match(svg).Groups[1].Value;

	// ---------------------------------------------------------------- sequence

	[Test]
	public void Sequence_participants_are_nodes_in_their_cluster_family()
	{
		var svg = MermaidRenderer.RenderSvg("sequenceDiagram\n  A->>B: hi\n  C->>D: yo");

		svg.Should().Contain(DesignContract.NodeGradientStop(Cluster0));
		svg.Should().Contain(DesignContract.Outline(Cluster0));
		svg.Should().Contain(DesignContract.Outline(DesignContract.Cluster(1)), "an unrelated pair is a second cluster");
	}

	[Test]
	public void Sequence_bottom_row_mirrors_the_participants()
	{
		var svg = MermaidRenderer.RenderSvg("sequenceDiagram\n  A->>B: hi");

		Regex.Count(svg, "<g class=\"actor actor-bottom\"").Should().Be(2);
		var bottom = svg[svg.IndexOf("<g class=\"actor actor-bottom\"", StringComparison.Ordinal)..];
		bottom.Should().StartWith("<g class=\"actor actor-bottom\" data-id=\"A\"");
		bottom.Should().Contain(DesignContract.Outline(Cluster0), "the bottom row is the same participant box as the top");
	}

	[Test]
	public void A_destroyed_participant_ends_at_its_cross_without_a_bottom_box()
	{
		var svg = MermaidRenderer.RenderSvg("sequenceDiagram\n  A->>B: hi\n  create participant W\n  B->>W: spawn\n  destroy W\n  B->>W: stop");

		svg.Should().Contain("<g class=\"destroy\">");
		svg.Should().NotContain("<g class=\"actor actor-bottom\" data-id=\"W\"");
	}

	[Test]
	public void Sequence_actor_is_the_participant_chip_with_a_person_glyph_in_the_family_ink()
	{
		var svg = MermaidRenderer.RenderSvg("sequenceDiagram\n  actor U as User\n  U->>B: hi");

		var start = svg.IndexOf("<g class=\"actor\" data-id=\"U\"", StringComparison.Ordinal);
		var actor = svg[start..svg.IndexOf("<g class=\"actor\" data-id=\"B\"", StringComparison.Ordinal)];
		actor.Should().Contain(DesignContract.Outline(Cluster0));
		actor.Should().Contain($"stroke=\"{DesignContract.Family(Cluster0).Ink}\"", "the glyph is drawn in the family ink");
	}

	[Test]
	public void Sequence_messages_are_edges_with_the_shared_label_pill()
	{
		var svg = MermaidRenderer.RenderSvg(FullSequence);

		svg.Should().Contain(DesignContract.Pill, "message labels are the same pill as flowchart edge labels");
		svg.Should().Contain(">Click login</text>");
		svg.Should().Contain($"stroke=\"{DesignSystem.EdgeColor}\" stroke-width=\"1.5\" stroke-dasharray=\"{DesignSystem.DashArray}\"", "replies are dashed in the dash language");
		svg.Should().MatchRegex("marker-end=\"url\\(#m[0-9a-f]{8}-mk-arrow\\)\"");
		svg.Should().NotContain("var(--_arrow)");
	}

	[Test]
	public void Sequence_activation_bars_are_the_accent()
	{
		var svg = MermaidRenderer.RenderSvg(FullSequence);

		svg.Should().MatchRegex($"class=\"activation\"[^>]*fill=\"{Regex.Escape(Accent.Top)}\" stroke=\"{Regex.Escape(Accent.Stroke)}\"");
	}

	[Test]
	public void Sequence_frames_are_containers_with_a_keyword_tab_in_their_family()
	{
		var svg = MermaidRenderer.RenderSvg(FullSequence);

		// one cluster of participants, so the first frame takes the next palette slot
		var frame = DesignContract.Cluster(1);
		var block = svg[svg.IndexOf("<g class=\"block\"", StringComparison.Ordinal)..];
		block.Should().Contain($"fill=\"{DesignContract.Band(frame)}\"", "the keyword tab is the family band");
		block.Should().MatchRegex(DesignContract.InkTextRegex(frame, "alt"), "the keyword is drawn in the family ink");
		block.Should().MatchRegex(DesignContract.InkTextRegex(frame, "Valid"), "the condition is the frame's subsection header in its ink, without brackets");
		block.Should().NotContain("[Valid]");
		block.Should().Contain($"stroke-dasharray=\"{DesignSystem.DashArray}\"", "else separators are dashed");
	}

	[Test]
	public void Sequence_notes_and_autonumbers_use_the_shared_components()
	{
		var svg = MermaidRenderer.RenderSvg(FullSequence);
		svg.Should().Contain("<g class=\"note\">");
		svg.Should().Contain($"width=\"3\" height=\"", "the note carries the accent rail");

		var numbered = MermaidRenderer.RenderSvg("sequenceDiagram\n  autonumber\n  A->>B: hi");
		numbered.Should().Contain("<g class=\"autonumber\">");
		numbered.Should().MatchRegex("<g class=\"autonumber\"><circle [^>]*fill=\"var\\(--accent, var\\(--fg\\)\\)\"", "the number is a round accent marker");
		numbered.Should().MatchRegex("fill=\"var\\(--bg\\)\"[^>]*>1</text>", "its number is knocked out in the page colour");
	}

	[Test]
	public void Sequence_box_colour_is_author_styling_and_strict_mode_drops_it()
	{
		const string src = "sequenceDiagram\n  box rgb(1,2,3) Team\n  participant A\n  end\n  A->>B: hi";

		MermaidRenderer.RenderSvg(src).Should().Contain("rgb(1,2,3)", "non-strict honours the author colour");
		MermaidRenderer.RenderSvg(src, new RenderOptions { Strict = new StrictStylingOptions() })
			.Should().NotContain("rgb(1,2,3)", "strict mode never lets a source colour reach paint");
	}

	// ---------------------------------------------------------------- architecture

	[Test]
	public void Architecture_services_are_icon_cards_on_the_node_recipe()
	{
		var svg = MermaidRenderer.RenderSvg(Architecture);

		var service = svg[svg.IndexOf("<g class=\"architecture-service\"", StringComparison.Ordinal)..];
		service.Should().Contain(DesignContract.Outline(Cluster0));
		svg.Should().Contain(DesignContract.NodeGradientStop(Cluster0));
		service.Should().Contain($"stroke=\"{DesignContract.Family(Cluster0).Ink}\"", "built-in pictograms are drawn in the family ink");
		service.Should().MatchRegex("<text [^>]*>Database</text>", "the title sits inside the card");
	}

	[Test]
	public void Architecture_groups_are_the_shared_container_with_the_icon_next_to_the_title()
	{
		var svg = MermaidRenderer.RenderSvg(Architecture);

		var groupColour = DesignContract.Cluster(1);
		svg.Should().Contain("<g class=\"architecture-group\" data-id=\"api\"");
		svg.Should().Contain(DesignContract.Band(groupColour), "the header strip is the family band");
		var header = svg[svg.IndexOf("<g class=\"architecture-group-title\"", StringComparison.Ordinal)..];
		header.Should().Contain($"stroke=\"{DesignContract.Family(groupColour).Ink}\"", "the group icon is drawn in the group ink");
		header.Should().MatchRegex(DesignContract.InkTextRegex(groupColour, "API"));
	}

	[Test]
	public void Architecture_edges_use_the_line_colour_and_shared_markers()
	{
		var svg = MermaidRenderer.RenderSvg(Architecture);

		svg.Should().MatchRegex($"class=\"architecture-edge\"[^>]*stroke=\"{Regex.Escape(DesignSystem.EdgeColor)}\" stroke-width=\"1.5\" marker-end=\"url\\(#m[0-9a-f]{{8}}-mk-arrow\\)\"");
		svg.Should().NotContain("var(--_arrow)");
	}

	[Test]
	public void Architecture_vendor_icons_keep_their_artwork_on_the_card()
	{
		var svg = MermaidRenderer.RenderSvg("architecture-beta\nservice es(elastic:elasticsearch)[Elasticsearch]");

		svg.Should().Contain(DesignContract.Outline(Cluster0), "the card is still the family");
		svg.Should().Contain("<image ");
	}

	// ---------------------------------------------------------------- C4

	[Test]
	public void C4_kinds_map_to_families()
	{
		var svg = MermaidRenderer.RenderSvg(Context);

		string Element(string id)
		{
			var start = svg.IndexOf($"<g class=\"node c4-element\" data-id=\"{id}\"", StringComparison.Ordinal);
			return svg[start..svg.IndexOf("</g>", start, StringComparison.Ordinal)];
		}

		Element("banking").Should().Contain($"stroke=\"{Accent.Stroke}\"", "the system in scope is the accent");
		Element("customer").Should().Contain(DesignContract.Outline(Cluster0), "a person is p0");
		Element("mail").Should().Contain($"stroke=\"{Neutral.Stroke}\"", "external systems are neutral");
		Element("mainframe").Should().Contain($"stroke=\"{Neutral.Stroke}\"", "external databases are neutral");
		Element("mainframe").Should().Contain(" A", "databases are cylinders");
		foreach (var mermaidC4 in new[] { "#08427B", "#1168BD", "#438DD5", "#999999" })
			svg.Should().NotContain(mermaidC4, "C4 no longer paints Mermaid's fixed palette");
	}

	[Test]
	public void C4_title_is_left_aligned_with_the_preset_mark()
	{
		var svg = MermaidRenderer.RenderSvg(Context);

		svg.Should().Contain("<g class=\"diagram-title\">");
		svg.Should().MatchRegex("<text x=\"56\" [^>]*text-anchor=\"start\"[^>]*>System Context diagram for Internet Banking System</text>");
	}

	[Test]
	public void C4_descriptions_wrap_and_are_never_cut()
	{
		const string longDescription = "Handles every incoming payment instruction, validates it against the ledger and forwards it to the clearing house";
		var svg = MermaidRenderer.RenderSvg($"C4Context\nSystem(pay, \"Payments\", \"{longDescription}\")\nSystem(b, \"Other\", \"Short\")");

		svg.Should().NotContain("…", "nothing is truncated with an ellipsis");
		var text = string.Concat(Regex.Matches(svg, ">([^<>]+)</text>").Select(m => m.Groups[1].Value + " "));
		foreach (var word in longDescription.Split(' '))
			text.Should().Contain(word);

		var heights = Regex.Matches(svg, "<g class=\"node c4-element\"[^>]*>\\s*<rect [^>]*height=\"([0-9.]+)\"")
			.Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
		heights.Should().HaveCount(2);
		heights[0].Should().BeGreaterThan(120, "the box grows to fit its description");
		heights[1].Should().Be(heights[0], "boxes in a row share one height");
	}

	[Test]
	public void C4_relation_labels_never_sit_over_box_text()
	{
		foreach (var style in new[] { DiagramStyle.Quiet, DiagramStyle.Blueprint, DiagramStyle.Tonal })
		{
			var svg = DesignContract.Render(Context, style);
			var boxes = Regex.Matches(svg, "<g class=\"node c4-element\"[^>]*>\\s*<(?:rect x=\"([0-9.]+)\" y=\"([0-9.]+)\" width=\"([0-9.]+)\" height=\"([0-9.]+)\"|path d=\"M([0-9.]+),([0-9.]+) A([0-9.]+),)")
				.Select(Box).ToList();
			boxes.Should().HaveCount(4);

			var labels = Regex.Matches(svg, "<g class=\"edge-label\"[^>]*>\\s*(?:<rect x=\"([0-9.-]+)\" y=\"([0-9.-]+)\" width=\"([0-9.]+)\" height=\"([0-9.]+)\"|<text x=\"([0-9.-]+)\" y=\"([0-9.-]+)\")").ToList();
			labels.Should().HaveCount(3);
			foreach (var label in labels)
			{
				var (lx, ly, lw, lh) = label.Groups[1].Success
					? (D(label.Groups[1]), D(label.Groups[2]), D(label.Groups[3]), D(label.Groups[4]))
					: (D(label.Groups[5]) - 40, D(label.Groups[6]) - 8, 80.0, 16.0);
				foreach (var (bx, by, bw, bh) in boxes)
				{
					var overlaps = lx < bx + bw && lx + lw > bx && ly < by + bh && ly + lh > by;
					overlaps.Should().BeFalse($"a {style} relation label must sit in a gap, not over a box");
				}
			}
		}

		static double D(Group g) => double.Parse(g.Value, CultureInfo.InvariantCulture);

		static (double, double, double, double) Box(Match m) => m.Groups[1].Success
			? (D(m.Groups[1]), D(m.Groups[2]), D(m.Groups[3]), D(m.Groups[4]))
			: (D(m.Groups[5]), D(m.Groups[6]) - 10, D(m.Groups[7]) * 2, 140);
	}

	[Test]
	public void C4_boundaries_are_dashed_containers_in_every_preset()
	{
		const string src = """
			C4Container
			Person(customer, "Customer", "A customer of the bank")
			Container_Boundary(c1, "Internet Banking") {
			  Container(spa, "Single-Page App", "JavaScript, Angular", "Provides banking UI")
			}
			Rel(customer, spa, "Uses", "HTTPS")
			""";
		foreach (var style in new[] { DiagramStyle.Quiet, DiagramStyle.Blueprint, DiagramStyle.Tonal })
		{
			var svg = DesignContract.Render(src, style);
			var boundary = svg[svg.IndexOf("<g class=\"c4-boundary\"", StringComparison.Ordinal)..];
			boundary.Should().Contain($"stroke-dasharray=\"{DesignSystem.BoundaryDash}\"", $"{style} boundaries are dashed");
			svg.Should().Contain(style == DiagramStyle.Blueprint ? ">INTERNET BANKING</text>" : ">Internet Banking</text>");
		}
	}

	// ---------------------------------------------------------------- all

	[Test]
	public void Presets_change_paint_never_layout()
	{
		foreach (var src in new[] { FullSequence, Architecture, Context })
		{
			var quiet = DesignContract.Render(src, DiagramStyle.Quiet);
			ViewBox(DesignContract.Render(src, DiagramStyle.Blueprint)).Should().Be(ViewBox(quiet));
			ViewBox(DesignContract.Render(src, DiagramStyle.Tonal)).Should().Be(ViewBox(quiet));
		}
	}

	[GeneratedRegex("viewBox=\"([^\"]+)\"")]
	private static partial Regex MyRegex();
}
