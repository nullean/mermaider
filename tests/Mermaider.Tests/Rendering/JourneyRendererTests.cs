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
	public void Sentiment_curve_runs_through_every_face_under_its_task()
	{
		const string longActor = "SeniorPrincipalStaffEngineerCoordinator";
		var svg = MermaidRenderer.RenderSvg($"""
			journey
			title Day
			section Work
			Start: 5: {longActor}
			Middle: 3: {longActor}
			End: 1: {longActor}
			""");

		var curve = Curve().Match(svg);
		curve.Success.Should().BeTrue("the sentiment curve is drawn");
		var points = CurvePoint().Matches(curve.Groups[1].Value)
			.Select(m => (X: double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), Y: double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)))
			.ToList();
		points.Should().HaveCount(3);

		var faces = Face().Matches(svg)
			.Select(m => (X: double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), Y: double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)))
			.ToList();
		faces.Should().BeEquivalentTo(points, o => o.WithStrictOrdering(), "every face sits on the curve");
		points[0].Y.Should().BeLessThan(points[1].Y, "a higher score sits higher");
		points[1].Y.Should().BeLessThan(points[2].Y);

		var tasks = TaskRect().Matches(svg).ToList();
		tasks.Should().HaveCount(3);
		for (var i = 0; i < 3; i++)
		{
			var x = double.Parse(tasks[i].Groups[1].Value, CultureInfo.InvariantCulture);
			var w = double.Parse(tasks[i].Groups[3].Value, CultureInfo.InvariantCulture);
			points[i].X.Should().BeApproximately(x + (w / 2), 0.01, "the face hangs under the centre of its task");
		}
	}

	[Test]
	public void Many_actors_wrap_the_legend_and_push_the_content_down()
	{
		var actors = string.Join(", ", Enumerable.Range(1, 25).Select(i => $"Actor{i:D2}"));
		var svg = MermaidRenderer.RenderSvg($"""
			journey
			title Many actors
			section A
			Only task: 5: {actors}
			""");

		svg.Should().Contain("Actor01");
		svg.Should().Contain("Actor25");
		var legendYs = LegendTextMatches(svg).Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Distinct().ToList();
		legendYs.Count.Should().BeGreaterThan(1, "the legend wraps into several rows");
		var taskY = double.Parse(TaskRect().Match(svg).Groups[2].Value, CultureInfo.InvariantCulture);
		taskY.Should().BeGreaterThan(legendYs.Max(), "content starts below the last legend row");
	}

	[GeneratedRegex(
		@"<path class=""journey-line"" d=""([^""]+)""",
		RegexOptions.CultureInvariant,
		matchTimeoutMilliseconds: 2000)]
	private static partial Regex Curve();

	[GeneratedRegex(
		@"([\d.]+),([\d.]+)",
		RegexOptions.CultureInvariant,
		matchTimeoutMilliseconds: 2000)]
	private static partial Regex CurvePoint();

	[GeneratedRegex(
		@"<g class=""face""[^>]*>\s*<circle cx=""([\d.]+)"" cy=""([\d.]+)""",
		RegexOptions.CultureInvariant,
		matchTimeoutMilliseconds: 2000)]
	private static partial Regex Face();

	[GeneratedRegex(
		@"<g class=""node journey-task""[^>]*>\s*<rect x=""([\d.]+)"" y=""([\d.]+)"" width=""([\d.]+)""",
		RegexOptions.CultureInvariant,
		matchTimeoutMilliseconds: 2000)]
	private static partial Regex TaskRect();

	[GeneratedRegex(
		@"<g class=""legend"">[\s\S]*?</g>",
		RegexOptions.CultureInvariant,
		matchTimeoutMilliseconds: 2000)]
	private static partial Regex Legend();

	private static MatchCollection LegendTextMatches(string svg) => LegendTextY().Matches(Legend().Match(svg).Value);

	[GeneratedRegex(
		@"<text x=""[\d.]+"" y=""([\d.]+)""",
		RegexOptions.CultureInvariant,
		matchTimeoutMilliseconds: 2000)]
	private static partial Regex LegendTextY();
}
