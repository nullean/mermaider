using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

/// <summary>Classes with a modifier (<c>&lt;&lt;abstract&gt;&gt;</c>, <c>&lt;&lt;interface&gt;&gt;</c>, …) are coloured by that modifier.</summary>
public class ClassAnnotationColourTests
{
	private const string Source = """
		classDiagram
		  class Shape {
		    <<abstract>>
		    +area() double
		  }
		  class Base {
		    <<abstract>>
		    +id int
		  }
		  class Drawable {
		    <<interface>>
		    +draw() void
		  }
		  class Plain {
		    +name String
		  }
		""";

	private static string Border(string svg, string id)
	{
		var start = svg.IndexOf($"<g class=\"class-node\" data-id=\"{id}\"", StringComparison.Ordinal);
		start.Should().BeGreaterThan(0, $"class {id} should be rendered");
		var box = svg[start..svg.IndexOf("</g>", start, StringComparison.Ordinal)];
		return Regex.Match(box, "<rect [^>]*fill=\"none\" stroke=\"([^\"]+)\"").Groups[1].Value;
	}

	private static string AnnotationFill(string svg, string id)
	{
		var start = svg.IndexOf($"<g class=\"class-node\" data-id=\"{id}\"", StringComparison.Ordinal);
		var box = svg[start..svg.IndexOf("</g>", start, StringComparison.Ordinal)];
		return Regex.Match(box, "font-style=\"italic\" fill=\"([^\"]+)\">&lt;&lt;").Groups[1].Value;
	}

	[Test]
	public void The_same_modifier_gives_the_same_box_colour_and_different_modifiers_differ()
	{
		var svg = MermaidRenderer.RenderSvg(Source);

		Border(svg, "Shape").Should().Be(Border(svg, "Base"));
		Border(svg, "Drawable").Should().NotBe(Border(svg, "Shape"));
		Border(svg, "Plain").Should().NotBe(Border(svg, "Shape")).And.NotBe(Border(svg, "Drawable"), "an unrelated class never takes a modifier's colour");
	}

	[Test]
	public void The_modifier_text_is_drawn_in_the_box_border_colour()
	{
		var svg = MermaidRenderer.RenderSvg(Source);

		AnnotationFill(svg, "Shape").Should().Be(Border(svg, "Shape"));
		AnnotationFill(svg, "Drawable").Should().Be(Border(svg, "Drawable"));
	}

	[Test]
	public void A_modifier_keeps_its_colour_in_other_diagrams()
	{
		var other = MermaidRenderer.RenderSvg("classDiagram\n  class X {\n    <<abstract>>\n    +a int\n  }\n  class Y {\n    +b int\n  }");

		Border(other, "X").Should().Be(Border(MermaidRenderer.RenderSvg(Source), "Shape"));
	}

	[Test]
	public void Modifier_colours_are_hashed_into_the_palette_and_never_use_the_default_slot()
	{
		var svg = MermaidRenderer.RenderSvg(Source);
		var plainFirst = MermaidRenderer.RenderSvg("classDiagram\n  class P {\n    +a int\n  }");

		foreach (var id in new[] { "Shape", "Base", "Drawable" })
			Border(svg, id).Should().NotBe(Border(plainFirst, "P"), "slot 0 is the default box colour, reserved for unannotated classes");
	}

	[Test]
	public void A_declared_lollipop_target_keeps_its_box_next_to_the_lollipop_like_mermaidjs()
	{
		var svg = MermaidRenderer.RenderSvg("classDiagram\n  class Drawable {\n    <<interface>>\n    +draw() void\n  }\n  class Circle {\n    +area() double\n  }\n  Circle --() Drawable");

		svg.Should().Contain("<g class=\"class-node\" data-id=\"Drawable\"", "the declared class keeps its box and members");
		svg.Should().Contain("class-node lollipop\" data-id=\"Drawable__lollipop\"", "the lollipop is its own node");
		svg.Should().Contain("draw");
	}

	[Test]
	public void A_lollipop_target_without_declared_members_is_just_the_circle()
	{
		var svg = MermaidRenderer.RenderSvg("classDiagram\n  class Circle {\n    +area() double\n  }\n  Circle --() Plugin");

		svg.Should().Contain("class-node lollipop\" data-id=\"Plugin\"");
		svg.Should().NotContain("Plugin__lollipop");
	}
}
