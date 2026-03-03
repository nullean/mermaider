using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

public class TimelineRendererTests
{
	private const string FullTimeline = """
		timeline
		title History of Social Media
		section Early Days
		2002 : LinkedIn
		2004 : Facebook : Google
		section Modern Era
		2010 : Instagram
		2019 : TikTok
		""";

	[Test]
	public void Renders_valid_svg()
	{
		var svg = MermaidRenderer.RenderSvg(FullTimeline);

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Contains_title()
	{
		var svg = MermaidRenderer.RenderSvg(FullTimeline);

		svg.Should().Contain("History of Social Media");
	}

	[Test]
	public void Contains_period_labels()
	{
		var svg = MermaidRenderer.RenderSvg(FullTimeline);

		svg.Should().Contain("2002");
		svg.Should().Contain("2004");
		svg.Should().Contain("2010");
		svg.Should().Contain("2019");
	}

	[Test]
	public void Contains_events()
	{
		var svg = MermaidRenderer.RenderSvg(FullTimeline);

		svg.Should().Contain("LinkedIn");
		svg.Should().Contain("Facebook");
		svg.Should().Contain("Google");
		svg.Should().Contain("Instagram");
		svg.Should().Contain("TikTok");
	}

	[Test]
	public void Contains_section_labels()
	{
		var svg = MermaidRenderer.RenderSvg(FullTimeline);

		svg.Should().Contain("Early Days");
		svg.Should().Contain("Modern Era");
	}

	[Test]
	public void Contains_timeline_axis()
	{
		var svg = MermaidRenderer.RenderSvg(FullTimeline);

		svg.Should().Contain("<line");
		svg.Should().Contain("<circle");
	}

	[Test]
	public void Renders_without_sections()
	{
		var svg = MermaidRenderer.RenderSvg("""
			timeline
			2020 : Event A
			2021 : Event B
			""");

		svg.Should().StartWith("<svg");
		svg.Should().Contain("Event A");
		svg.Should().Contain("Event B");
	}
}
