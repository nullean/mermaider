using AwesomeAssertions;
using Mermaider.Models;

namespace Mermaider.Tests;

public class StrictStylingTests
{
	// Strip mode (the new default) — disallowed directives are ignored and rendering continues.
	private static readonly StrictStylingOptions DefaultStrict = new()
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

	// Block mode — preserves the original throw-on-first-violation behavior.
	private static readonly StrictStylingOptions BlockStrict = DefaultStrict with { Mode = StrictStylingMode.Block };

	[Test]
	public void Rejects_classDef_directive()
	{
		var input = """
			graph TD
			  classDef red fill:#f00
			  A --> B
			""";
		var options = new RenderOptions { Strict = BlockStrict };

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
		var options = new RenderOptions { Strict = BlockStrict };

		var act = () => MermaidRenderer.RenderSvg(input, options);

		act.Should().Throw<MermaidParseException>()
			.WithMessage("*style*not allowed*");
	}

	[Test]
	public void Rejects_unknown_class_via_shorthand()
	{
		var input = """
			graph TD
			  A:::unknown --> B
			""";
		var options = new RenderOptions { Strict = BlockStrict };

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
		var options = new RenderOptions { Strict = BlockStrict };

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
	public void Allows_unknown_class_in_strip_mode()
	{
		var input = """
			graph TD
			  A:::whatever --> B
			""";
		var options = new RenderOptions
		{
			Strict = new StrictStylingOptions { AllowedClasses = [] }
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

	// ========================================================================
	// A1 — source-authored theme / themeVariables rejected under strict mode
	// ========================================================================

	[Test]
	public void Rejects_init_themeVariables_in_strict_mode()
	{
		var input = """
			%%{init: {"themeVariables": {"primaryColor": "#ff0000"}}}%%
			graph TD
			  A --> B
			""";
		var options = new RenderOptions { Strict = BlockStrict };

		var act = () => MermaidRenderer.RenderSvg(input, options);

		act.Should().Throw<MermaidParseException>()
			.WithMessage("*Strict mode*theme*");
	}

	[Test]
	public void Rejects_init_theme_in_strict_mode()
	{
		var input = """
			%%{init: {"theme": "dark"}}%%
			graph TD
			  A --> B
			""";
		var options = new RenderOptions { Strict = BlockStrict };

		var act = () => MermaidRenderer.RenderSvg(input, options);

		act.Should().Throw<MermaidParseException>()
			.WithMessage("*Strict mode*theme*");
	}

	[Test]
	public void Allows_title_only_frontmatter_in_strict_mode()
	{
		var input = """
			---
			title: My Diagram
			---
			graph TD
			  A --> B
			""";
		var options = new RenderOptions { Strict = DefaultStrict };

		var act = () => MermaidRenderer.RenderSvg(input, options);

		act.Should().NotThrow();
	}

	// ========================================================================
	// A2 — linkStyle and Update*Style rejected under strict mode
	// ========================================================================

	[Test]
	public void Rejects_linkStyle_in_strict_mode()
	{
		var input = """
			graph TD
			  A --> B
			  linkStyle 0 stroke:#f00
			""";
		var options = new RenderOptions { Strict = BlockStrict };

		var act = () => MermaidRenderer.RenderSvg(input, options);

		act.Should().Throw<MermaidParseException>()
			.WithMessage("*linkStyle*not allowed*");
	}

	[Test]
	public void Rejects_UpdateElementStyle_in_strict_mode()
	{
		var input = """
			C4Context
			Person(user, "User")
			UpdateElementStyle(user, $fontColor="red")
			""";
		var options = new RenderOptions { Strict = BlockStrict };

		var act = () => MermaidRenderer.RenderSvg(input, options);

		act.Should().Throw<MermaidParseException>()
			.WithMessage("*Update*Style*not allowed*");
	}

	[Test]
	public void Rejects_UpdateRelStyle_in_strict_mode()
	{
		var input = """
			C4Context
			Person(user, "User")
			UpdateRelStyle(user, user, $lineColor="red")
			""";
		var options = new RenderOptions { Strict = BlockStrict };

		var act = () => MermaidRenderer.RenderSvg(input, options);

		act.Should().Throw<MermaidParseException>()
			.WithMessage("*Update*Style*not allowed*");
	}

	[Test]
	public void Does_not_reject_linkStyle_without_strict()
	{
		// Without strict mode, linkStyle is parsed/silently handled or ignored
		var input = """
			graph TD
			  A --> B
			  linkStyle 0 stroke:#f00
			""";

		var act = () => MermaidRenderer.RenderSvg(input);

		act.Should().NotThrow();
	}

	[Test]
	public void Rejects_every_class_name_outside_the_positive_identifier_grammar()
	{
		string[] invalidNames =
		[
			"", "1starts-with-digit", "contains space", "contains.dot", "contains:colon",
			"quote'", "double\"quote", "close}", "comma,name", "slash/name", "é",
		];

		foreach (var name in invalidNames)
		{
			var options = new RenderOptions
			{
				Strict = new StrictStylingOptions
				{
					AllowedClasses = [new DiagramClass { Name = name }],
				},
			};
			var act = () => MermaidRenderer.RenderSvg("graph TD\nA --> B", options);

			act.Should().ThrowExactly<MermaidParseException>($"'{name}' is outside the class-name allowlist");
		}
	}

	[Test]
	public void Allows_each_class_name_character_in_the_positive_identifier_grammar()
	{
		string[] validNames = ["a", "_private", "A1", "with-dash", "with_underscore", "Mixed_1-name"];

		foreach (var name in validNames)
		{
			var options = new RenderOptions
			{
				Strict = new StrictStylingOptions
				{
					AllowedClasses = [new DiagramClass { Name = name }],
				},
			};
			var act = () => MermaidRenderer.RenderSvg("graph TD\nA --> B", options);

			act.Should().NotThrow($"'{name}' is inside the class-name allowlist");
		}
	}

	[Test]
	public void Styled_class_requires_fill()
	{
		var options = new RenderOptions
		{
			Strict = new StrictStylingOptions
			{
				AllowedClasses = [new DiagramClass { Name = "invalid", Color = "#fff" }],
			},
		};
		var act = () => MermaidRenderer.RenderSvg("graph TD\nA --> B", options);

		act.Should().ThrowExactly<MermaidParseException>()
			.WithMessage("*must define Fill*");
	}

	[Test]
	public void Rejects_non_hex_value_in_each_strict_class_color_slot()
	{
		var invalidClasses = new (string Property, DiagramClass Class)[]
		{
			(nameof(DiagramClass.Fill), new DiagramClass { Name = "invalid", Fill = "red" }),
			(nameof(DiagramClass.Stroke), new DiagramClass { Name = "invalid", Fill = "#fff", Stroke = "url(#paint)" }),
			(nameof(DiagramClass.Color), new DiagramClass { Name = "invalid", Fill = "#fff", Color = "#12" }),
			(nameof(DiagramClass.DarkFill), new DiagramClass { Name = "invalid", Fill = "#fff", DarkFill = "#12345" }),
			(nameof(DiagramClass.DarkStroke), new DiagramClass { Name = "invalid", Fill = "#fff", DarkStroke = "#ggg" }),
			(nameof(DiagramClass.DarkColor), new DiagramClass { Name = "invalid", Fill = "#fff", DarkColor = "#fff;display:none" }),
		};

		foreach (var (property, invalidClass) in invalidClasses)
		{
			var options = new RenderOptions
			{
				Strict = new StrictStylingOptions { AllowedClasses = [invalidClass] },
			};
			var act = () => MermaidRenderer.RenderSvg("graph TD\nA --> B", options);

			act.Should().ThrowExactly<MermaidParseException>()
				.WithMessage($"*{property}*hexadecimal color*");
		}
	}

	// ========================================================================
	// Strip mode — default behavior; disallowed directives are dropped and
	// reported via OnStripped; rendering continues normally.
	// ========================================================================

	[Test]
	public void Strip_classDef_renders_and_reports_callback()
	{
		var input = """
			graph TD
			  classDef red fill:#f00
			  A --> B
			""";
		var stripped = new List<StrictStylingViolation>();
		var options = new RenderOptions
		{
			Strict = DefaultStrict with { OnStripped = stripped.Add }
		};

		var svg = MermaidRenderer.RenderSvg(input, options);

		svg.Should().Contain("</svg>");
		stripped.Should().ContainSingle(v =>
			v.Kind == StrictStylingViolationKind.ClassDefDirective &&
			v.Line == 2);
	}

	[Test]
	public void Strip_style_directive_renders_and_reports_callback()
	{
		var input = """
			graph TD
			  A --> B
			  style A fill:#f00
			""";
		var stripped = new List<StrictStylingViolation>();
		var options = new RenderOptions
		{
			Strict = DefaultStrict with { OnStripped = stripped.Add }
		};

		var svg = MermaidRenderer.RenderSvg(input, options);

		svg.Should().Contain("</svg>");
		stripped.Should().ContainSingle(v =>
			v.Kind == StrictStylingViolationKind.StyleDirective &&
			v.Line == 3);
	}

	[Test]
	public void Strip_linkStyle_renders_without_source_edge_color_and_reports_callback()
	{
		var input = """
			graph TD
			  A --> B
			  linkStyle 0 stroke:#f00,stroke-width:4px
			""";
		var stripped = new List<StrictStylingViolation>();
		var options = new RenderOptions
		{
			Strict = DefaultStrict with { OnStripped = stripped.Add }
		};

		var svg = MermaidRenderer.RenderSvg(input, options);

		svg.Should().Contain("</svg>");
		// Source linkStyle must not appear as inline stroke on the edge
		svg.Should().NotContain("#f00");
		stripped.Should().ContainSingle(v =>
			v.Kind == StrictStylingViolationKind.LinkStyleDirective &&
			v.Line == 3);
	}

	[Test]
	public void Strip_unknown_class_shorthand_renders_and_reports_callback()
	{
		var input = """
			graph TD
			  A:::whatever --> B
			""";
		var stripped = new List<StrictStylingViolation>();
		var options = new RenderOptions
		{
			Strict = DefaultStrict with { OnStripped = stripped.Add }
		};

		var svg = MermaidRenderer.RenderSvg(input, options);

		svg.Should().Contain("</svg>");
		stripped.Should().ContainSingle(v =>
			v.Kind == StrictStylingViolationKind.UnknownClassReference &&
			v.Source == "whatever");
	}

	[Test]
	public void Strip_unknown_class_directive_renders_and_reports_callback()
	{
		var input = """
			graph TD
			  A --> B
			  class A whatever
			""";
		var stripped = new List<StrictStylingViolation>();
		var options = new RenderOptions
		{
			Strict = DefaultStrict with { OnStripped = stripped.Add }
		};

		var svg = MermaidRenderer.RenderSvg(input, options);

		svg.Should().Contain("</svg>");
		stripped.Should().ContainSingle(v =>
			v.Kind == StrictStylingViolationKind.UnknownClassReference &&
			v.Source == "whatever");
	}

	[Test]
	public void Strip_theme_override_renders_with_host_colors_and_reports_callback()
	{
		var input = """
			%%{init: {"theme": "dark"}}%%
			graph TD
			  A --> B
			""";
		var stripped = new List<StrictStylingViolation>();
		var options = new RenderOptions
		{
			// Provide explicit host colors so we can assert they win over the source theme.
			Bg = "#123456",
			Strict = DefaultStrict with { OnStripped = stripped.Add }
		};

		var svg = MermaidRenderer.RenderSvg(input, options);

		svg.Should().Contain("</svg>");
		// Host background color must appear; dark theme defaults must NOT override it.
		svg.Should().Contain("#123456");
		stripped.Should().ContainSingle(v =>
			v.Kind == StrictStylingViolationKind.ThemeOverride &&
			v.Line == 0);
	}

	[Test]
	public void Strip_mode_no_callback_renders_silently_without_throwing()
	{
		// Sanity: strip mode with no OnStripped callback must not throw.
		var input = """
			graph TD
			  classDef red fill:#f00
			  A:::unknown --> B
			  linkStyle 0 stroke:#f00
			""";
		var options = new RenderOptions { Strict = DefaultStrict };

		var act = () => MermaidRenderer.RenderSvg(input, options);

		act.Should().NotThrow();
	}

	// ========================================================================
	// Block mode — AllowedClasses validation always throws in both modes.
	// ========================================================================

	[Test]
	public void AllowedClasses_validation_throws_in_strip_mode_too()
	{
		// Host-config errors (bad AllowedClasses) must always throw, regardless of Mode.
		var options = new RenderOptions
		{
			Strict = new StrictStylingOptions
			{
				Mode = StrictStylingMode.Strip,
				AllowedClasses = [new DiagramClass { Name = "invalid", Color = "#fff" }],
			}
		};

		var act = () => MermaidRenderer.RenderSvg("graph TD\nA --> B", options);

		act.Should().ThrowExactly<MermaidParseException>()
			.WithMessage("*must define Fill*");
	}

	// ========================================================================
	// SVG-sanitizer callback (OnSanitized on RenderOptions)
	// ========================================================================

	[Test]
	public void OnSanitized_not_called_when_no_violations()
	{
		var called = false;
		var options = new RenderOptions
		{
			OnSanitized = _ => called = true
		};

		_ = MermaidRenderer.RenderSvg("graph TD\n  A --> B", options);

		called.Should().BeFalse("a clean diagram produces no sanitizer violations");
	}
}
