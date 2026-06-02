using AwesomeAssertions;
using System.Globalization;

namespace Mermaider.Tests.Rendering;

public class SvgRendererTests
{
	[Test]
	public void RendersSvgWithXmlHeader()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			  A --> B
			""");

		svg.Should().StartWith("<svg xmlns=");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void SvgContainsNodes()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			  A[Hello] --> B[World]
			""");

		svg.Should().Contain("data-id=\"A\"");
		svg.Should().Contain("data-id=\"B\"");
		svg.Should().Contain("Hello");
		svg.Should().Contain("World");
	}

	[Test]
	public void SvgContainsEdges()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			  A --> B
			""");

		svg.Should().Contain("class=\"edge\"");
		svg.Should().Contain("data-from=\"A\"");
		svg.Should().Contain("data-to=\"B\"");
	}

	[Test]
	public void FlowchartEdgesAreRoundedByDefault()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			  A --> B
			  A --> C
			""");

		svg.Should().Contain("class=\"edge\"");
		svg.Should().Contain(" Q");
	}

	[Test]
	public void FlowchartEdgesCanBeStraight()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			  A --> B
			  A --> C
			""", new() { RoundedEdges = false });

		svg.Should().Contain("class=\"edge\"");
		svg.Should().NotContain(" Q");
	}

	[Test]
	public void SvgContainsStyleBlock()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			  A --> B
			""");

		svg.Should().Contain("<style>");
		svg.Should().Contain("</style>");
		svg.Should().Contain("color-mix");
	}

	[Test]
	public void RespectCustomColors()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			  A --> B
			""", new()
		{
			Bg = "#1e1e2e",
			Fg = "#cdd6f4",
		});

		svg.Should().Contain("--bg:#1e1e2e");
		svg.Should().Contain("--fg:#cdd6f4");
	}

	[Test]
	public void TransparentBackgroundByDefault()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			  A --> B
			""");

		svg.Should().NotContain("background:var(--bg)");
	}

	[Test]
	public void OpaqueBackgroundWhenTransparentDisabled()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			  A --> B
			""", new() { Transparent = false });

		svg.Should().Contain("background:var(--bg)");
	}

	[Test]
	public void RenderSvgUsesInvariantNumberFormatting()
	{
		var previousCulture = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

			var svg = MermaidRenderer.RenderSvg("""
				graph TD
				  A --> B
				""");

			var openTag = svg[..svg.IndexOf('>')];
			openTag.Should().Contain("height=\"").And.Contain(".");
			openTag.Should().NotContain("244,");
			openTag.Should().NotMatchRegex("""\b(?:viewBox|width|height)="[^"]*\d,\d""");
		}
		finally
		{
			CultureInfo.CurrentCulture = previousCulture;
		}
	}

	[Test]
	public void LinkStyleAppliesStrokeToEdge()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph LR
			  A --> B
			  linkStyle 0 stroke:#ff3,stroke-width:4px
			""");

		svg.Should().Contain("stroke=\"#ff3\"");
		svg.Should().Contain("stroke-width=\"4px\"");
	}

	[Test]
	public void LinkStyleAppliesDasharray()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph LR
			  A --> B
			  linkStyle 0 stroke-dasharray:5 5
			""");

		svg.Should().Contain("stroke-dasharray=\"5 5\"");
	}

	[Test]
	public void LinkStyleDefaultAppliesStrokeToAllEdges()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph LR
			  A --> B
			  B --> C
			  linkStyle default stroke:#333,stroke-width:1px
			""");

		// Both edges should have the custom stroke
		svg.Should().Contain("stroke=\"#333\"");
		svg.Should().Contain("stroke-width=\"1px\"");
	}

	[Test]
	public void LinkStyleColorApplesToEdgeLabel()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph LR
			  A -->|yes| B
			  linkStyle 0 color:red
			""");

		svg.Should().Contain("fill=\"red\"");
	}

	[Test]
	public void NearAlignedVerticalEdgesDoNotRenderTinyDoglegs()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TB
			  Root["Painless Operators"]
			  Ref["Reference"]
			  Desc["Object interaction and<br/>safe data access"]
			  Detail["Method Call . ( )<br/>Field Access .<br/>Null Safe ?.<br/>New Instance new ( )<br/>String Concatenation +<br/>List/Map Init ["]
			  Root --> Ref
			  Ref --> Desc
			  Desc --> Detail
			""");

		var path = GetEdgePath(svg, "Desc", "Detail");

		CountSegments(path).Should().Be(1, "a near-vertical edge should be collapsed to one straight segment");
	}

	[Test]
	public void EdgesSharingHorizontalTrunkUseAlignedStems()
	{
		var svg = MermaidRenderer.RenderSvg("""
			flowchart LR
			  A[Start] --> B{Decision}
			  B -->|Yes| C[Action 1]
			  B -->|No| D[Action 2]
			  C --> E[End]
			  D --> E
			""");

		var cToE = GetEdgePath(svg, "C", "E");
		var dToE = GetEdgePath(svg, "D", "E");

		StemX(cToE).Should().BeApproximately(StemX(dToE), 4.0);
	}

	private static string GetEdgePath(string svg, string from, string to)
	{
		var edgeStart = svg.IndexOf($"<path class=\"edge\" data-from=\"{from}\" data-to=\"{to}\"", StringComparison.Ordinal);
		edgeStart.Should().BeGreaterThanOrEqualTo(0, $"edge {from} -> {to} should be present");
		var pathStart = svg.IndexOf(" d=\"", edgeStart, StringComparison.Ordinal);
		pathStart.Should().BeGreaterThanOrEqualTo(0, $"edge {from} -> {to} should have a path");
		pathStart += 4;
		var pathEnd = svg.IndexOf('"', pathStart);
		return svg[pathStart..pathEnd];
	}

	private static double StemX(string path)
	{
		var comma = path.IndexOf(',');
		comma.Should().BeGreaterThan(1);
		return double.Parse(path[1..comma], CultureInfo.InvariantCulture);
	}

	private static int CountSegments(string path)
	{
		var count = 0;
		var index = -1;
		while ((index = path.IndexOf(" L", index + 1, StringComparison.Ordinal)) >= 0)
			count++;
		return count;
	}
}
