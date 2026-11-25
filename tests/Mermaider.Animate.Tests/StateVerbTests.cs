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
		Problems(Flow + "\nanimate\n  mark #1 warn\n").Single().Message.Should().Contain("names its elements by id");
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
			.Single(
				e => e.Name.LocalName == "path" && (string?)e.Attribute("pathLength") == "1" && (string?)e.Attribute("stroke-dashoffset") ==
					"1" && (string?)e.Attribute("stroke-width") == "3.5"
			);
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

	[Test]
	[Arguments("count b 3", null, 3.0, null)]
	[Arguments("count b 38 -> 1200 \"ms\" as warn", 38.0, 1200.0, "ms")]
	[Arguments("count \"Queue\" 0.5 -> 2.25 \"%\"", 0.5, 2.25, "%")]
	public void Count_takes_a_node_a_number_and_what_it_counts(string line, double? from, double to, string? unit)
	{
		var lines = ("flowchart LR\n  a --> b[Queue]\nanimate\n  " + line + "\n").Split('\n');
		var diagnostics = new List<AnimationDiagnostic>();
		var score = ScoreParser.Parse(lines, ScoreParser.FindStart(lines), diagnostics);
		diagnostics.Should().BeEmpty();
		var count = score.Steps.Single().Statements.Single().Should().BeOfType<CountStatement>().Subject;
		(count.From, count.To, count.Unit).Should().Be((from, to, unit));
	}

	[Test]
	[Arguments("count b")]
	[Arguments("count b ms")]
	[Arguments("count b 3 ->")]
	[Arguments("count 3")]
	public void Count_without_a_number_says_how_it_is_written(string line) =>
		Problems(Flow + "\nanimate\n  " + line + "\n").Should().ContainSingle().Which.Message.Should().StartWith("count takes a node and a number");

	private static List<string> Badges(string svg) =>
		[.. XDocument.Parse(svg).Descendants().Where(e => e.Name.LocalName == "text" && e.Value.EndsWith(" ms", StringComparison.Ordinal)).Select(e => e.Value)];

	private static string? Shown(AnimatedDiagram animation, double t)
	{
		var frame = XDocument.Parse(animation.RenderFrame(TimeSpan.FromMilliseconds(t)));

		static bool Visible(XElement e) =>
			!e.AncestorsAndSelf().Any(a => System.Text.RegularExpressions.Regex.IsMatch((string?)a.Attribute("style") ?? "", "(^|;)opacity:0(;|$)"));

		return frame
			.Descendants()
			.Where(e => e.Name.LocalName == "text" && e.Value.EndsWith(" ms", StringComparison.Ordinal) && Visible(e))
			.Select(e => e.Value)
			.SingleOrDefault();
	}

	[Test]
	public void A_count_rolls_through_the_values_between_one_at_a_time_and_stays_at_the_last()
	{
		var animation = MermaidAnimator.Compile(Flow + "\nanimate once\n  count b 0 -> 100 \"ms\"\n  pulse a\n");
		Badges(animation.Svg).Should().Equal("0 ms", "10 ms", "20 ms", "30 ms", "40 ms", "50 ms", "60 ms", "70 ms", "80 ms", "90 ms", "100 ms");
		var seen = new List<string>();
		for (var t = 0.0; t <= animation.ScoreDuration.TotalMilliseconds; t += 10)
			if (Shown(animation, t) is { } text && (seen.Count == 0 || seen[^1] != text))
				seen.Add(text);
		seen.Should().Equal(Badges(animation.Svg), "never two at once, never out of order");
		Shown(animation, animation.ScoreDuration.TotalMilliseconds).Should().Be("100 ms");
	}

	[Test]
	public void A_second_count_rolls_on_from_the_first_and_badge_clear_removes_it()
	{
		var animation = MermaidAnimator.Compile(Flow + "\nanimate once\n  count b 40 \"ms\"\n  count b 50 \"ms\"\n  badge b clear\n  pulse a\n");
		Badges(animation.Svg).Should().StartWith(["0 ms", "4 ms"]).And.Contain("40 ms").And.Contain("41 ms").And.EndWith("50 ms");
		Shown(animation, animation.ScoreDuration.TotalMilliseconds).Should().BeNull();
	}

	[Test]
	public void Under_reduced_motion_only_the_last_number_of_a_count_appears()
	{
		var svg = MermaidAnimator.RenderSvg(Flow + "\nanimate once\n  count b 0 -> 100 \"ms\"\n");
		var style = svg[svg.IndexOf("@media (prefers-reduced-motion: reduce)", StringComparison.Ordinal)..];
		var doc = XDocument.Parse(svg);

		string ClassOf(string text) =>
			doc
				.Descendants()
				.Single(e => e.Name.LocalName == "text" && e.Value == text)
				.Ancestors()
				.Select(a => (string?)a.Attribute("class"))
				.First(c => c != null && System.Text.RegularExpressions.Regex.IsMatch(c, @"-a\d+"))!.Split(' ').Last();

		style.Should().Contain($".{ClassOf("50 ms")} {{ animation: none; }}");
		style.Should().NotContain($".{ClassOf("100 ms")} {{ animation: none; }}");
	}
}
