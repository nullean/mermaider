using System.Globalization;
using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

public partial class JourneyRendererTests
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
	public void Uses_theme_css_vars_for_title()
	{
		var svg = MermaidRenderer.RenderSvg(FullJourney);

		// Title uses theme token; section/task labels use mermaid-native white on dark fills
		svg.Should().Contain("var(--_text)");
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

	[Test]
	public void Activity_line_tracks_tasks_when_left_margin_expands()
	{
		// Long actor names grow leftMargin; timeline must still end at last task right edge
		const string longActor = "SeniorPrincipalStaffEngineerCoordinator";
		var svg = MermaidRenderer.RenderSvg($"""
			journey
			title Day
			section Work
			Start: 5: {longActor}
			Middle: 3: {longActor}
			End: 1: {longActor}
			""");

		var lineMatch = ActivityLine().Match(svg);
		lineMatch.Success.Should().BeTrue("activity timeline line should be present");
		var lineX1 = double.Parse(lineMatch.Groups[1].Value, CultureInfo.InvariantCulture);
		var lineX2 = double.Parse(lineMatch.Groups[2].Value, CultureInfo.InvariantCulture);

		var rects = TaskRect().Matches(svg);
		rects.Count.Should().Be(3);
		var firstTaskX = double.Parse(rects[0].Groups[1].Value, CultureInfo.InvariantCulture);
		var lastTaskX = double.Parse(rects[^1].Groups[1].Value, CultureInfo.InvariantCulture);
		var lastTaskRight = lastTaskX + 150;

		// Long name expands margin past the 150 base
		firstTaskX.Should().BeGreaterThan(150);
		lineX1.Should().Be(firstTaskX);
		lineX2.Should().BeApproximately(lastTaskRight - 4, 0.02);
	}

	[Test]
	public void ViewBox_height_grows_with_many_actors()
	{
		// Enough unique actors that legend extends past the default face band
		var actors = string.Join(", ", Enumerable.Range(1, 25).Select(i => $"Actor{i:D2}"));
		var svg = MermaidRenderer.RenderSvg($"""
			journey
			title Many actors
			section A
			Only task: 5: {actors}
			""");

		var vb = ViewBox().Match(svg);
		vb.Success.Should().BeTrue();
		var height = double.Parse(vb.Groups[1].Value, CultureInfo.InvariantCulture);

		// Legend: cy starts 60, +20 per actor → last ~ 60+24*20 = 540 (+ pad / title shift)
		height.Should().BeGreaterThan(540);
		svg.Should().Contain("Actor25");
	}

	[GeneratedRegex(
		@"<line x1=""([\d.]+)"" y1=""200"" x2=""([\d.]+)"" y2=""200""[^>]*marker-end=""url\(#journey-arrow\)""",
		RegexOptions.CultureInvariant,
		matchTimeoutMilliseconds: 2000)]
	private static partial Regex ActivityLine();

	[GeneratedRegex(
		@"<rect x=""([\d.]+)"" y=""110"" width=""150"" height=""50""",
		RegexOptions.CultureInvariant,
		matchTimeoutMilliseconds: 2000)]
	private static partial Regex TaskRect();

	[GeneratedRegex(
		@"viewBox=""0 0 [\d.]+ ([\d.]+)""",
		RegexOptions.CultureInvariant,
		matchTimeoutMilliseconds: 2000)]
	private static partial Regex ViewBox();
}
