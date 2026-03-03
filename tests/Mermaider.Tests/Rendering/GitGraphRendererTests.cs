using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

public class GitGraphRendererTests
{
	private const string BasicGitGraph = """
		gitGraph
		commit
		commit
		branch develop
		checkout develop
		commit
		commit
		checkout main
		merge develop
		commit
		""";

	[Test]
	public void Renders_valid_svg()
	{
		var svg = MermaidRenderer.RenderSvg(BasicGitGraph);

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Contains_branch_labels()
	{
		var svg = MermaidRenderer.RenderSvg(BasicGitGraph);

		svg.Should().Contain("main");
		svg.Should().Contain("develop");
	}

	[Test]
	public void Contains_commit_circles()
	{
		var svg = MermaidRenderer.RenderSvg(BasicGitGraph);

		svg.Should().Contain("<circle");
	}

	[Test]
	public void Contains_merge_links()
	{
		var svg = MermaidRenderer.RenderSvg(BasicGitGraph);

		svg.Should().Contain("<path d=\"M");
	}

	[Test]
	public void Renders_commit_with_tag()
	{
		var svg = MermaidRenderer.RenderSvg("""
			gitGraph
			commit tag: "v1.0"
			commit
			""");

		svg.Should().Contain("v1.0");
	}

	[Test]
	public void Renders_highlight_commit()
	{
		var svg = MermaidRenderer.RenderSvg("""
			gitGraph
			commit type: HIGHLIGHT
			""");

		svg.Should().Contain("<rect");
	}

	[Test]
	public void Renders_reverse_commit()
	{
		var svg = MermaidRenderer.RenderSvg("""
			gitGraph
			commit type: REVERSE
			""");

		svg.Should().Contain("<line");
	}

	[Test]
	public void Renders_custom_commit_id()
	{
		var svg = MermaidRenderer.RenderSvg("""
			gitGraph
			commit id: "init"
			commit id: "feat"
			""");

		svg.Should().Contain("init");
		svg.Should().Contain("feat");
	}
}
