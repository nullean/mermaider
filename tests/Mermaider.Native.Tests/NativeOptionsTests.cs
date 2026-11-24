using AwesomeAssertions;
using Mermaider.Models;

namespace Mermaider.Native.Tests;

public class NativeOptionsTests
{
	[Test]
	public void No_options_are_the_defaults()
	{
		var options = NativeOptions.Parse(null);
		options.Style.Should().Be(DiagramStyle.Quiet);
		options.Bg.Should().NotBeNullOrEmpty();
		NativeOptions.Parse("  ").Should().BeEquivalentTo(options);
	}

	[Test]
	public void A_theme_is_the_base_and_a_colour_given_beside_it_wins()
	{
		var themed = NativeOptions.Parse("""{"theme":"tokyo-night"}""");
		var overridden = NativeOptions.Parse("""{"theme":"tokyo-night","bg":"#102030"}""");
		themed.Bg.Should().NotBe("#102030");
		overridden.Bg.Should().Be("#102030");
		overridden.Fg.Should().Be(themed.Fg, "the rest of the theme is kept");
	}

	[Test]
	public void The_colours_fonts_style_and_numbers_arrive_where_they_belong()
	{
		var options = NativeOptions.Parse(
			"""
			{"bg":"#111111","fg":"#eeeeee","line":"#333333","accent":"#ff8800","muted":"#888888","surface":"#222222","border":"#444444",
			 "font":"Inter","monoFont":"Menlo","fontSize":"14px","style":"Blueprint","gradient":false,"transparent":false,
			 "roundedEdges":false,"tint":0.8,"padding":12,"nodeSpacing":30,"layerSpacing":50,"elevation":2,
			 "dataPalette":["#aa0000","#00aa00"]}
			"""
		);
		options.Bg.Should().Be("#111111");
		options.Accent.Should().Be("#ff8800");
		options.Surface.Should().Be("#222222");
		options.Font.Should().Be("Inter");
		options.MonoFont.Should().Be("Menlo");
		options.FontSize.Should().Be("14px");
		options.Style.Should().Be(DiagramStyle.Blueprint, "style names are not case sensitive");
		options.Gradient.Should().BeFalse();
		options.Transparent.Should().BeFalse();
		options.RoundedEdges.Should().BeFalse();
		options.Tint.Should().Be(0.8);
		options.Padding.Should().Be(12);
		options.NodeSpacing.Should().Be(30);
		options.LayerSpacing.Should().Be(50);
		options.Elevation.Should().Be(2);
		options.DataPalette.Should().Equal("#aa0000", "#00aa00");
	}

	[Test]
	[Arguments("""{"nope":1}""", "unknown option 'nope'")]
	[Arguments("""{"theme":"no-such-theme"}""", "unknown theme 'no-such-theme'")]
	[Arguments("""{"style":"loud"}""", "unknown style 'loud'")]
	[Arguments("""{"bg":3}""", "option 'bg' must be a non-empty string")]
	[Arguments("""{"bg":""}""", "option 'bg' must be a non-empty string")]
	[Arguments("""{"gradient":"yes"}""", "option 'gradient' must be true or false")]
	[Arguments("""{"tint":"high"}""", "option 'tint' must be a number")]
	[Arguments("""{"dataPalette":"red"}""", "option 'dataPalette' must be an array of strings")]
	[Arguments("[1]", "options must be a JSON object")]
	[Arguments("{bg", "options are not valid JSON")]
	public void A_mistake_in_the_options_is_an_error_that_says_what(string json, string message)
	{
		var act = () => NativeOptions.Parse(json);
		act.Should().Throw<ArgumentException>().Which.Message.Should().Contain(message);
	}
}
