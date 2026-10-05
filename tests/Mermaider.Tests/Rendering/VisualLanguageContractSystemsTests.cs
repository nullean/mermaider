using AwesomeAssertions;
using Mermaider.Rendering;
using Mermaider.Theming;

namespace Mermaider.Tests.Rendering;

/// <summary>
/// (systems diagram types.) The structural contract of the shared visual language (see <see cref="VisualLanguage"/>): the same cluster colour is a
/// darker border plus a light tint of the same hue, subgraphs/namespaces carry their title in the border colour on a plain
/// box without a header band, and every edge label is the same pill. It deliberately does not pin which hue is chosen,
/// so palette changes do not break it, but it does fail when one diagram type stops looking like the others.
/// </summary>
public class VisualLanguageContractSystemsTests
{
	private static string Cluster0 => Themes.Default.PaletteAt(0);

	[Test]
	public void Sequence_participants_that_talk_share_a_cluster_colour()
	{
		var svg = MermaidRenderer.RenderSvg("sequenceDiagram\n  A->>B: hi\n  C->>D: yo");

		svg.Should().Contain($"fill=\"{VisualLanguage.Tint(Cluster0, VisualLanguage.NodeTint)}\" stroke=\"{VisualLanguage.Border(Cluster0)}\"");
		var second = Themes.Default.AutoPaletteAt(1);
		svg.Should().Contain($"fill=\"{VisualLanguage.Tint(second, VisualLanguage.NodeTint)}\" stroke=\"{VisualLanguage.Border(second)}\"", "an unrelated pair is a second cluster");
	}

	[Test]
	public void Sequence_frames_are_tinted_groups_with_the_keyword_in_the_border_colour()
	{
		var svg = MermaidRenderer.RenderSvg("sequenceDiagram\n  A->>B: hi\n  alt ok\n    B-->>A: yes\n  else bad\n    B-->>A: no\n  end");

		var group = svg[svg.IndexOf("<g class=\"block\"", StringComparison.Ordinal)..];
		var stroke = System.Text.RegularExpressions.Regex.Match(group, "<rect [^>]*stroke=\"([^\"]+)\"").Groups[1].Value;
		svg.Should().Contain($"fill=\"{stroke}\"", "the keyword is drawn in the frame's border colour");
		svg.Should().NotContain("var(--_arrow)", "markers follow the line colour");
	}

	[Test]
	public void Architecture_groups_are_tinted_boxes_with_titles_in_the_border_colour()
	{
		var svg = MermaidRenderer.RenderSvg("""
			architecture-beta
			group api(cloud)[API]
			service db(database)[Database] in api
			service server(server)[Server] in api
			db:R -- L:server
			""");

		var group = svg[svg.IndexOf("class=\"architecture-group\"", StringComparison.Ordinal)..];
		var stroke = System.Text.RegularExpressions.Regex.Match(group, "<rect [^>]*stroke=\"([^\"]+)\"").Groups[1].Value;
		group.Should().NotContain("stroke-dasharray", "groups are solid, tinted boxes");
		group.Should().Contain($"fill=\"{stroke}\"", "the title is drawn in the group's border colour");
		svg.Should().NotContain("var(--_arrow)", "markers follow the line colour");
	}
}
