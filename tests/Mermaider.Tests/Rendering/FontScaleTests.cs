using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Rendering;

namespace Mermaider.Tests.Rendering;

public class FontScaleTests
{
	[Test]
	public void Computes_all_tiers_for_each_allowed_unit()
	{
		var cases = new[]
		{
			("16px", "12px", "14px", "16px", "18px", 16.0),
			("1rem", "0.75rem", "0.875rem", "1rem", "1.125rem", 16.0),
			("1.5em", "1.125em", "1.313em", "1.5em", "1.688em", 24.0),
			("100%", "75%", "87.5%", "100%", "112.5%", 1600.0),
		};

		foreach (var (input, xs, s, m, l, px) in cases)
		{
			var scale = FontScale.From(new RenderOptions { FontSize = input });
			scale.Xs.Should().Be(xs);
			scale.S.Should().Be(s);
			scale.M.Should().Be(m);
			scale.L.Should().Be(l);
			scale.MPx.Should().Be(px);
		}
	}

	[Test]
	public void Invalid_base_sizes_fall_back_to_the_complete_default_scale()
	{
		string?[] invalid =
		[
			"", " ", "1", "px", "-1rem", "+1rem", ".5rem", "1.0000rem", "1e2px",
			"calc(1rem)", "var(--size)", "1vh", "1rem;display:none", "NaNpx", "Infinitypx",
			new string('9', 1_000) + "px",
		];

		foreach (var value in invalid)
		{
			var scale = FontScale.From(new RenderOptions { FontSize = value });
			scale.M.Should().Be(FontScale.Default.M, $"'{value}' is outside the base-size grammar");
			scale.Xs.Should().Be(FontScale.Default.Xs);
			scale.S.Should().Be(FontScale.Default.S);
			scale.L.Should().Be(FontScale.Default.L);
		}
	}

	[Test]
	public void Invalid_ratios_fall_back_independently()
	{
		double[] invalid = [0, -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity];

		foreach (var value in invalid)
		{
			var scale = FontScale.From(new RenderOptions
			{
				FontSizeExtraSmall = value,
				FontSizeSmall = value,
				FontSizeLarge = value,
			});

			scale.Xs.Should().Be(FontScale.Default.Xs);
			scale.S.Should().Be(FontScale.Default.S);
			scale.L.Should().Be(FontScale.Default.L);
		}
	}

	[Test]
	public void Valid_custom_ratios_are_formatted_invariantly()
	{
		var scale = FontScale.From(new RenderOptions
		{
			FontSize = "10px",
			FontSizeExtraSmall = 0.3333,
			FontSizeSmall = 1.25,
			FontSizeLarge = 2.5,
		});

		scale.Xs.Should().Be("3.333px");
		scale.S.Should().Be("12.5px");
		scale.M.Should().Be("10px");
		scale.L.Should().Be("25px");
	}
}
