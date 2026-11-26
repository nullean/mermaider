using System.Text.RegularExpressions;
using System.Xml.Linq;
using AwesomeAssertions;
using Mermaider.Animate.Score;

namespace Mermaider.Animate.Tests;

/// <summary>fail, cascade, recover: an outage in the words people use for one.</summary>
public partial class FailureTests
{
	private const string Flow =
		"""
		flowchart LR
		  client[Client] --> lb[Balancer]
		  lb --> api[Orders API]
		  api --> db[(Orders DB)]
		""";

	private static readonly string[] Labels = ["Client", "Balancer", "Orders API", "Orders DB"];

	/// <summary>A cascade's line: a stroke in the failure colour, drawn by its dash.</summary>
	private static bool IsCascade(XElement e) =>
		(string?)e.Attribute("pathLength") == "1" && (string?)e.Attribute("stroke-dasharray") == "1 1" && (string?)e.Attribute("stroke-width") == "3.5";

	[GeneratedRegex("(^|;)opacity:0(;|$)")]
	private static partial Regex Hidden();

	private static bool Shown(XElement e) =>
		!e.AncestorsAndSelf().Any(
			a => Hidden().IsMatch((string?)a.Attribute("style") ?? "") || ((string?)a.Attribute("style") is null && (string?)a.Attribute("opacity") == "0")
		);

	private static XDocument At(AnimatedDiagram animation, TimeSpan t) => XDocument.Parse(animation.RenderFrame(t));

	/// <summary>The failure mark on the node labelled <paramref name="label"/>: its ring, tinted in the failure colour.</summary>
	private static XElement? MarkOn(XDocument doc, string label) =>
		doc
			.Descendants()
			.First(e => e.Name.LocalName == "text" && e.Value == label)
			.Ancestors()
			.TakeWhile(a => a.Descendants().Count(d => d.Name.LocalName == "text" && Labels.Contains(d.Value)) <= 1)
			.Select(a => a.Descendants().FirstOrDefault(r => r.Name.LocalName == "rect" && (string?)r.Attribute("fill-opacity") == "0.14"))
			.FirstOrDefault(r => r is not null);

	private static bool Failed(XDocument doc, string label) => MarkOn(doc, label) is { } mark && Shown(mark);

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
	public void The_verbs_name_nodes_a_reason_and_a_chain()
	{
		var lines = (Flow +
			"\nanimate\n  step \"x\"\n    fail api \"timeout\"\n    fail \"Orders DB\"\n    cascade db -> api -> lb \"disk full\"\n    recover api, db\n").Split(
			'\n'
		);
		var diagnostics = new List<AnimationDiagnostic>();
		var score = ScoreParser.Parse(lines, ScoreParser.FindStart(lines), diagnostics);
		diagnostics.Should().BeEmpty();
		var statements = score.Steps.Single().Statements;
		statements[0].Should().BeOfType<FailStatement>().Which.Reason.Should().Be("timeout");
		statements[1].Should().BeOfType<FailStatement>().Which.Targets.Single().ByLabel.Should().BeTrue();
		statements[2].Should().BeOfType<CascadeStatement>().Which.Path.Should().Equal("db", "api", "lb");
		statements[3].Should().BeOfType<RecoverStatement>().Which.Targets.Should().HaveCount(2);

		Problems(Flow + "\nanimate\n  step \"x\"\n    cascade db\n    pulse db\n").Should().ContainSingle().Which.Message.Should().Contain("at least two");
		Problems(Flow + "\nanimate\n  step \"x\"\n    cascade db -> client\n    pulse db\n")
			.Should()
			.ContainSingle()
			.Which
			.Message
			.Should()
			.Contain("no edge between db and client");
		Problems(Flow + "\nanimate\n  step \"x\"\n    fail nope\n    pulse db\n").Should().ContainSingle();
	}

	[Test]
	public void A_failed_node_is_marked_carries_its_reason_and_is_cut_off()
	{
		var animation = MermaidAnimator.Compile(Flow + "\nanimate once\n  step \"x\"\n    fail api \"timeout\"\n");
		var before = At(animation, TimeSpan.Zero);
		var after = At(animation, animation.Steps[0].End);
		Failed(before, "Orders API").Should().BeFalse();
		Failed(after, "Orders API").Should().BeTrue();
		Failed(after, "Balancer").Should().BeFalse("only the failed node is marked");
		after.Descendants().Single(e => e.Name.LocalName == "text" && e.Value == "timeout").Should().Match<XElement>(e => Shown(e));

		// lb -> api is cut (dimmed, with its cross); api -> db, which leaves it, is not
		Regex.Matches(animation.RenderFrame(animation.Steps[0].End), @"opacity:0\.28").Count.Should().BeGreaterThan(0);
		var crosses = after
			.Descendants()
			.Where(e => e.Name.LocalName == "g" && e.Elements().Any(c => c.Name.LocalName == "circle" && (string?)c.Attribute("r") == "10"))
			.ToList();
		crosses.Count(Shown).Should().Be(1, "one edge leads into api");

		// where the flow starts, nothing leads in: its failure cuts what leaves it
		var source = MermaidAnimator.Compile(Flow + "\nanimate once\n  step \"x\"\n    fail client\n");
		Regex.Matches(source.RenderFrame(source.Steps[0].End), @"opacity:0\.28").Count.Should().BeGreaterThan(0);
	}

	[Test]
	public void A_cascade_fails_each_node_in_turn_from_its_cause()
	{
		var animation = MermaidAnimator.Compile(Flow + "\nanimate once\n  step \"x\"\n    cascade db -> api -> lb -> client \"disk full\"\n");

		double FirstFailed(string label)
		{
			for (var t = 0; t <= animation.Duration.TotalMilliseconds; t += 50)
				if (Failed(At(animation, TimeSpan.FromMilliseconds(t)), label))
					return t;
			return double.MaxValue;
		}

		var times = new[] { "Orders DB", "Orders API", "Balancer", "Client" }.Select(FirstFailed).ToList();
		times.Should().BeInAscendingOrder().And.NotContain(double.MaxValue);
		times.Zip(times.Skip(1), (a, b) => b - a).Should().OnlyContain(gap => gap >= 200, "each fails when the failure reaches it, not all at once");

		var end = At(animation, animation.Steps[0].End);
		end.Descendants().Count(e => e.Name.LocalName == "text" && e.Value == "disk full").Should().Be(1, "the reason goes on the cause only");

		// the failure runs against the arrows: the line toward api starts at db's end of the edge
		var lines = end.Descendants().Where(IsCascade).ToList();
		lines.Should().HaveCount(3);
		foreach (var line in lines)
		{
			var xs = Regex
				.Matches((string)line.Attribute("d")!, @"[ML](-?[\d.]+),")
				.Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
				.ToList();
			xs[0].Should().BeGreaterThan(xs[^1], "each hop is drawn from the cause's side, against the arrow");
		}
	}

	[Test]
	public void Recovery_undoes_the_failure_and_its_cascade()
	{
		var animation = MermaidAnimator.Compile(
			Flow + "\nanimate once\n  step \"down\"\n    fail db \"disk full\"\n    cascade db -> api\n  step \"back\"\n    recover db, api\n"
		);
		var down = At(animation, animation.Steps[0].End);
		var back = At(animation, animation.Steps[1].End);
		Failed(down, "Orders DB").Should().BeTrue();
		Failed(down, "Orders API").Should().BeTrue();
		Failed(back, "Orders DB").Should().BeFalse();
		Failed(back, "Orders API").Should().BeFalse();
		back.Descendants().Where(e => e.Name.LocalName == "text" && e.Value == "disk full").Should().OnlyContain(e => !Shown(e));
		back.Descendants().Where(IsCascade).Should().OnlyContain(e => !Shown(e));
		Regex.Matches(animation.RenderFrame(animation.Steps[1].End), @"opacity:0\.28").Count.Should().Be(0, "the cut edge heals");
	}

	[Test]
	public void A_cut_gives_way_to_the_failure_running_down_it()
	{
		var animation = MermaidAnimator.Compile(Flow + "\nanimate once\n  step \"x\"\n    fail db\n    cascade db -> api\n");
		var end = At(animation, animation.Steps[0].End);
		var crosses = end
			.Descendants()
			.Where(e => e.Name.LocalName == "g" && e.Elements().Any(c => c.Name.LocalName == "circle" && (string?)c.Attribute("r") == "10"))
			.ToList();
		crosses.Should().NotBeEmpty();
		crosses.Should().OnlyContain(c => !Shown(c));
	}
}
