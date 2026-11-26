using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AwesomeAssertions;
using Mermaider.Animate.Score;

namespace Mermaider.Animate.Tests;

/// <summary>ghost, group, because, retry and scale: the verbs that explain a system.</summary>
public partial class ExplainTests
{
	private const string Flow =
		"""
		flowchart LR
		  web[Web] --> api[Orders API]
		  api --> db[(Orders DB)]
		  cron[Report] --> db
		""";

	[GeneratedRegex("(^|;)opacity:0(;|$)")]
	private static partial Regex Hidden();

	private static bool Shown(XElement e) =>
		!e.AncestorsAndSelf().Any(
			a => Hidden().IsMatch((string?)a.Attribute("style") ?? "") || ((string?)a.Attribute("style") is null && (string?)a.Attribute("opacity") == "0")
		);

	private static XDocument At(AnimatedDiagram animation, TimeSpan t) => XDocument.Parse(animation.RenderFrame(t));

	private static XElement Node(XDocument doc, string id) => doc.Descendants().First(e => (string?)e.Attribute("data-id") == id);

	private static double N(XElement e, string attribute) => double.Parse((string)e.Attribute(attribute)!, CultureInfo.InvariantCulture);

	/// <summary>The lowest opacity any wrapper of the element is at: a ghost dims its node's wrapper.</summary>
	private static double Faded(XElement e) =>
		e
			.AncestorsAndSelf()
			.Select(a => Regex.Match((string?)a.Attribute("style") ?? "", @"(?:^|;)opacity:([\d.]+)"))
			.Where(m => m.Success)
			.Select(m => double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
			.DefaultIfEmpty(1)
			.Min();

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
	public void The_verbs_parse_and_say_what_they_take()
	{
		var lines = (Flow +
			"\nanimate\n  step \"x\"\n    ghost cron, cron -> db\n    ghost cron clear\n    group api, db \"Core\" as warn\n    because cron -> api \"locks\" as fail\n    retry api -> db 3 backoff succeeds\n    scale api to 3\n").Split(
			'\n'
		);
		var diagnostics = new List<AnimationDiagnostic>();
		var score = ScoreParser.Parse(lines, ScoreParser.FindStart(lines), diagnostics);
		diagnostics.Should().BeEmpty();
		var s = score.Steps.Single().Statements;
		s[0].Should().BeOfType<GhostStatement>().Which.Should().Match<GhostStatement>(g => g.Targets.Count == 1 && g.Edges.Count == 1 && !g.Clear);
		s[1].Should().BeOfType<GhostStatement>().Which.Clear.Should().BeTrue();
		s[2].Should().BeOfType<GroupStatement>().Which.Should().Match<GroupStatement>(g => g.Label == "Core" && g.Targets.Count == 2 && g.Role == Role.Warn);
		s[3].Should().BeOfType<BecauseStatement>().Which.Should().Match<BecauseStatement>(b => b.From == "cron" && b.To == "api" && b.Reason == "locks");
		s[4].Should().BeOfType<RetryStatement>().Which.Should().Match<RetryStatement>(r => r.Attempts == 3 && r.Backoff && r.Succeeds);
		s[5].Should().BeOfType<ScaleStatement>().Which.Count.Should().Be(3);

		string Step(string statement) => Flow + $"\nanimate\n  step \"x\"\n    {statement}\n    pulse db\n";

		Problems(Step("group api, db")).Should().ContainSingle().Which.Message.Should().StartWith("group takes nodes and a quoted label");
		Problems(Step("because cron \"x\"")).Should().ContainSingle();
		Problems(Step("because cron -> api")).Should().ContainSingle().Which.Message.Should().StartWith("because takes a cause");
		Problems(Step("retry api -> db 0")).Should().ContainSingle().Which.Message.Should().Contain("1 to 9");
		Problems(Step("scale api 3")).Should().ContainSingle().Which.Message.Should().StartWith("scale takes a node");
		Problems(Step("retry web -> db 2")).Should().ContainSingle().Which.Message.Should().Contain("no edge between web and db");
	}

	[Test]
	public void A_ghost_fades_with_a_dashed_outline_until_cleared()
	{
		var animation = MermaidAnimator.Compile(
			Flow + "\nanimate once\n  step \"planned\"\n    ghost cron, cron -> db\n  step \"built\"\n    ghost cron, cron -> db clear\n"
		);
		var planned = At(animation, animation.Steps[0].End);
		Faded(Node(planned, "cron")).Should().BeApproximately(0.38, 0.01);
		var outlines = planned.Descendants().Where(e => (string?)e.Attribute("stroke-dasharray") == "6 5").ToList();
		outlines.Should().HaveCount(2, "one round the node, one along the edge").And.OnlyContain(e => Shown(e));
		Faded(Node(planned, "api")).Should().Be(1, "only the ghost fades");

		var built = At(animation, animation.Steps[1].End);
		Faded(Node(built, "cron")).Should().Be(1);
		built.Descendants().Where(e => (string?)e.Attribute("stroke-dasharray") == "6 5").Should().OnlyContain(e => !Shown(e));
	}

	[Test]
	public void A_group_outlines_its_nodes_for_its_step()
	{
		var animation = MermaidAnimator.Compile(Flow + "\nanimate once\n  step \"one\"\n    group api, db \"Core\"\n  step \"two\"\n    pulse web\n");
		var during = At(animation, animation.Steps[0].End - TimeSpan.FromMilliseconds(200));
		var label = during.Descendants().Single(e => e.Name.LocalName == "text" && e.Value == "Core");
		Shown(label).Should().BeTrue();
		var outline = label.Parent!.Elements().Single(e => e.Name.LocalName == "rect");
		foreach (var id in new[] { "api", "db" })
		{
			var node = Node(during, id).Descendants().First(e => e.Name.LocalName is "rect" or "path");
			var x = node.Attribute("x") is null ? double.NaN : N(node, "x");
			if (!double.IsNaN(x))
				x.Should().BeGreaterThan(N(outline, "x")).And.BeLessThan(N(outline, "x") + N(outline, "width"), $"{id} is inside the outline");
		}
		Shown(At(animation, animation.Steps[1].End).Descendants().Single(e => e.Name.LocalName == "text" && e.Value == "Core")).Should().BeFalse();
	}

	[Test]
	public void Because_draws_from_cause_to_effect_and_its_reason_covers_no_node()
	{
		var animation = MermaidAnimator.Compile(
			Flow + "\nanimate once\n  step \"why\"\n    because cron -> api \"table locks\" as fail\n  step \"next\"\n    pulse web\n"
		);
		var end = At(animation, animation.Steps[0].End - TimeSpan.FromMilliseconds(200));
		var reason = end.Descendants().Single(e => e.Name.LocalName == "text" && e.Value == "table locks");
		Shown(reason).Should().BeTrue();
		var line = reason.Parent!.Parent!.Elements().First(e => (string?)e.Attribute("pathLength") == "1");
		Regex.Match((string?)line.Attribute("style") ?? "", @"stroke-dashoffset:(-?[\d.]+)").Groups[1].Value.Should().Be("0", "the line is drawn");

		// the pill: its centre from the group's translate, its size from its rect
		var group = reason.Parent!;
		var at = Regex.Match((string)group.Attribute("transform")!, @"translate\((-?[\d.]+) (-?[\d.]+)\)");
		var (cx, cy) = (double.Parse(at.Groups[1].Value, CultureInfo.InvariantCulture), double.Parse(at.Groups[2].Value, CultureInfo.InvariantCulture));
		var pill = group.Elements().Single(e => e.Name.LocalName == "rect");
		var (w, h) = (N(pill, "width"), N(pill, "height"));
		var plain = XDocument.Parse(Mermaider.MermaidRenderer.RenderSvg(Flow));
		foreach (var rect in plain.Descendants().Where(e => e.Name.LocalName == "rect" && e.Parent?.Attribute("data-id") is not null))
		{
			var overlaps = Math.Abs(N(rect, "x") + (N(rect, "width") / 2) - cx) < (N(rect, "width") + w) / 2 && Math.Abs(
				N(rect, "y") + (N(rect, "height") / 2) - cy
			) < (N(rect, "height") + h) / 2;
			overlaps.Should().BeFalse($"the reason does not cover {rect.Parent!.Attribute("data-id")!.Value}");
		}
		Shown(At(animation, animation.Steps[1].End).Descendants().Single(e => e.Name.LocalName == "text" && e.Value == "table locks"))
			.Should()
			.BeFalse("it leaves with its step");
	}

	[Test]
	public void Retry_numbers_its_attempts_and_backs_off()
	{
		double FirstSeen(AnimatedDiagram animation, string label)
		{
			for (var t = 0; t <= animation.Duration.TotalMilliseconds; t += 20)
				if (At(animation, TimeSpan.FromMilliseconds(t)).Descendants().Any(e => e.Name.LocalName == "text" && e.Value == label && Shown(e)))
					return t;
			return double.MaxValue;
		}

		var backoff = MermaidAnimator.Compile(Flow + "\nanimate once\n  step \"x\"\n    retry api -> db 3 backoff\n");
		var times = new[] { "1/3", "2/3", "3/3" }.Select(l => FirstSeen(backoff, l)).ToList();
		times.Should().BeInAscendingOrder().And.NotContain(double.MaxValue);
		(times[2] - times[1]).Should().BeGreaterThan(times[1] - times[0] + 100, "each wait is longer than the last");

		var even = MermaidAnimator.Compile(Flow + "\nanimate once\n  step \"x\"\n    retry api -> db 3\n");
		var steady = new[] { "1/3", "2/3", "3/3" }.Select(l => FirstSeen(even, l)).ToList();
		(steady[2] - steady[1]).Should().BeApproximately(steady[1] - steady[0], 45, "without backoff the waits are even");

		// lost attempts turn back halfway; one that succeeds is carried all the way, in the ok colour
		var succeeds = MermaidAnimator.Compile(Flow + "\nanimate once\n  step \"x\"\n    retry api -> db 2 succeeds\n");
		succeeds.Svg.Should().Contain(">2/2<");
		Regex.Matches(backoff.Svg, @"<path [^>]*stroke=""#[0-9a-f]{6}""[^>]*pathLength=""1""").Count.Should().BeGreaterThan(0);
	}

	[Test]
	public void Scale_stacks_replicas_behind_the_node_and_folds_them_back()
	{
		var animation = MermaidAnimator.Compile(Flow + "\nanimate once\n  step \"up\"\n    scale api to 3\n  step \"down\"\n    scale api to 1\n");
		var up = At(animation, animation.Steps[0].End);
		var wrapper = Node(up, "api").Parent!;
		var replicas = wrapper.Elements().Where(e => (string?)e.Attribute("aria-hidden") == "true").ToList();
		replicas.Should().HaveCount(2, "three in all: the node and two replicas");
		replicas.Should().OnlyContain(r => Shown(r));
		var depths = replicas
			.Select(r => Regex.Match((string?)r.Attribute("style") ?? "", @"translate\(([\d.]+)px").Groups[1].Value)
			.Select(v => double.Parse(v, CultureInfo.InvariantCulture))
			.ToList();
		depths.Should().BeEquivalentTo([6.0, 12.0], "each sits a little further behind");
		replicas.Should().OnlyContain(r => r.IsBefore(Node(up, "api")), "behind the node, drawn before it");
		up.Descendants().Any(e => e.Name.LocalName == "text" && e.Value == "×3" && Shown(e)).Should().BeTrue();

		var down = At(animation, animation.Steps[1].End);
		Node(down, "api").Parent!.Elements().Where(e => (string?)e.Attribute("aria-hidden") == "true").Should().OnlyContain(r => !Shown(r));
		down
			.Descendants()
			.Where(e => e.Name.LocalName == "text" && e.Value.StartsWith('×'))
			.Should()
			.OnlyContain(e => !Shown(e), "one is no count worth a badge");
	}
}
