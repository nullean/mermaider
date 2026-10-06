using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Rendering;
using Mermaider.Theming;

namespace Mermaider.Tests.Rendering;

public class DesignInputsTests
{
	private const string Flow = "graph TD\n  A[Start] --> B[End]";

	[Test]
	public void Null_options_yield_the_defaults()
	{
		var inputs = DesignInputs.From(null);

		_ = inputs.Should().Be(DesignInputs.Default);
		_ = inputs.Spec.Should().BeSameAs(StyleSpec.Quiet);
		_ = inputs.Gradient.Should().BeTrue();
		_ = inputs.Tint.Should().Be(1.0);
		_ = inputs.Elevation.Should().Be(1);
	}

	[Test]
	public void Unset_tint_and_elevation_fall_back_to_defaults()
	{
		var inputs = DesignInputs.From(new RenderOptions());

		_ = inputs.Tint.Should().Be(1.0);
		_ = inputs.Elevation.Should().Be(1);
		_ = inputs.Gradient.Should().BeTrue();
	}

	[Test]
	[Arguments(0.1, 0.5)]
	[Arguments(0.5, 0.5)]
	[Arguments(0.75, 0.75)]
	[Arguments(1.5, 1.5)]
	[Arguments(9.0, 1.5)]
	[Arguments(-3.0, 0.5)]
	public void Tint_is_clamped_to_half_and_one_and_a_half(double requested, double expected) =>
		DesignInputs.From(new RenderOptions { Tint = requested }).Tint.Should().Be(expected);

	[Test]
	[Arguments(double.NaN)]
	[Arguments(double.PositiveInfinity)]
	[Arguments(double.NegativeInfinity)]
	public void Non_finite_tint_falls_back_to_one(double requested) =>
		DesignInputs.From(new RenderOptions { Tint = requested }).Tint.Should().Be(1.0);

	[Test]
	[Arguments(-5, 0)]
	[Arguments(0, 0)]
	[Arguments(1, 1)]
	[Arguments(2, 2)]
	[Arguments(7, 2)]
	public void Elevation_is_clamped_to_zero_through_two(int requested, int expected) =>
		DesignInputs.From(new RenderOptions { Elevation = requested }).Elevation.Should().Be(expected);

	[Test]
	public void Gradient_flag_is_carried_through() =>
		DesignInputs.From(new RenderOptions { Gradient = false }).Gradient.Should().BeFalse();

	[Test]
	[Arguments(DiagramStyle.Quiet)]
	[Arguments(DiagramStyle.Blueprint)]
	[Arguments(DiagramStyle.Tonal)]
	public void Style_round_trips_to_the_matching_spec(DiagramStyle style)
	{
		var spec = DesignInputs.From(new RenderOptions { Style = style }).Spec;

		_ = spec.Style.Should().Be(style);
		_ = spec.Should().BeSameAs(StyleSpec.For(style));
	}

	[Test]
	public void Undefined_style_value_falls_back_to_quiet() =>
		DesignInputs.From(new RenderOptions { Style = (DiagramStyle)42 }).Spec.Should().BeSameAs(StyleSpec.Quiet);

	[Test]
	[Arguments(DiagramStyle.Quiet)]
	[Arguments(DiagramStyle.Blueprint)]
	[Arguments(DiagramStyle.Tonal)]
	public void Normalized_configuration_carries_the_preset_and_its_bend_radius(DiagramStyle style)
	{
		var options = new RenderOptions { Style = style, Tint = 3, Elevation = -1 };
		var diagram = DiagramPreparationStage.Prepare(Flow, options, ResourceLimits.Default);

		var config = RenderConfigurationNormalizer.Normalize(diagram, options);

		_ = config.Styles.Design.Spec.Style.Should().Be(style);
		_ = config.Styles.Design.Tint.Should().Be(1.5);
		_ = config.Styles.Design.Elevation.Should().Be(0);
		_ = config.EdgeRadius.Should().Be(StyleSpec.For(style).EdgeBendRadius);
	}

	[Test]
	public void Rounded_edges_off_forces_square_bends_in_every_preset()
	{
		var options = new RenderOptions { Style = DiagramStyle.Tonal, RoundedEdges = false };
		var diagram = DiagramPreparationStage.Prepare(Flow, options, ResourceLimits.Default);

		_ = RenderConfigurationNormalizer.Normalize(diagram, options).EdgeRadius.Should().Be(0);
	}

	[Test]
	public void Each_preset_renders_differently()
	{
		var quiet = MermaidRenderer.RenderSvg(Flow, new RenderOptions { Style = DiagramStyle.Quiet });
		var blueprint = MermaidRenderer.RenderSvg(Flow, new RenderOptions { Style = DiagramStyle.Blueprint });
		var tonal = MermaidRenderer.RenderSvg(Flow, new RenderOptions { Style = DiagramStyle.Tonal });

		_ = quiet.Should().NotBe(blueprint);
		_ = quiet.Should().NotBe(tonal);
		_ = blueprint.Should().NotBe(tonal);
	}

	[Test]
	public void Elevation_zero_emits_no_shadow_rules()
	{
		var flat = MermaidRenderer.RenderSvg(Flow, new RenderOptions { Elevation = 0 });
		var raised = MermaidRenderer.RenderSvg(Flow, new RenderOptions { Elevation = 2 });

		_ = flat.Should().NotContain("drop-shadow");
		_ = raised.Should().Contain("drop-shadow");
	}

	[Test]
	public void Blueprint_ignores_elevation() =>
		MermaidRenderer.RenderSvg(Flow, new RenderOptions { Style = DiagramStyle.Blueprint, Elevation = 2 })
			.Should().NotContain("drop-shadow");
}
