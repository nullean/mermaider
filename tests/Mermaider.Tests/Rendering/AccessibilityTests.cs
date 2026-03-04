using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

public class AccessibilityTests
{
	// ========================================================================
	// accTitle single-line
	// ========================================================================

	[Test]
	public void Flowchart_accTitle_adds_role_and_aria_label()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			accTitle: My Flowchart Title
			A --> B
			""");

		svg.Should().Contain("role=\"img\"");
		svg.Should().Contain("aria-label=\"My Flowchart Title\"");
		svg.Should().Contain("aria-roledescription=\"flowchart\"");
		svg.Should().Contain("<title>My Flowchart Title</title>");
	}

	[Test]
	public void Flowchart_accDescr_adds_desc_element()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			accTitle: My Flowchart
			accDescr: This shows the login process
			A --> B
			""");

		svg.Should().Contain("<title>My Flowchart</title>");
		svg.Should().Contain("<desc>This shows the login process</desc>");
	}

	[Test]
	public void Flowchart_accDescr_multiline()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			accTitle: My Flowchart
			accDescr {
			Line one of description
			Line two of description
			}
			A --> B
			""");

		svg.Should().Contain("<title>My Flowchart</title>");
		svg.Should().Contain("<desc>Line one of description\nLine two of description</desc>");
	}

	[Test]
	public void No_accessibility_when_no_directives()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			A --> B
			""");

		svg.Should().NotContain("role=\"img\"");
		svg.Should().NotContain("aria-label=");
		svg.Should().NotContain("aria-roledescription=");
		svg.Should().NotContain("<title>");
		svg.Should().NotContain("<desc>");
	}

	[Test]
	public void AccTitle_only_without_accDescr()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			accTitle: Just a title
			A --> B
			""");

		svg.Should().Contain("<title>Just a title</title>");
		svg.Should().NotContain("<desc>");
	}

	[Test]
	public void AccDescr_only_without_accTitle()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			accDescr: Just a description
			A --> B
			""");

		svg.Should().Contain("<desc>Just a description</desc>");
		svg.Should().NotContain("<title>");
		svg.Should().NotContain("aria-label=");
		svg.Should().Contain("role=\"img\"");
	}

	[Test]
	public void AccTitle_with_special_characters_is_escaped()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			accTitle: A "title" with <special> & chars
			A --> B
			""");

		svg.Should().Contain("aria-label=\"A &quot;title&quot; with &lt;special&gt; &amp; chars\"");
		svg.Should().Contain("<title>A &quot;title&quot; with &lt;special&gt; &amp; chars</title>");
	}

	// ========================================================================
	// All 13 diagram types
	// ========================================================================

	[Test]
	public void Sequence_diagram_accessibility()
	{
		var svg = MermaidRenderer.RenderSvg("""
			sequenceDiagram
			accTitle: Auth Flow
			accDescr: Shows authentication sequence
			Alice->>Bob: Hello
			""");

		svg.Should().Contain("role=\"img\"");
		svg.Should().Contain("aria-roledescription=\"sequence diagram\"");
		svg.Should().Contain("<title>Auth Flow</title>");
		svg.Should().Contain("<desc>Shows authentication sequence</desc>");
	}

	[Test]
	public void Class_diagram_accessibility()
	{
		var svg = MermaidRenderer.RenderSvg("""
			classDiagram
			accTitle: Class Hierarchy
			accDescr: Shows inheritance
			class Animal
			""");

		svg.Should().Contain("role=\"img\"");
		svg.Should().Contain("aria-roledescription=\"class diagram\"");
		svg.Should().Contain("<title>Class Hierarchy</title>");
		svg.Should().Contain("<desc>Shows inheritance</desc>");
	}

	[Test]
	public void Er_diagram_accessibility()
	{
		var svg = MermaidRenderer.RenderSvg("""
			erDiagram
			accTitle: DB Schema
			accDescr: Database entity relationships
			CUSTOMER ||--o{ ORDER : places
			""");

		svg.Should().Contain("role=\"img\"");
		svg.Should().Contain("aria-roledescription=\"ER diagram\"");
		svg.Should().Contain("<title>DB Schema</title>");
		svg.Should().Contain("<desc>Database entity relationships</desc>");
	}

	[Test]
	public void Pie_chart_accessibility()
	{
		var svg = MermaidRenderer.RenderSvg("""
			pie
			accTitle: Market Share
			accDescr: Browser usage statistics
			"Chrome" : 70
			"Firefox" : 20
			"Other" : 10
			""");

		svg.Should().Contain("role=\"img\"");
		svg.Should().Contain("aria-roledescription=\"pie chart\"");
		svg.Should().Contain("<title>Market Share</title>");
		svg.Should().Contain("<desc>Browser usage statistics</desc>");
	}

	[Test]
	public void State_diagram_accessibility()
	{
		var svg = MermaidRenderer.RenderSvg("""
			stateDiagram-v2
			accTitle: Order States
			accDescr: Order lifecycle
			[*] --> Pending
			Pending --> Shipped
			""");

		svg.Should().Contain("role=\"img\"");
		svg.Should().Contain("aria-roledescription=\"state diagram\"");
		svg.Should().Contain("<title>Order States</title>");
		svg.Should().Contain("<desc>Order lifecycle</desc>");
	}

	[Test]
	public void Quadrant_chart_accessibility()
	{
		var svg = MermaidRenderer.RenderSvg("""
			quadrantChart
			accTitle: Priority Matrix
			accDescr: Effort vs impact analysis
			x-axis Low Effort --> High Effort
			y-axis Low Impact --> High Impact
			quadrant-1 Plan
			quadrant-2 Do First
			quadrant-3 Delegate
			quadrant-4 Eliminate
			""");

		svg.Should().Contain("role=\"img\"");
		svg.Should().Contain("aria-roledescription=\"quadrant chart\"");
		svg.Should().Contain("<title>Priority Matrix</title>");
		svg.Should().Contain("<desc>Effort vs impact analysis</desc>");
	}

	[Test]
	public void Timeline_accessibility()
	{
		var svg = MermaidRenderer.RenderSvg("""
			timeline
			accTitle: Project Timeline
			accDescr: Key milestones
			section Phase 1
			Task A : 2024
			""");

		svg.Should().Contain("role=\"img\"");
		svg.Should().Contain("aria-roledescription=\"timeline\"");
		svg.Should().Contain("<title>Project Timeline</title>");
		svg.Should().Contain("<desc>Key milestones</desc>");
	}

	[Test]
	public void GitGraph_accessibility()
	{
		var svg = MermaidRenderer.RenderSvg("""
			gitGraph
			accTitle: Release History
			accDescr: Git branching model
			commit
			branch develop
			commit
			""");

		svg.Should().Contain("role=\"img\"");
		svg.Should().Contain("aria-roledescription=\"git graph\"");
		svg.Should().Contain("<title>Release History</title>");
		svg.Should().Contain("<desc>Git branching model</desc>");
	}

	[Test]
	public void Radar_chart_accessibility()
	{
		var svg = MermaidRenderer.RenderSvg("""
			radar-beta
			accTitle: Skill Assessment
			accDescr: Developer skills radar
			axis A, B, C
			curve Team1: 3, 4, 5
			""");

		svg.Should().Contain("role=\"img\"");
		svg.Should().Contain("aria-roledescription=\"radar chart\"");
		svg.Should().Contain("<title>Skill Assessment</title>");
		svg.Should().Contain("<desc>Developer skills radar</desc>");
	}

	[Test]
	public void Treemap_accessibility()
	{
		var svg = MermaidRenderer.RenderSvg("""
			treemap-beta
			accTitle: File Sizes
			accDescr: Disk usage treemap
			root
			  A: 100
			  B: 200
			""");

		svg.Should().Contain("role=\"img\"");
		svg.Should().Contain("aria-roledescription=\"treemap\"");
		svg.Should().Contain("<title>File Sizes</title>");
		svg.Should().Contain("<desc>Disk usage treemap</desc>");
	}

	[Test]
	public void Venn_diagram_accessibility()
	{
		var svg = MermaidRenderer.RenderSvg("""
			venn-beta
			accTitle: Set Overlaps
			accDescr: Venn diagram of groups
			set A
			set B
			""");

		svg.Should().Contain("role=\"img\"");
		svg.Should().Contain("aria-roledescription=\"venn diagram\"");
		svg.Should().Contain("<title>Set Overlaps</title>");
		svg.Should().Contain("<desc>Venn diagram of groups</desc>");
	}

	[Test]
	public void Mindmap_accessibility()
	{
		var svg = MermaidRenderer.RenderSvg("""
			mindmap
			accTitle: Ideas
			accDescr: Brainstorm map
			  root((Central))
			    Topic A
			    Topic B
			""");

		svg.Should().Contain("role=\"img\"");
		svg.Should().Contain("aria-roledescription=\"mindmap\"");
		svg.Should().Contain("<title>Ideas</title>");
		svg.Should().Contain("<desc>Brainstorm map</desc>");
	}

	// ========================================================================
	// Accessibility directives do not interfere with diagram parsing
	// ========================================================================

	[Test]
	public void AccTitle_does_not_appear_as_node_in_flowchart()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			accTitle: My Title
			A[Start] --> B[End]
			""");

		svg.Should().Contain("data-id=\"A\"");
		svg.Should().Contain("data-id=\"B\"");
		svg.Should().NotContain("data-id=\"accTitle\"");
	}

	[Test]
	public void AccDescr_does_not_appear_as_node_in_flowchart()
	{
		var svg = MermaidRenderer.RenderSvg("""
			graph TD
			accDescr: My Description
			A[Start] --> B[End]
			""");

		svg.Should().Contain("data-id=\"A\"");
		svg.Should().Contain("data-id=\"B\"");
		svg.Should().NotContain("data-id=\"accDescr\"");
	}

	// ========================================================================
	// Parser unit tests
	// ========================================================================

	[Test]
	public void Parser_extracts_title_and_single_line_description()
	{
		var lines = new[]
		{
			"graph TD",
			"accTitle: Test Title",
			"accDescr: Test Description",
			"A --> B",
		};

		var (info, filtered) = Mermaider.Parsing.AccessibilityParser.Extract(lines);

		info.Title.Should().Be("Test Title");
		info.Description.Should().Be("Test Description");
		info.HasContent.Should().BeTrue();
		filtered.Should().HaveCount(2);
		filtered[0].Should().Be("graph TD");
		filtered[1].Should().Be("A --> B");
	}

	[Test]
	public void Parser_extracts_multiline_description()
	{
		var lines = new[]
		{
			"graph TD",
			"accTitle: Test Title",
			"accDescr {",
			"First line",
			"Second line",
			"}",
			"A --> B",
		};

		var (info, filtered) = Mermaider.Parsing.AccessibilityParser.Extract(lines);

		info.Title.Should().Be("Test Title");
		info.Description.Should().Be("First line\nSecond line");
		filtered.Should().HaveCount(2);
	}

	[Test]
	public void Parser_returns_empty_info_when_no_directives()
	{
		var lines = new[]
		{
			"graph TD",
			"A --> B",
		};

		var (info, filtered) = Mermaider.Parsing.AccessibilityParser.Extract(lines);

		info.Title.Should().BeNull();
		info.Description.Should().BeNull();
		info.HasContent.Should().BeFalse();
		filtered.Should().HaveCount(2);
	}
}
