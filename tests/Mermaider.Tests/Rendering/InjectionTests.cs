using System.Xml.Linq;
using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

/// <summary>
/// End-to-end HTML/XSS-injection resistance for the DEFAULT render path
/// (no <c>Strict</c> option, so no SVG sanitizer runs). The rendered SVG is
/// assumed to be published inline on a public page, so any diagram-source-derived
/// string that reaches markup must be escaped at render time — the sanitizer is a
/// host opt-in, not the primary defense.
///
/// The core guarantee tested here: user-controlled <c>style</c> / <c>classDef</c> /
/// <c>linkStyle</c> values cannot break out of the attribute they are emitted into
/// and inject an event handler or a new element. Regression coverage for the
/// attribute-breakout vulnerability where e.g. <c>style A fill:red" onmouseover="alert(1)</c>
/// produced a live <c>onmouseover</c> attribute on the rendered <c>&lt;rect&gt;</c>.
/// </summary>
public class InjectionTests
{
	/// <summary>Parses the SVG (which also proves it is well-formed — no tag breakout).</summary>
	private static XDocument ParseSvg(string svg) => XDocument.Parse(svg);

	/// <summary>Every attribute name across the whole document.</summary>
	private static IEnumerable<string> AllAttributeNames(XDocument doc) =>
		doc.Descendants().SelectMany(e => e.Attributes()).Select(a => a.Name.LocalName);

	private static void ShouldHaveNoEventHandlersOrScripts(string svg)
	{
		var doc = ParseSvg(svg);

		AllAttributeNames(doc)
			.Should().NotContain(n => n.StartsWith("on", StringComparison.OrdinalIgnoreCase),
				"no event-handler attribute may survive into published SVG");

		doc.Descendants().Select(e => e.Name.LocalName)
			.Should().NotContain("script")
			.And.NotContain("foreignObject");
	}

	[Test]
	public void Node_style_cannot_break_out_of_fill_attribute()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			  A[hi]
			  style A fill:red" onmouseover="alert(document.domain)
			""");

		ShouldHaveNoEventHandlersOrScripts(svg);
		// The payload survives only as an inert, escaped literal fill value.
		svg.Should().NotContain("onmouseover=\"");
		svg.Should().Contain("&quot;");
	}

	[Test]
	public void ClassDef_style_cannot_break_out_of_attribute()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			  A:::bad
			  classDef bad fill:red" onmouseover="alert(1)
			""");

		ShouldHaveNoEventHandlersOrScripts(svg);
		svg.Should().NotContain("onmouseover=\"");
	}

	[Test]
	public void LinkStyle_cannot_break_out_of_edge_stroke_attribute()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			  A --> B
			  linkStyle 0 stroke:red" onload="alert(1)
			""");

		ShouldHaveNoEventHandlersOrScripts(svg);
		svg.Should().NotContain("onload=\"");
	}

	[Test]
	public void Node_text_color_cannot_break_out_of_attribute()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			  A[hi]
			  style A color:red" onmouseover="alert(1)
			""");

		ShouldHaveNoEventHandlersOrScripts(svg);
	}

	[Test]
	public void Stroke_dasharray_cannot_break_out_of_attribute()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			  A --> B
			  linkStyle 0 stroke-dasharray:4" onload="alert(1)
			""");

		ShouldHaveNoEventHandlersOrScripts(svg);
	}

	[Test]
	public void Subgraph_style_cannot_break_out_of_attribute()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			  subgraph G
			    A --> B
			  end
			  style G fill:red" onmouseover="alert(1)
			""");

		ShouldHaveNoEventHandlersOrScripts(svg);
	}

	[Test]
	public void State_diagram_style_cannot_break_out_of_attribute()
	{
		var svg = MermaidRenderer.RenderSvg("""
			stateDiagram-v2
			  [*] --> S
			  style S fill:red" onmouseover="alert(1)
			""");

		ShouldHaveNoEventHandlersOrScripts(svg);
		svg.Should().NotContain("onmouseover=\"");
	}

	[Test]
	public void Node_label_html_is_escaped_as_text()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			  A["<img src=x onerror=alert(1)>"]
			""");

		ShouldHaveNoEventHandlersOrScripts(svg);
		svg.Should().NotContain("<img");
		svg.Should().Contain("&lt;img");
	}

	[Test]
	public void Node_label_script_tag_is_escaped_as_text()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			  A["</text><script>alert(1)</script>"]
			""");

		ShouldHaveNoEventHandlersOrScripts(svg);
		svg.Should().NotContain("<script");
	}

	[Test]
	public void Edge_label_html_is_escaped_as_text()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			  A -->|"<script>alert(1)</script>"| B
			""");

		ShouldHaveNoEventHandlersOrScripts(svg);
		svg.Should().NotContain("<script");
	}

	[Test]
	public void Sequence_box_color_cannot_break_out_of_fill_attribute()
	{
		// `box` color is a free-form non-whitespace token; a crafted value must not
		// escape the fill="..." attribute on the box <rect>.
		var svg = MermaidRenderer.RenderSvg("""
			sequenceDiagram
			  box "/><image href=x onerror=alert(1)/> Team
			  participant A
			  end
			  A->>A: hi
			""");

		ShouldHaveNoEventHandlersOrScripts(svg);
		var doc = ParseSvg(svg);
		doc.Descendants().Select(e => e.Name.LocalName).Should().NotContain("image");
	}

	[Test]
	public void TreeView_css_class_cannot_break_out_of_class_attribute()
	{
		// Parser constrains :::class to [\w-], so this renders as an ordinary label;
		// the test guards that no markup escapes into the class attribute regardless.
		var svg = MermaidRenderer.RenderSvg("""
			treeView
			  root
			    child:::highlight
			""");

		ShouldHaveNoEventHandlersOrScripts(svg);
	}

	[Test]
	public void ThemeVariables_color_cannot_break_out_of_style_attribute()
	{
		// Source-authored themeVariables are user-controlled in non-strict mode and land in
		// the root <svg style="--bg:...">. A crafted color must not break out of that attribute.
		var svg = MermaidRenderer.RenderSvg(
			"""%%{init: {"themeVariables": {"background": "red\"><script>alert(1)</script>"}}}%%""" + "\n" +
			"""
			graph TD
			  A --> B
			""");

		ShouldHaveNoEventHandlersOrScripts(svg);
		svg.Should().NotContain("<script");
	}

	[Test]
	public void Legitimate_styles_are_preserved_unescaped()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			  A[hi]
			  style A fill:#f00,stroke:#00f,stroke-width:3px
			""");

		ParseSvg(svg); // well-formed
		svg.Should().Contain("fill=\"#f00\"");
		svg.Should().Contain("stroke=\"#00f\"");
		svg.Should().Contain("stroke-width=\"3px\"");
	}
}
