using System.Xml.Linq;
using AwesomeAssertions;
using Mermaider.Animate.Score;

namespace Mermaider.Animate.Tests;

/// <summary>The verbs that show state (fill, badge, hang, note), message references, and lit paths.</summary>
public class StateVerbTests
{
	private const string Flow = """
		flowchart LR
		  a[A] --> b[Queue]
		  b --> c[C]
		""";

	private const string Sequence = """
		sequenceDiagram
		  A->>B: one
		  B-->>A: two
		  A->>C: three
		""";

	private static List<AnimationDiagnostic> Problems(string source)
	{
		try
		{
			_ = MermaidAnimator.Compile(source);
			return [];
		}
		catch (AnimationException e)
		{
			return [.. e.Diagnostics];
		}
	}

	[Test]
	public void The_state_verbs_parse()
	{
		var lines = "flowchart LR\n  a --> b\nanimate\n  fill b 3/8\n  fill b 5\n  badge a, b \"lag\" as warn\n  badge a clear\n  hang b\n  note #2..#3 \"38 ms\" as ok\n  mark #1..#2 warn\n".Split(
			'\n'
		);
		var diagnostics = new List<AnimationDiagnostic>();
		var score = ScoreParser.Parse(lines, 2, diagnostics);
		diagnostics.Should().BeEmpty();
		var statements = score.Steps.Select(s => s.Statements.Single()).ToList();
		statements[0].Should().BeEquivalentTo(new FillStatement(new TargetRef("b", false, 4), 3, 8, 4));
		statements[1].Should().BeOfType<FillStatement>().Which.Capacity.Should().BeNull();
		statements[2].Should().BeOfType<BadgeStatement>().Which.Role.Should().Be(Role.Warn);
		statements[3].Should().BeOfType<BadgeStatement>().Which.Text.Should().BeNull();
		statements[4].Should().BeOfType<HangStatement>();
		statements[5].Should().BeEquivalentTo(new NoteStatement(2, 3, "38 ms", Role.Ok, 9));
		statements[6].Should().BeOfType<MarkStatement>().Which.Edges.Select(e => e.ToString()).Should().Equal("#1", "#2");
	}

	[Test]
	public void Fill_lights_slots_up_to_the_count_and_drains_them()
	{
		var animation = MermaidAnimator.Compile(Flow + "\nanimate once\n  step \"up\"\n    fill b 6/8\n  step \"down\"\n    fill b 2\n");

		int Lit(TimeSpan t) =>
			XDocument
				.Parse(animation.RenderFrame(t))
				.Descendants()
				.Count(
					e => e.Name.LocalName == "rect" && (string?)e.Attribute("height") == "5" && ((string?)e.Attribute("style"))?.StartsWith(
						"opacity:1",
						StringComparison.Ordinal
					) == true && (string?)e.Attribute("width") != null
				) - 1; // the track

		Lit(animation.Steps[0].End - TimeSpan.FromMilliseconds(50)).Should().Be(6);
		Lit(animation.Steps[1].End - TimeSpan.FromMilliseconds(50)).Should().Be(2);
	}

	[Test]
	public void The_first_fill_needs_a_capacity_and_counts_stay_within_it()
	{
		Problems(Flow + "\nanimate\n  fill b 3\n").Single().Message.Should().Contain("needs its capacity");
		Problems(Flow + "\nanimate\n  fill b 9/8\n").Single().Message.Should().Contain("more than the capacity");
		Problems(Flow + "\nanimate\n  fill b 3/8\n  fill b 9\n").Single().Message.Should().Contain("more than b's capacity of 8");
	}

	[Test]
	public void Messages_are_checked_against_the_diagram()
	{
		Problems(Sequence + "\nanimate\n  mark #4 warn\n").Single().Message.Should().Be("The diagram has 3 messages, so there is no #4.");
		Problems(Flow + "\nanimate\n  mark #1 warn\n").Single().Message.Should().Contain("not a sequence diagram");
		Problems(Flow + "\nanimate\n  note #1 \"x\"\n").Single().Message.Should().Contain("use badge");
		Problems(Sequence + "\nanimate\n  play\n  note #1..#3 \"total\"\n  mark #2 ok\n").Should().BeEmpty();
	}

	[Test]
	public void A_sent_token_lights_its_path_until_the_step_ends()
	{
		var animation = MermaidAnimator.Compile(Flow + "\nanimate once\n  step \"go\"\n    send a -> b\n  step \"next\"\n    pulse c\n");
		var trail = XDocument
			.Parse(animation.Svg)
			.Descendants()
			.Single(e => e.Name.LocalName == "path" && (string?)e.Attribute("pathLength") == "1" && (string?)e.Attribute("stroke-dashoffset") == "1");
		var cls = ((string)trail.Attribute("class")!).Split(' ').Last();

		string StyleAt(TimeSpan t) =>
			(string)XDocument
				.Parse(animation.RenderFrame(t))
				.Descendants()
				.Single(e => ((string?)e.Attribute("class") ?? "").Split(' ').Contains(cls))
				.Attribute("style")!;

		StyleAt(animation.Steps[0].End - TimeSpan.FromMilliseconds(50)).Should().Contain("stroke-dashoffset:0").And.Contain("opacity:0.85");
		StyleAt(animation.Steps[1].End).Should().MatchRegex("(^|;)opacity:0(;|$)");
	}

	[Test]
	public void A_tour_keeps_the_hops_it_has_taken_lit()
	{
		var animation = MermaidAnimator.Compile(Flow + "\nanimate once\n  step \"go\"\n    tour a -> b -> c\n");
		var frame = XDocument.Parse(animation.RenderFrame(animation.Steps[0].End - TimeSpan.FromMilliseconds(50)));
		var trails = frame
			.Descendants()
			.Where(
				e => e.Name.LocalName == "path" && (string?)e.Attribute("pathLength") == "1" && (string?)e.Attribute("stroke-dasharray") == "1 1" &&
					e.Attribute("mask") is null && (string?)e.Attribute("stroke-width") == "3.5"
			)
			.ToList();
		trails.Should().HaveCount(2);
		trails.Should().OnlyContain(t => ((string)t.Attribute("style")!).Contains("opacity:0.85"));
		// and nothing on the route is dimmed by the focus
		frame
			.Descendants()
			.Where(e => (string?)e.Attribute("data-id") is "a" or "b" or "c")
			.Should()
			.OnlyContain(e => !((string?)e.Attribute("style") ?? "").Contains("opacity:0.22"));
	}

	[Test]
	public void Captions_sit_above_the_diagram_and_are_numbered()
	{
		var svg = MermaidAnimator.RenderSvg(Flow + "\nanimate\n  step \"one\"\n    pulse a\n  step \"two\"\n    pulse b\n");
		var root = XDocument.Parse(svg).Root!;
		var viewBox = ((string)root.Attribute("viewBox")!).Split(' ');
		double.Parse(viewBox[1], System.Globalization.CultureInfo.InvariantCulture).Should().BeLessThan(0, "the caption band is added above");
		root.Descendants().Where(e => e.Name.LocalName == "text").Select(e => e.Value).Should().Contain(["1/2", "one", "2/2", "two"]);
	}
}
