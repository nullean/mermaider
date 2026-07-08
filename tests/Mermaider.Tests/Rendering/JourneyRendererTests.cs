using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

public class JourneyRendererTests
{
	private const string FullJourney = """
		journey
		title My working day
		section Go to work
		Make tea: 5: Me
		Go upstairs: 3: Me
		Do work: 1: Me, Cat
		section Go home
		Go downstairs: 5: Me
		Sit down: 5: Me
		""";

	[Test]
	public void Renders_valid_svg()
	{
		var svg = MermaidRenderer.RenderSvg(FullJourney);

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Contains_title()
	{
		var svg = MermaidRenderer.RenderSvg(FullJourney);

		svg.Should().Contain("My working day");
	}

	[Test]
	public void Contains_section_and_task_labels()
	{
		var svg = MermaidRenderer.RenderSvg(FullJourney);

		svg.Should().Contain("Go to work");
		svg.Should().Contain("Go home");
		svg.Should().Contain("Make tea");
		svg.Should().Contain("Do work");
		svg.Should().Contain("Sit down");
	}

	[Test]
	public void Contains_actors()
	{
		var svg = MermaidRenderer.RenderSvg(FullJourney);

		svg.Should().Contain("Me");
		svg.Should().Contain("Cat");
	}

	[Test]
	public void Contains_task_boxes_and_timeline()
	{
		var svg = MermaidRenderer.RenderSvg(FullJourney);

		svg.Should().Contain("<rect");
		svg.Should().Contain("<line");
		svg.Should().Contain("stroke-dasharray");
	}

	[Test]
	public void Contains_faces_for_scores()
	{
		var svg = MermaidRenderer.RenderSvg(FullJourney);

		// Faces are drawn as circle + path/line mouths
		svg.Should().Contain("<circle");
		svg.Should().Contain("<path");
	}

	[Test]
	public void Contains_actor_legend()
	{
		var svg = MermaidRenderer.RenderSvg(FullJourney);

		// Legend lists unique actors (Me, Cat)
		svg.Should().Contain("Me");
		svg.Should().Contain("Cat");
	}

	[Test]
	public void Uses_theme_css_vars_for_text()
	{
		var svg = MermaidRenderer.RenderSvg(FullJourney);

		svg.Should().Contain("var(--_text)");
		svg.Should().Contain("var(--fs-");
	}

	[Test]
	public void Renders_empty_journey()
	{
		var svg = MermaidRenderer.RenderSvg("""
			journey
			title Empty
			""");

		svg.Should().StartWith("<svg");
		svg.Should().Contain("Empty");
	}

	[Test]
	public void Detects_and_renders_via_public_api()
	{
		var svg = MermaidRenderer.RenderSvg("""
			journey
			section A
			Step one: 4: User
			Step two: 2: User, Admin
			""");

		svg.Should().Contain("Step one");
		svg.Should().Contain("Admin");
	}
}
