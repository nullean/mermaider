using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

public class PieRendererTests
{
	private const string SimplePie = """
		pie
		title Pet Adoption
		"Dogs" : 386
		"Cats" : 85
		"Rats" : 15
		""";

	[Test]
	public void Renders_valid_svg()
	{
		var svg = MermaidRenderer.RenderSvg(SimplePie);

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Contains_title()
	{
		var svg = MermaidRenderer.RenderSvg(SimplePie);

		svg.Should().Contain("Pet Adoption");
	}

	[Test]
	public void Contains_slice_paths()
	{
		var svg = MermaidRenderer.RenderSvg(SimplePie);

		svg.Should().Contain("<path d=\"M");
	}

	[Test]
	public void Contains_legend_entries()
	{
		var svg = MermaidRenderer.RenderSvg(SimplePie);

		svg.Should().Contain("Dogs");
		svg.Should().Contain("Cats");
		svg.Should().Contain("Rats");
	}

	[Test]
	public void Contains_percentage_labels()
	{
		var svg = MermaidRenderer.RenderSvg(SimplePie);

		svg.Should().Contain("%");
	}

	[Test]
	public void ShowData_includes_values_in_legend()
	{
		var svg = MermaidRenderer.RenderSvg("""
			pie showData
			"A" : 30
			"B" : 70
			""");

		svg.Should().Contain("(30)");
		svg.Should().Contain("(70)");
	}

	[Test]
	public void Renders_single_slice_as_circle()
	{
		var svg = MermaidRenderer.RenderSvg("""
			pie
			"Only" : 100
			""");

		svg.Should().Contain("<circle");
	}

	[Test]
	public void Renders_without_title()
	{
		var svg = MermaidRenderer.RenderSvg("""
			pie
			"A" : 50
			"B" : 50
			""");

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Respects_custom_colors()
	{
		var svg = MermaidRenderer.RenderSvg(SimplePie, new()
		{
			Bg = "#1e1e2e",
			Fg = "#cdd6f4",
		});

		svg.Should().Contain("--bg:#1e1e2e");
		svg.Should().Contain("--fg:#cdd6f4");
	}
}
