using AwesomeAssertions;
using Mermaider.Models;

namespace Mermaider.Tests;

public class StrictModeTests
{
	private static readonly StrictModeOptions DefaultStrict = new()
	{
		AllowedClasses =
		[
			new DiagramClass
			{
				Name = "success",
				Fill = "#D4EDDA",
				Stroke = "#28A745",
				Color = "#155724"
			},
			new DiagramClass
			{
				Name = "danger",
				Fill = "#F8D7DA",
				Stroke = "#DC3545",
				Color = "#721C24",
				DarkFill = "#2D1B1E",
				DarkStroke = "#E4606D",
				DarkColor = "#F5C6CB"
			},
			new DiagramClass { Name = "external" }
		]
	};

	[Test]
	public void Rejects_classDef_directive()
	{
		var input = """
			graph TD
			  classDef red fill:#f00
			  A --> B
			""";
		var options = new RenderOptions { Strict = DefaultStrict };

		var act = () => MermaidRenderer.RenderSvg(input, options);

		act.Should().Throw<MermaidParseException>()
			.WithMessage("*classDef*not allowed*");
	}

	[Test]
	public void Rejects_style_directive()
	{
		var input = """
			graph TD
			  A --> B
			  style A fill:#f00
			""";
		var options = new RenderOptions { Strict = DefaultStrict };

		var act = () => MermaidRenderer.RenderSvg(input, options);

		act.Should().Throw<MermaidParseException>()
			.WithMessage("*style*not allowed*");
	}

	[Test]
	public void Rejects_linkStyle_directive()
	{
		var input = """
			graph TD
			  A --> B
			  linkStyle 0 stroke:#ff3,stroke-width:4px
			""";
		var options = new RenderOptions { Strict = DefaultStrict };

		var act = () => MermaidRenderer.RenderSvg(input, options);

		act.Should().Throw<MermaidParseException>()
			.WithMessage("*linkStyle*not allowed*");
	}

	[Test]
	public void Rejects_unknown_class_via_shorthand()
	{
		var input = """
			graph TD
			  A:::unknown --> B
			""";
		var options = new RenderOptions { Strict = DefaultStrict };

		var act = () => MermaidRenderer.RenderSvg(input, options);

		act.Should().Throw<MermaidParseException>()
			.WithMessage("*unknown class 'unknown'*");
	}

	[Test]
	public void Rejects_unknown_class_via_class_directive()
	{
		var input = """
			graph TD
			  A --> B
			  class A unknown
			""";
		var options = new RenderOptions { Strict = DefaultStrict };

		var act = () => MermaidRenderer.RenderSvg(input, options);

		act.Should().Throw<MermaidParseException>()
			.WithMessage("*unknown class 'unknown'*");
	}

	[Test]
	public void Allows_known_class_via_shorthand()
	{
		var input = """
			graph TD
			  A:::success --> B:::danger
			""";
		var options = new RenderOptions { Strict = DefaultStrict };

		var svg = MermaidRenderer.RenderSvg(input, options);

		svg.Should().Contain("cls-success");
		svg.Should().Contain("cls-danger");
	}

	[Test]
	public void Emits_light_mode_css_for_allowed_classes()
	{
		var input = """
			graph TD
			  A:::success --> B
			""";
		var options = new RenderOptions { Strict = DefaultStrict };

		var svg = MermaidRenderer.RenderSvg(input, options);

		svg.Should().Contain(".cls-success rect");
		svg.Should().Contain("fill: #D4EDDA");
		svg.Should().Contain("stroke: #28A745");
	}

	[Test]
	public void Emits_dark_mode_media_query()
	{
		var input = """
			graph TD
			  A:::danger --> B
			""";
		var options = new RenderOptions { Strict = DefaultStrict };

		var svg = MermaidRenderer.RenderSvg(input, options);

		svg.Should().Contain("@media (prefers-color-scheme: dark)");
		svg.Should().Contain("#2D1B1E");
		svg.Should().Contain("#E4606D");
	}

	[Test]
	public void Auto_derives_dark_colors_when_not_specified()
	{
		var input = """
			graph TD
			  A:::success --> B
			""";
		var options = new RenderOptions { Strict = DefaultStrict };

		var svg = MermaidRenderer.RenderSvg(input, options);

		svg.Should().Contain("@media (prefers-color-scheme: dark)");
		svg.Should().Contain(".cls-success");
	}

	[Test]
	public void External_class_gets_raw_class_name_without_prefix()
	{
		var input = """
			graph TD
			  A:::external --> B
			""";
		var options = new RenderOptions { Strict = DefaultStrict };

		var svg = MermaidRenderer.RenderSvg(input, options);

		svg.Should().Contain("class=\"node external\"");
		svg.Should().NotContain("cls-external");
		svg.Should().NotContain(".cls-external rect");
	}

	[Test]
	public void Allows_unknown_class_when_RejectUnknownClasses_is_false()
	{
		var input = """
			graph TD
			  A:::whatever --> B
			""";
		var options = new RenderOptions
		{
			Strict = new StrictModeOptions
			{
				AllowedClasses = [],
				RejectUnknownClasses = false
			}
		};

		var svg = MermaidRenderer.RenderSvg(input, options);
		svg.Should().Contain("</svg>");
	}

	[Test]
	public void No_inline_styles_applied_in_strict_mode()
	{
		var input = """
			graph TD
			  A:::success --> B
			""";
		var options = new RenderOptions { Strict = DefaultStrict };

		var svg = MermaidRenderer.RenderSvg(input, options);

		svg.Should().NotContain("fill=\"#D4EDDA\"");
	}

	[Test]
	public void Works_with_state_diagram()
	{
		var input = """
			stateDiagram-v2
			  [*] --> Active
			  Active --> [*]
			""";
		var options = new RenderOptions { Strict = DefaultStrict };

		var svg = MermaidRenderer.RenderSvg(input, options);
		svg.Should().Contain("</svg>");
	}

	[Test]
	public void Works_with_sequence_diagram()
	{
		var input = """
			sequenceDiagram
			  Alice->>Bob: Hello
			""";
		var options = new RenderOptions { Strict = DefaultStrict };

		var svg = MermaidRenderer.RenderSvg(input, options);
		svg.Should().Contain("</svg>");
	}
}
