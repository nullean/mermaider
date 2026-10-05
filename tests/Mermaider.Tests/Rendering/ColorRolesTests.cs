using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Rendering;
using Mermaider.Theming;

namespace Mermaider.Tests.Rendering;

/// <summary>Semantic colour roles (success, failure, warning, info) and the role-aware automatic palette.</summary>
public class ColorRolesTests
{
	/// <summary>The gradient start stage of a node painted in <paramref name="color"/> (the default Quiet preset).</summary>
	private static string NodeTop(string color) => new ColorFamily("x", color).Top;

	private static double Hue(string hex)
	{
		ColorUtils.TryHueSaturation(hex, out var hue, out _).Should().BeTrue();
		return hue;
	}

	[Test]
	public void Default_auto_palette_never_uses_a_success_failure_or_warning_hue()
	{
		var colors = Themes.Default;
		var auto = colors.AutoPalette();

		foreach (var role in new[] { ColorRole.Success, ColorRole.Failure, ColorRole.Warning })
		{
			var reserved = Hue(colors.RoleColor(role));
			foreach (var c in auto.Where(c => ColorUtils.TryHueSaturation(c, out _, out var s) && s > 0.12))
				ColorUtils.HueDistance(Hue(c), reserved).Should().BeGreaterThan(DiagramColors.RoleHueWindow, $"{c} is too close to the {role} role");
		}

		auto.Should().Contain(CategoricalPalette.Blue).And.Contain(CategoricalPalette.Orange).And.Contain(CategoricalPalette.Teal).And.Contain(CategoricalPalette.Purple);
		auto.Should().NotContain(CategoricalPalette.Red).And.NotContain(CategoricalPalette.Green).And.NotContain(CategoricalPalette.Yellow);
	}

	[Test]
	public void A_custom_role_colour_reserves_its_hue()
	{
		var colors = Themes.Default with { Failure = CategoricalPalette.Purple };

		colors.AutoPalette().Should().NotContain(CategoricalPalette.Purple);
		colors.AutoPalette().Should().Contain(CategoricalPalette.Red, "the default failure red is free once failure is purple");
	}

	[Test]
	public void A_custom_data_palette_is_filtered_and_falls_back_when_nothing_is_left()
	{
		var mixed = Themes.Default with { DataPalette = ["#ff0000", "#0000ff", "#00ff00"] };
		mixed.AutoPalette().Should().BeEquivalentTo(["#0000ff"]);

		var onlyRoles = Themes.Default with { DataPalette = ["#ff0000", "#00ff00"] };
		onlyRoles.AutoPalette().Should().BeEquivalentTo(["#ff0000", "#00ff00"], "an unusable palette is used as given instead of drawing nothing");
	}

	[Test]
	public void Dark_backgrounds_get_brighter_role_defaults()
	{
		var dark = new DiagramColors { Bg = "#1a1b26", Fg = "#c0caf5" };

		dark.RoleColor(ColorRole.Success).Should().NotBe(Themes.Default.RoleColor(ColorRole.Success));
	}

	[Test]
	public void Role_classes_colour_a_node_with_the_role_tint_and_border()
	{
		var svg = MermaidRenderer.RenderSvg("flowchart TD\n  A:::success --> B:::failure --> C");

		svg.Should().Contain(NodeTop(CategoricalPalette.Green));
		svg.Should().Contain(VisualLanguage.Border(CategoricalPalette.Green));
		svg.Should().Contain(NodeTop(CategoricalPalette.Red));
	}

	[Test]
	public void Role_colours_can_be_set_through_render_options()
	{
		var svg = MermaidRenderer.RenderSvg("flowchart TD\n  A:::warning --> B", new RenderOptions { Warning = "#ff8800" });

		svg.Should().Contain(NodeTop("#ff8800"));
	}

	[Test]
	public void A_classDef_fill_beats_the_role()
	{
		var svg = MermaidRenderer.RenderSvg("flowchart TD\n  classDef success fill:#123456\n  A:::success --> B");

		svg.Should().Contain("fill=\"#123456\"");
		svg.Should().NotContain(NodeTop(CategoricalPalette.Green));
	}

	[Test]
	public void An_unsafe_role_colour_is_ignored()
	{
		var svg = MermaidRenderer.RenderSvg(
			"flowchart TD\n  A:::success --> B",
			new RenderOptions { Success = "red\" onload=\"alert(1)" });

		svg.Should().NotContain("onload");
		svg.Should().Contain(NodeTop(CategoricalPalette.Green));
	}

	[Test]
	public void Strict_mode_does_not_let_the_diagram_pick_role_colours_unless_the_class_is_allowed()
	{
		var strict = new RenderOptions { Strict = new StrictStylingOptions { AllowedClasses = [new DiagramClass { Name = "external" }] } };

		var svg = MermaidRenderer.RenderSvg("flowchart TD\n  A:::success --> B", strict);

		svg.Should().NotContain(NodeTop(CategoricalPalette.Green), "the class was not allow-listed, so it was stripped");
	}

	[Test]
	public void The_default_role_is_the_first_colour_boxes_use()
	{
		Themes.Default.RoleColor(ColorRole.Default).Should().Be(CategoricalPalette.Blue);
		Themes.Default.AutoPalette()[0].Should().Be(CategoricalPalette.Blue);

		var custom = Themes.Default with { Default = CategoricalPalette.Teal };
		custom.AutoPalette()[0].Should().Be(CategoricalPalette.Teal);
		custom.AutoPalette().Count(c => c == CategoricalPalette.Teal).Should().Be(1, "the default is not repeated further down the palette");
		custom.AutoPalette().Should().NotContain(CategoricalPalette.LightTeal, "a near-duplicate of the default is skipped too");
	}

	[Test]
	public void The_default_role_colours_a_plain_flowchart_and_can_come_from_render_options()
	{
		var svg = MermaidRenderer.RenderSvg("flowchart TD\n  A --> B", new RenderOptions { Default = "#7f3fbf" });

		svg.Should().Contain(NodeTop("#7f3fbf"));
	}

	[Test]
	public void An_unsafe_default_colour_is_ignored()
	{
		var svg = MermaidRenderer.RenderSvg("flowchart TD\n  A --> B", new RenderOptions { Default = "red\" onload=\"x" });

		svg.Should().NotContain("onload");
		svg.Should().Contain(NodeTop(CategoricalPalette.Blue));
	}
}
