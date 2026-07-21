using System.Xml.Linq;
using AwesomeAssertions;
using Mermaider.Models;

namespace Mermaider.Tests.Rendering;

/// <summary>
/// End-to-end HTML/XSS-injection resistance for the DEFAULT render path
/// (sanitization is always on). The rendered SVG is assumed to be published inline
/// on a public page, so every diagram-source-derived string must be escaped at its
/// rendering sink and the output allowlist must independently reject unsafe markup.
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
		doc.Root!.DescendantsAndSelf().SelectMany(e => e.Attributes()).Select(a => a.Name.LocalName);

	private static void ShouldHaveNoEventHandlersOrScripts(string svg)
	{
		var doc = ParseSvg(svg);

		AllAttributeNames(doc)
			.Should().NotContain(n => n.StartsWith("on", StringComparison.OrdinalIgnoreCase),
				"no event-handler attribute may survive into published SVG");

		doc.Root!.DescendantsAndSelf().Select(e => e.Name.LocalName)
			.Should().NotContain("script")
			.And.NotContain("foreignObject");

		doc.Root.DescendantsAndSelf()
			.Where(e => e.Attributes().Any(a => a.Name.LocalName == "href"))
			.Should().OnlyContain(e =>
				e.Name.LocalName == "image"
				&& e.Attributes().Single(a => a.Name.LocalName == "href").Value
					.StartsWith("data:image/", StringComparison.Ordinal));
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
		// The value-level paint allowlist removes the entire invalid fill value.
		svg.Should().NotContain("onmouseover=\"");
		svg.Should().NotContain("alert(document.domain)");
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

	[Test]
	public void External_url_in_source_paint_is_removed()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			  A[hi]
			  style A fill:url(https://attacker.invalid/pixel)
			""");

		ShouldHaveNoEventHandlersOrScripts(svg);
		var doc = ParseSvg(svg);
		var fills = doc.Descendants()
			.Where(e => e.Name.LocalName == "rect")
			.Select(e => e.Attribute("fill")?.Value)
			.Where(value => value is not null);
		string.Join('\n', fills).Should().NotContain("attacker.invalid");
	}

	[Test]
	public void Accessibility_and_frontmatter_strings_cannot_inject_markup()
	{
		var svg = MermaidRenderer.RenderSvg("""
			---
			title: </title><script>frontmatter-marker</script><title>
			---
			graph TD
			  accTitle: </title><script>accessibility-title-marker</script><title>
			  accDescr: </desc><foreignObject>accessibility-description-marker</foreignObject><desc>
			  A --> B
			""");

		ShouldHaveNoEventHandlersOrScripts(svg);
		svg.Should().Contain("frontmatter-marker");
		svg.Should().Contain("accessibility-title-marker");
		svg.Should().Contain("accessibility-description-marker");
	}

	[Test]
	public void Render_options_cannot_inject_css_or_svg_markup()
	{
		var svg = MermaidRenderer.RenderSvg(
			"pie\n\"A\" : 1",
			new Mermaider.Models.RenderOptions
			{
				Bg = "red;position:fixed",
				Font = "x';} body { display:none }/*",
				DataPalette = ["red\"/><script>palette-marker</script><rect fill=\"red"],
			});

		ShouldHaveNoEventHandlersOrScripts(svg);
		svg.Should().NotContain("position:fixed");
		svg.Should().NotContain("body { display:none");
		svg.Should().NotContain("palette-marker");
	}

	[Test]
	public void Invalid_or_null_palette_entries_fall_back_without_reaching_svg()
	{
		var act = () => MermaidRenderer.RenderSvg(
			"pie\n\"A\" : 1",
			new RenderOptions { DataPalette = [null!, "red;display:none"] });

		act.Should().NotThrow();
		var svg = act();
		svg.Should().NotContain("display:none");
		SvgSanitizer.SanitizeRendererOutput(svg).HasViolations.Should().BeFalse();
	}

	[Test]
	[MethodDataSource(nameof(DiagramInjectionCases))]
	public void Every_supported_diagram_escapes_documented_user_strings(string name, string source)
	{
		var svg = MermaidRenderer.RenderSvg(source, new RenderOptions { SanitizeMode = SanitizeMode.Block });

		ShouldHaveNoEventHandlersOrScripts(svg);
		svg.Should().Contain("attack-marker", $"the {name} payload must reach a rendered text/data sink");
	}

	public static IEnumerable<(string Name, string Source)> DiagramInjectionCases()
	{
		const string payload = "attack-marker</text><script>x</script><text>";
		static string Source(string template, string value) =>
			template.Replace("PAYLOAD", value, StringComparison.Ordinal);

		yield return ("flowchart node label", Source("""
			graph TD
			  A["PAYLOAD"]
			""", payload));
		yield return ("state alias", Source("""
			stateDiagram-v2
			  state "PAYLOAD" as S
			""", payload));
		yield return ("sequence message", Source("""
			sequenceDiagram
			  A->>B: PAYLOAD
			""", payload));
		yield return ("class relationship label", Source("""
			classDiagram
			  A --> B : PAYLOAD
			""", payload));
		yield return ("ER relationship label", Source("""
			erDiagram
			  A ||--|| B : PAYLOAD
			""", payload));
		yield return ("pie slice label", Source("""
			pie
			  "PAYLOAD" : 1
			""", payload));
		yield return ("quadrant point label", Source("""
			quadrantChart
			  PAYLOAD: [0.5, 0.5]
			""", payload));
		yield return ("timeline event", Source("""
			timeline
			  2026 : PAYLOAD
			""", payload));
		yield return ("git commit tag", Source("""
			gitGraph
			  commit tag: "PAYLOAD"
			""", payload));
		yield return ("radar curve label", Source("""
			radar-beta
			  axis A, B
			  curve c["PAYLOAD"]{1, 2}
			""", payload));
		yield return ("treemap label", Source("""
			treemap-beta
			  "PAYLOAD": 1
			""", payload));
		yield return ("venn set label", Source("""
			venn-beta
			  set A["PAYLOAD"]
			""", payload));
		yield return ("mindmap node label", Source("""
			mindmap
			  root
			    PAYLOAD
			""", payload));
		yield return ("gantt task label", Source("""
			gantt
			  dateFormat YYYY-MM-DD
			  PAYLOAD : a1, 2026-01-01, 1d
			""", payload));
		yield return ("journey task label", Source("""
			journey
			  PAYLOAD: 3: User
			""", payload));
		yield return ("C4 element label", Source("""
			C4Context
			  Person(p, "PAYLOAD")
			""", payload));
		yield return ("sankey node label", Source("""
			sankey-beta
			  PAYLOAD,B,1
			""", payload));
		yield return ("XY chart title", Source("""
			xychart-beta
			  title "PAYLOAD"
			  bar [1]
			""", payload));
		yield return ("requirement text", Source("""
			requirementDiagram
			  requirement r {
			    text: PAYLOAD
			  }
			""", payload));
		yield return ("packet field label", Source("""
			packet-beta
			  0-7: "PAYLOAD"
			""", payload));
		yield return ("kanban task title", Source("""
			kanban
			  Todo
			    task[PAYLOAD]
			""", payload));
		yield return ("architecture service label", Source("""
			architecture-beta
			  service s(server)[PAYLOAD]
			""", payload));
		yield return ("block node label", Source("""
			block-beta
			  A["PAYLOAD"]
			""", payload));
		yield return ("tree view label", Source("""
			treeView-beta
			  PAYLOAD
			""", payload));

		// Additional documented renderer-visible fields. These complement the one-case-per-type
		// matrix above and make each distinct text sink fail closed under Block mode.
		yield return ("flowchart edge label", Source("""
			graph TD
			  A -->|PAYLOAD| B
			""", payload));
		yield return ("flowchart subgraph label", Source("""
			graph TD
			  subgraph G[PAYLOAD]
			    A --> B
			  end
			""", payload));
		yield return ("state transition label", Source("""
			stateDiagram-v2
			  A --> B : PAYLOAD
			""", payload));
		yield return ("sequence participant alias", Source("""
			sequenceDiagram
			  participant A as PAYLOAD
			  A->>A: safe
			""", payload));
		yield return ("sequence note", Source("""
			sequenceDiagram
			  participant A
			  Note right of A: PAYLOAD
			""", payload));
		yield return ("sequence block label", Source("""
			sequenceDiagram
			  loop PAYLOAD
			    A->>A: safe
			  end
			""", payload));
		yield return ("class member", Source("""
			classDiagram
			  class A {
			    +String PAYLOAD
			  }
			""", payload));
		yield return ("class annotation", Source("""
			classDiagram
			  class A {
			    <<PAYLOAD>>
			  }
			""", payload));
		yield return ("ER attribute type", Source("""
			erDiagram
			  A {
			    PAYLOAD field
			  }
			""", payload));
		yield return ("ER attribute comment", Source("""
			erDiagram
			  A {
			    string field "PAYLOAD"
			  }
			""", payload));
		yield return ("pie title", Source("""
			pie
			  title PAYLOAD
			  "A" : 1
			""", payload));
		yield return ("quadrant title", Source("""
			quadrantChart
			  title PAYLOAD
			  A: [0.5, 0.5]
			""", payload));
		yield return ("quadrant axis label", Source("""
			quadrantChart
			  x-axis PAYLOAD --> Right
			  A: [0.5, 0.5]
			""", payload));
		yield return ("quadrant region label", Source("""
			quadrantChart
			  quadrant-1 PAYLOAD
			  A: [0.5, 0.5]
			""", payload));
		yield return ("timeline title", Source("""
			timeline
			  title PAYLOAD
			  2026 : safe
			""", payload));
		yield return ("timeline section", Source("""
			timeline
			  section PAYLOAD
			  2026 : safe
			""", payload));
		yield return ("timeline period", Source("""
			timeline
			  PAYLOAD : safe
			""", payload));
		yield return ("git commit id", Source("""
			gitGraph
			  commit id: "PAYLOAD"
			""", payload));
		yield return ("git branch name", Source("""
			gitGraph
			  branch PAYLOAD
			  commit
			""", payload));
		yield return ("radar title", Source("""
			radar-beta
			  title PAYLOAD
			  axis A, B
			""", payload));
		yield return ("radar axis label", Source("""
			radar-beta
			  axis a["PAYLOAD"], B
			  curve c{1, 2}
			""", payload));
		yield return ("treemap parent label", Source("""
			treemap-beta
			  "PAYLOAD"
			    "child": 1
			""", payload));
		yield return ("venn union label", Source("""
			venn-beta
			  set A
			  set B
			  union A, B["PAYLOAD"]
			""", payload));
		yield return ("gantt title", Source("""
			gantt
			  title PAYLOAD
			  dateFormat YYYY-MM-DD
			  Safe : a1, 2026-01-01, 1d
			""", payload));
		yield return ("gantt section", Source("""
			gantt
			  dateFormat YYYY-MM-DD
			  section PAYLOAD
			  Safe : a1, 2026-01-01, 1d
			""", payload));
		yield return ("journey title", Source("""
			journey
			  title PAYLOAD
			  Safe: 3: User
			""", payload));
		yield return ("journey section", Source("""
			journey
			  section PAYLOAD
			  Safe: 3: User
			""", payload));
		yield return ("journey actor", Source("""
			journey
			  Safe: 3: PAYLOAD
			""", payload));
		yield return ("C4 title", Source("""
			C4Context
			  title PAYLOAD
			  Person(p, "safe")
			""", payload));
		yield return ("C4 element description", Source("""
			C4Context
			  Person(p, "safe", "PAYLOAD")
			""", payload));
		yield return ("C4 relationship label", Source("""
			C4Context
			  Person(a, "A")
			  System(b, "B")
			  Rel(a, b, "PAYLOAD", "HTTPS")
			""", payload));
		yield return ("C4 relationship technology", Source("""
			C4Context
			  Person(a, "A")
			  System(b, "B")
			  Rel(a, b, "safe", "PAYLOAD")
			""", payload));
		yield return ("XY category label", Source("""
			xychart-beta
			  x-axis ["PAYLOAD"]
			  bar [1]
			""", payload));
		yield return ("XY axis title", Source("""
			xychart-beta
			  y-axis "PAYLOAD" 0 --> 10
			  bar [1]
			""", payload));
		yield return ("XY series name", Source("""
			xychart-beta
			  line "PAYLOAD" [1]
			""", payload));
		yield return ("requirement title", Source("""
			requirementDiagram
			  title PAYLOAD
			  requirement r {
			  }
			""", payload));
		yield return ("requirement element type", Source("""
			requirementDiagram
			  element e {
			    type: PAYLOAD
			  }
			""", payload));
		yield return ("requirement document reference", Source("""
			requirementDiagram
			  element e {
			    docRef: PAYLOAD
			  }
			""", payload));
		yield return ("packet title", Source("""
			packet-beta
			  title PAYLOAD
			  0-7: "safe"
			""", payload));
		yield return ("kanban title", Source("""
			kanban
			  title PAYLOAD
			  Todo
			    Safe
			""", payload));
		yield return ("kanban column title", Source("""
			kanban
			  column[PAYLOAD]
			    Safe
			""", payload));
		yield return ("kanban assigned metadata", Source("""
			kanban
			  Todo
			    task[Safe]@{ assigned: 'PAYLOAD' }
			""", payload));
		yield return ("kanban ticket metadata", Source("""
			kanban
			  Todo
			    task[Safe]@{ ticket: 'PAYLOAD' }
			""", payload));
		yield return ("architecture group label", Source("""
			architecture-beta
			  group g(cloud)[PAYLOAD]
			  service s(server)[safe] in g
			""", payload));
		yield return ("block title", Source("""
			block-beta
			  title PAYLOAD
			  A["safe"]
			""", payload));
		yield return ("tree view description", Source("""
			treeView-beta
			  safe ## PAYLOAD
			""", payload));
	}
}
