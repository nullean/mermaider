using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

public class DiagramPreprocessorTests
{
	// ========================================================================
	// Frontmatter: title extraction
	// ========================================================================

	[Test]
	public void ExtractsTitleFromFrontmatter()
	{
		var input = """
			---
			title: My Diagram
			---
			graph LR
			    A --> B
			""";

		var (cleaned, metadata) = DiagramPreprocessor.Process(input);

		metadata.Title.Should().Be("My Diagram");
		cleaned.Should().NotContain("---");
		cleaned.Should().Contain("graph LR");
	}

	[Test]
	public void ExtractsQuotedTitleFromFrontmatter()
	{
		var input = "---\ntitle: \"My Quoted Title\"\n---\ngraph TD\n  A --> B";

		var (_, metadata) = DiagramPreprocessor.Process(input);

		metadata.Title.Should().Be("My Quoted Title");
	}

	[Test]
	public void ExtractsSingleQuotedTitleFromFrontmatter()
	{
		var input = "---\ntitle: 'Single Quoted'\n---\ngraph TD\n  A --> B";

		var (_, metadata) = DiagramPreprocessor.Process(input);

		metadata.Title.Should().Be("Single Quoted");
	}

	[Test]
	public void StripsFrontmatterFromOutput()
	{
		var input = "---\ntitle: Test\n---\ngraph TD\n  A --> B";

		var (cleaned, _) = DiagramPreprocessor.Process(input);

		cleaned.Should().NotContain("title:");
		cleaned.Should().NotContain("---");
		cleaned.Trim().Should().StartWith("graph TD");
	}

	[Test]
	public void HandlesNoFrontmatter()
	{
		var input = "graph TD\n  A --> B";

		var (cleaned, metadata) = DiagramPreprocessor.Process(input);

		metadata.Title.Should().BeNull();
		cleaned.Should().Be(input);
	}

	[Test]
	public void HandlesUnclosedFrontmatter()
	{
		var input = "---\ntitle: No Closing\ngraph TD\n  A --> B";

		var (cleaned, metadata) = DiagramPreprocessor.Process(input);

		// Unclosed frontmatter should not be stripped
		metadata.Title.Should().BeNull();
		cleaned.Should().Be(input);
	}

	// ========================================================================
	// Init directive: theme extraction
	// ========================================================================

	[Test]
	public void ExtractsThemeFromInitDirective()
	{
		var input = "%%{init: {\"theme\": \"dark\"}}%%\ngraph LR\n  A --> B";

		var (cleaned, metadata) = DiagramPreprocessor.Process(input);

		metadata.Theme.Should().Be("dark");
		cleaned.Should().NotContain("%%{init");
		cleaned.Trim().Should().StartWith("graph LR");
	}

	[Test]
	public void ExtractsThemeVariablesFromInitDirective()
	{
		var input = "%%{init: {\"themeVariables\": {\"primaryColor\": \"#ff0000\", \"lineColor\": \"#00ff00\"}}}%%\ngraph TD\n  A --> B";

		var (_, metadata) = DiagramPreprocessor.Process(input);

		metadata.ThemeVariables.Should().NotBeNull();
		metadata.ThemeVariables!["primaryColor"].Should().Be("#ff0000");
		metadata.ThemeVariables["lineColor"].Should().Be("#00ff00");
	}

	[Test]
	public void ExtractsThemeAndVariablesTogether()
	{
		var input = "%%{init: {\"theme\": \"forest\", \"themeVariables\": {\"primaryColor\": \"#abc\"}}}%%\ngraph TD\n  A --> B";

		var (_, metadata) = DiagramPreprocessor.Process(input);

		metadata.Theme.Should().Be("forest");
		metadata.ThemeVariables.Should().NotBeNull();
		metadata.ThemeVariables!["primaryColor"].Should().Be("#abc");
	}

	[Test]
	public void StripsInitDirectiveFromOutput()
	{
		var input = "%%{init: {\"theme\": \"dark\"}}%%\ngraph TD\n  A --> B";

		var (cleaned, _) = DiagramPreprocessor.Process(input);

		cleaned.Should().NotContain("%%{init");
		cleaned.Should().NotContain("}%%");
	}

	[Test]
	public void HandlesNoInitDirective()
	{
		var input = "graph TD\n  A --> B";

		var (cleaned, metadata) = DiagramPreprocessor.Process(input);

		metadata.Theme.Should().BeNull();
		metadata.ThemeVariables.Should().BeNull();
		cleaned.Should().Be(input);
	}

	[Test]
	public void HandlesMalformedJsonInInit()
	{
		var input = "%%{init: {not valid json}}%%\ngraph TD\n  A --> B";

		var (cleaned, metadata) = DiagramPreprocessor.Process(input);

		// Malformed JSON: directive is still stripped, but no values extracted
		metadata.Theme.Should().BeNull();
		cleaned.Should().NotContain("%%{init");
		cleaned.Trim().Should().StartWith("graph TD");
	}

	// ========================================================================
	// Combined frontmatter + init directive
	// ========================================================================

	[Test]
	public void CombinesFrontmatterAndInitDirective()
	{
		var input = "---\ntitle: My Title\n---\n%%{init: {\"theme\": \"dark\"}}%%\ngraph TD\n  A --> B";

		var (cleaned, metadata) = DiagramPreprocessor.Process(input);

		metadata.Title.Should().Be("My Title");
		metadata.Theme.Should().Be("dark");
		cleaned.Should().NotContain("---");
		cleaned.Should().NotContain("%%{init");
		cleaned.Trim().Should().StartWith("graph TD");
	}

	[Test]
	public void InitTitleOverridesFrontmatterTitle()
	{
		var input = "---\ntitle: Frontmatter Title\n---\n%%{init: {\"title\": \"Init Title\"}}%%\ngraph TD\n  A --> B";

		var (_, metadata) = DiagramPreprocessor.Process(input);

		metadata.Title.Should().Be("Init Title");
	}

	// ========================================================================
	// Empty / whitespace input
	// ========================================================================

	[Test]
	public void HandlesEmptyInput()
	{
		var (cleaned, metadata) = DiagramPreprocessor.Process("");

		metadata.Should().Be(DiagramMetadata.Empty);
		cleaned.Should().Be("");
	}

	[Test]
	public void HandlesWhitespaceOnlyInput()
	{
		var (cleaned, metadata) = DiagramPreprocessor.Process("   \n  \n  ");

		metadata.Should().Be(DiagramMetadata.Empty);
		cleaned.Should().Be("   \n  \n  ");
	}

	// ========================================================================
	// SVG rendering integration
	// ========================================================================

	[Test]
	public void RenderSvgIncludesTitleElement()
	{
		var input = "---\ntitle: My Flow\n---\ngraph TD\n  A --> B";

		var svg = MermaidRenderer.RenderSvg(input);

		svg.Should().Contain("<title>My Flow</title>");
	}

	[Test]
	public void RenderSvgAppliesThemeFromInit()
	{
		var input = "%%{init: {\"theme\": \"dark\"}}%%\ngraph TD\n  A --> B";

		// "dark" is not a recognized built-in theme name, so it should fall back to default.
		// Use a real built-in theme name:
		var inputWithTheme = "%%{init: {\"theme\": \"dracula\"}}%%\ngraph TD\n  A --> B";

		var svg = MermaidRenderer.RenderSvg(inputWithTheme);

		// Dracula theme bg = #282a36
		svg.Should().Contain("--bg:#282a36");
	}

	[Test]
	public void RenderSvgAppliesThemeVariablesFromInit()
	{
		var input = "%%{init: {\"themeVariables\": {\"background\": \"#111111\"}}}%%\ngraph TD\n  A --> B";

		var svg = MermaidRenderer.RenderSvg(input);

		svg.Should().Contain("--bg:#111111");
	}

	[Test]
	public void RenderSvgWithoutMetadataHasNoTitleElement()
	{
		var svg = MermaidRenderer.RenderSvg("graph TD\n  A --> B");

		svg.Should().NotContain("<title>");
	}

	[Test]
	public void ParseStillWorksWithFrontmatter()
	{
		var input = "---\ntitle: Test\n---\ngraph TD\n  A --> B";

		var graph = MermaidRenderer.Parse(input);

		graph.Nodes.Should().ContainKey("A");
		graph.Nodes.Should().ContainKey("B");
	}

	[Test]
	public void ParseStillWorksWithInitDirective()
	{
		var input = "%%{init: {\"theme\": \"dark\"}}%%\ngraph TD\n  A --> B";

		var graph = MermaidRenderer.Parse(input);

		graph.Nodes.Should().ContainKey("A");
		graph.Nodes.Should().ContainKey("B");
	}

	[Test]
	public void RenderSvgEscapesTitleContent()
	{
		var input = "---\ntitle: A <script> & \"test\"\n---\ngraph TD\n  A --> B";

		var svg = MermaidRenderer.RenderSvg(input);

		svg.Should().Contain("<title>A &lt;script&gt; &amp; &quot;test&quot;</title>");
	}

	[Test]
	public void ExplicitRenderOptionsOverrideInitTheme()
	{
		var input = "%%{init: {\"theme\": \"dracula\"}}%%\ngraph TD\n  A --> B";

		var svg = MermaidRenderer.RenderSvg(input, new() { Bg = "#FF0000" });

		// Explicit option should win over init theme
		svg.Should().Contain("--bg:#FF0000");
	}

	[Test]
	public void InitDirectiveWithWhitespaceVariants()
	{
		// Extra whitespace around the JSON
		var input = "%%{init:   {  \"theme\" :  \"nord\"  }  }%%\ngraph TD\n  A --> B";

		var svg = MermaidRenderer.RenderSvg(input);

		// Nord theme bg = #2e3440
		svg.Should().Contain("--bg:#2e3440");
	}
}
