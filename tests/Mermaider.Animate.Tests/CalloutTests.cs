using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AwesomeAssertions;
using Mermaider.Animate.Score;

namespace Mermaider.Animate.Tests;

/// <summary>Callouts: a label clear of the diagram, with a leader to what it explains, for the length of its step.</summary>
public partial class CalloutTests
{
	private const string Chain = """
		flowchart LR
		  client --> lb
		  lb --> api
		  api --> db
		""";

	[GeneratedRegex("(^|;)opacity:0(;|$)")]
	private static partial Regex Hidden();

	private static double N(XElement e, string attribute) => double.Parse((string)e.Attribute(attribute)!, CultureInfo.InvariantCulture);

	/// <summary>The callout whose text starts with <paramref name="text"/>: its group, its dot and its text.</summary>
	private static (XElement Group, XElement Dot, XElement Text) Callout(XDocument doc, string text)
	{
		var label = doc.Descendants().Single(e => e.Name.LocalName == "text" && e.Value.StartsWith(text, StringComparison.Ordinal) && e.Elements().Any());
		var group = label.Parent!;
		return (group, group.Elements().Single(e => e.Name.LocalName == "circle"), label);
	}

	private static bool IsHidden(XElement e) => e.AncestorsAndSelf().Any(a => Hidden().IsMatch((string?)a.Attribute("style") ?? ""));

	private static XElement Node(XDocument doc, string id) =>
		doc.Descendants().First(e => (string?)e.Attribute("data-id") == id || ((string?)e.Attribute("id"))?.EndsWith(id, StringComparison.Ordinal) == true);

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
	public void A_callout_names_one_node_or_edge_and_its_text()
	{
		var lines = (Chain +
			"\nanimate\n  step \"x\"\n    callout lb \"round robin\" as warn\n    callout api -> db \"slow\"\n    callout \"client\" \"you\"\n").Split('\n');
		var diagnostics = new List<AnimationDiagnostic>();
		var score = ScoreParser.Parse(lines, ScoreParser.FindStart(lines), diagnostics);
		diagnostics.Should().BeEmpty();
		var callouts = score.Steps.Single().Statements.Cast<CalloutStatement>().ToList();
		callouts.Select(c => $"{c.Target?.Id}{c.Edge} {c.Text} {c.Role}").Should().Equal("lb round robin Warn", "api -> db slow ", "client you ");

		Problems(Chain + "\nanimate\n  step \"x\"\n    callout lb, api \"both\"\n    pulse db\n")
			.Should()
			.ContainSingle()
			.Which
			.Message
			.Should()
			.StartWith("callout takes one node or edge");
		Problems(Chain + "\nanimate\n  step \"x\"\n    callout lb\n    pulse db\n").Should().ContainSingle();
		Problems(Chain + "\nanimate\n  step \"x\"\n    callout nope \"x\"\n    pulse db\n")
			.Should()
			.ContainSingle("a node that is not there is reported once, not once per pass");
	}

	[Test]
	public void A_callout_takes_the_side_its_leader_reaches_without_crossing_a_node()
	{
		var animation = MermaidAnimator.Compile(Chain + "\nanimate once\n  step \"x\"\n    callout lb \"in the middle\"\n    callout db \"at the end\"\n");
		var doc = XDocument.Parse(animation.RenderFrame(animation.Steps[0].End - TimeSpan.FromMilliseconds(300)));
		var plain = XDocument.Parse(MermaidRenderer.RenderSvg(Chain));
		var bottom = plain.Descendants().Where(e => e.Name.LocalName == "rect").Max(r => N(r, "y") + N(r, "height"));
		var right = plain.Descendants().Where(e => e.Name.LocalName == "rect").Max(r => N(r, "x") + N(r, "width"));

		// lb sits between client and api: either side would cross one, so its callout goes below
		var middle = Callout(doc, "in the middle");
		N(middle.Text.Elements().First(), "y").Should().BeGreaterThan(bottom);
		// db is at the end of the chain: its callout goes right, level with it
		var end = Callout(doc, "at the end");
		N(end.Text.Elements().First(), "x").Should().BeGreaterThan(right);
		Math.Abs(N(end.Text.Elements().First(), "y") - 4 - N(end.Dot, "cy")).Should().BeLessThan(8);

		// below is the shorter way out from api, but cache is in it; the right is clear
		const string rows = "flowchart LR\n  client --> lb\n  lb --> api\n  api --> db\n  lb --> cache\n  cache --> db\n";
		var twoRows = MermaidAnimator.Compile(rows + "\nanimate once\n  step \"x\"\n    callout api \"clear on the right\"\n");
		var api = Callout(XDocument.Parse(twoRows.RenderFrame(twoRows.Steps[0].End - TimeSpan.FromMilliseconds(300))), "clear on the right");
		((string?)api.Text.Attribute("text-anchor")).Should().Be("start", "it is placed on the right, not below through cache");
	}

	[Test]
	public void The_stage_grows_round_the_diagram_and_the_diagram_stays_put()
	{
		var plain = XDocument.Parse(MermaidAnimator.RenderSvg(Chain + "\nanimate\n  step \"x\"\n    pulse db\n"));
		var called = XDocument.Parse(MermaidAnimator.RenderSvg(Chain + "\nanimate\n  step \"x\"\n    callout db \"a long explanation for the end\"\n"));

		double[] Box(XDocument d) => [.. ((string)d.Root!.Attribute("viewBox")!).Split(' ').Select(v => double.Parse(v, CultureInfo.InvariantCulture))];

		var (before, after) = (Box(plain), Box(called));
		after[0].Should().Be(before[0], "nothing was placed left");
		after[2].Should().BeGreaterThan(before[2] + 150, "the label needs its width on the right");
		after[3].Should().Be(before[3]);
		var rects =
			(XDocument d) => d
				.Descendants()
				.Where(e => e.Name.LocalName == "rect" && e.Attribute("rx") != null)
				.Take(4)
				.Select(r => (N(r, "x"), N(r, "y")))
				.ToList();
		rects(called).Should().Equal(rects(plain));
	}

	[Test]
	public void A_callout_lasts_for_its_step()
	{
		var animation = MermaidAnimator.Compile(Chain + "\nanimate once\n  step \"one\"\n    callout db \"now\"\n  step \"two\"\n    pulse lb\n");

		XDocument At(TimeSpan t) => XDocument.Parse(animation.RenderFrame(t));

		IsHidden(Callout(At(TimeSpan.Zero), "now").Group).Should().BeTrue();
		IsHidden(Callout(At(animation.Steps[0].End - TimeSpan.FromMilliseconds(300)), "now").Group).Should().BeFalse();
		IsHidden(Callout(At(animation.Steps[1].End), "now").Group).Should().BeTrue("the next step has its own point to make");
		var longer = MermaidAnimator.Compile(
			Chain + "\nanimate once\n  step \"one\"\n    callout db \"now, and a good deal more for the reader to take in before it goes\"\n"
		);
		longer.Steps[0].End.Should().BeGreaterThan(animation.Steps[0].End + TimeSpan.FromMilliseconds(1000), "the step holds long enough to read it");
	}

	[Test]
	public void Callouts_in_one_step_keep_clear_of_each_other_and_long_ones_wrap()
	{
		const string stack =
			"flowchart TD\n  a --> b\n  b --> c\n\nanimate once\n  step \"x\"\n    callout a \"first\"\n    callout b \"second, which is long enough to need a second line\"\n    callout c \"third\"\n";
		var animation = MermaidAnimator.Compile(stack);
		var doc = XDocument.Parse(animation.RenderFrame(animation.Steps[0].End - TimeSpan.FromMilliseconds(300)));
		var texts = new[] { "first", "second", "third" }.Select(t => Callout(doc, t).Text).ToList();
		texts[1].Elements().Should().HaveCountGreaterThan(1, "a long callout wraps");
		var spans = texts.Select(t => (Top: t.Elements().Min(s => N(s, "y")) - 12, Bottom: t.Elements().Max(s => N(s, "y")))).OrderBy(s => s.Top).ToList();
		for (var i = 1; i < spans.Count; i++)
			if (Callout(doc, "first").Text.Attribute("text-anchor")?.Value == texts[i].Attribute("text-anchor")?.Value)
				spans[i].Top.Should().BeGreaterThanOrEqualTo(spans[i - 1].Bottom, "labels on one side do not overlap");
	}

	[Test]
	public void Two_callouts_on_one_node_stack()
	{
		var animation = MermaidAnimator.Compile(Chain + "\nanimate once\n  step \"x\"\n    callout db \"one\"\n    callout db \"two\"\n");
		var doc = XDocument.Parse(animation.RenderFrame(animation.Steps[0].End - TimeSpan.FromMilliseconds(300)));
		var (one, two) = (N(Callout(doc, "one").Text.Elements().Single(), "y"), N(Callout(doc, "two").Text.Elements().Single(), "y"));
		Math.Abs(two - one).Should().BeGreaterThanOrEqualTo(16, "the second goes under the first");
	}

	[Test]
	public void A_callout_on_a_message_steps_off_the_arrow_before_it_runs_out()
	{
		var animation = MermaidAnimator.Compile(
			"sequenceDiagram\n  A->>B: hi\n  B->>C: forward\n\nanimate once\n  step \"x\"\n    callout #2 \"the slow hop\" as warn\n"
		);
		var doc = XDocument.Parse(animation.RenderFrame(animation.Steps[0].End - TimeSpan.FromMilliseconds(300)));
		var (group, dot, _) = Callout(doc, "the slow hop");
		var leader = (string)group.Elements().Single(e => e.Name.LocalName == "path").Attribute("d")!;
		var points = Regex.Matches(leader, @"(-?[\d.]+),(-?[\d.]+)").Select(m => double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)).ToList();
		points[1].Should().BeGreaterThan(N(dot, "cy") + 5, "the leader leaves the message before it runs along beside it");
	}
}
