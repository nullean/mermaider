using AwesomeAssertions;
using Mermaider.Models;

namespace Mermaider.Tests.Rendering;

public class TreeViewRendererTests
{
	private const string BasicTree = """
		treeView-beta
		    project/
		        src/
		            index.js
		        README.md
		""";

	[Test]
	public void Renders_valid_svg()
	{
		var svg = MermaidRenderer.RenderSvg(BasicTree);

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Contains_role_description()
	{
		var svg = MermaidRenderer.RenderSvg("""
			treeView-beta
			accTitle: Tree
			    src/
			        main.ts
			""");

		svg.Should().Contain("aria-roledescription=\"tree view\"");
	}

	[Test]
	public void Contains_node_labels()
	{
		var svg = MermaidRenderer.RenderSvg(BasicTree);

		svg.Should().Contain("project");
		svg.Should().Contain("src");
		svg.Should().Contain("index.js");
		svg.Should().Contain("README.md");
	}

	[Test]
	public void Directory_labels_are_bold()
	{
		var svg = MermaidRenderer.RenderSvg(BasicTree);

		svg.Should().Contain("font-weight=\"600\"");
	}

	[Test]
	public void File_labels_are_normal_weight()
	{
		var svg = MermaidRenderer.RenderSvg("""
			treeView-beta
			    file.txt
			""");

		svg.Should().Contain("font-weight=\"400\"");
	}

	[Test]
	public void Contains_connector_lines()
	{
		var svg = MermaidRenderer.RenderSvg(BasicTree);

		svg.Should().Contain("class=\"treeview-branches\" fill=\"none\" stroke=\"var(--_line)\"");
		svg.Should().Contain("<path d=\"M");
	}

	[Test]
	public void Default_icons_are_inline_family_glyphs()
	{
		var svg = MermaidRenderer.RenderSvg(BasicTree);

		svg.Should().Contain("class=\"treeview-icon\"");
		svg.Should().NotContain("<image", "default folder / file icons are drawn inline so they take the family colour");
	}

	[Test]
	public void Explicit_icon_renders()
	{
		var svg = MermaidRenderer.RenderSvg("""
			treeView-beta
			    app.ts icon(file:code)
			""");

		svg.Should().Contain("data:image/svg+xml;base64,");
	}

	[Test]
	public void Suppressed_icon_does_not_render()
	{
		var svg = MermaidRenderer.RenderSvg("""
			treeView-beta
			    hidden.txt icon(none)
			""");

		svg.Should().NotContain("<image");
	}

	[Test]
	public void Highlight_renders_accent_fill()
	{
		var svg = MermaidRenderer.RenderSvg("""
			treeView-beta
			    important.ts :::highlight
			""");

		svg.Should().Contain($"fill=\"{Theming.ColorFamily.Accent().Tint(2)}\"");
		svg.Should().Contain($"stroke=\"{Theming.ColorFamily.Accent().Edge}\"");
	}

	[Test]
	public void Custom_class_applied_to_group()
	{
		var svg = MermaidRenderer.RenderSvg("""
			treeView-beta
			    special.ts :::custom-class
			""");

		svg.Should().Contain("class=\"treeview-node custom-class\"");
	}

	[Test]
	public void Description_renders_as_meta_text()
	{
		var svg = MermaidRenderer.RenderSvg("""
			treeView-beta
			    main.ts ## entry point
			""");

		svg.Should().Contain("entry point");
		svg.Should().MatchRegex("font-size=\"var\\(--fs-xs[^\"]*\" font-weight=\"400\" fill=\"var\\(--_text-muted\\)\"[^>]*>entry point<");
	}

	[Test]
	public void Uses_theme_text_variable()
	{
		var svg = MermaidRenderer.RenderSvg(BasicTree);

		svg.Should().Contain("var(--_text)");
	}

	[Test]
	public void Strict_mode_rejects_unknown_class()
	{
		var act = () => MermaidRenderer.RenderSvg(
			"""
			treeView-beta
			    file.txt :::unknown-class
			""",
			new RenderOptions
			{
				Strict = new StrictStylingOptions
				{
					Mode = StrictStylingMode.Block,
					AllowedClasses = [new DiagramClass { Name = "highlight" }],
				}
			});

		act.Should().Throw<MermaidParseException>()
			.WithMessage("*unknown*class*unknown-class*");
	}

	[Test]
	public void Strict_mode_allows_known_class()
	{
		var svg = MermaidRenderer.RenderSvg(
			"""
			treeView-beta
			    file.txt :::highlight
			""",
			new RenderOptions
			{
				Strict = new StrictStylingOptions
				{
					AllowedClasses = [new DiagramClass { Name = "highlight" }],
				}
			});

		svg.Should().Contain("highlight");
	}

	[Test]
	public void Box_drawing_renders_correctly()
	{
		var svg = MermaidRenderer.RenderSvg("""
			treeView-beta
			├── src/
			│   └── main.ts
			└── README.md
			""");

		svg.Should().StartWith("<svg");
		svg.Should().Contain("src");
		svg.Should().Contain("main.ts");
		svg.Should().Contain("README.md");
	}

	[Test]
	public void Theming_respects_render_options()
	{
		var svg = MermaidRenderer.RenderSvg(BasicTree, new RenderOptions
		{
			Bg = "#1E1E2E",
			Fg = "#CDD6F4",
		});

		svg.Should().Contain("--bg:#1E1E2E");
		svg.Should().Contain("--fg:#CDD6F4");
	}

	[Test]
	public void Transparent_option_omits_background()
	{
		var svg = MermaidRenderer.RenderSvg(BasicTree, new RenderOptions { Transparent = true });

		svg.Should().NotContain("background:var(--bg)");
	}

	[Test]
	public void Non_transparent_includes_background()
	{
		var svg = MermaidRenderer.RenderSvg(BasicTree, new RenderOptions { Transparent = false });

		svg.Should().Contain("background:var(--bg)");
	}
}
