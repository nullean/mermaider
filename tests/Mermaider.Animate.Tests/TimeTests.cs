using System.Text.RegularExpressions;
using System.Xml.Linq;
using AwesomeAssertions;

namespace Mermaider.Animate.Tests;

/// <summary>Gantt sweeps, timeline periods and git commits: diagrams about time.</summary>
public partial class TimeTests
{
	private const string Gantt =
		"""
		gantt
		  dateFormat YYYY-MM-DD
		  section Build
		  Design :d1, 2024-01-01, 5d
		  Build :b1, after d1, 10d
		  Launch :milestone, l1, after b1, 0d
		""";

	private const string Timeline = """
		timeline
		  2019 : First release
		  2020 : Versioned : Rate limits
		  2022 : Gateway
		""";

	private const string Git =
		"""
		gitGraph
		  commit id: "a"
		  commit id: "b"
		  branch feature
		  checkout feature
		  commit id: "c"
		  checkout main
		  merge feature id: "m"
		""";

	[GeneratedRegex("(^|;)opacity:0(;|$)")]
	private static partial Regex Hidden();

	[GeneratedRegex(@"stroke-dashoffset:(-?[\d.]+)")]
	private static partial Regex Offset();

	private static bool IsHidden(XElement e) => e.AncestorsAndSelf().Any(a => Hidden().IsMatch((string?)a.Attribute("style") ?? ""));

	private static XDocument Frame(AnimatedDiagram animation, TimeSpan t) => XDocument.Parse(animation.RenderFrame(t));

	private static double Grown(XDocument frame, string taskId)
	{
		// a bar grows behind a mask: its dash offset runs from 1 (nothing) to 0 (all of it)
		var task = frame.Descendants().Single(e => (string?)e.Attribute("data-id") == taskId);
		var mask = ((string)task.Descendants().First(e => e.Name.LocalName == "rect" && e.Attribute("mask") != null).Attribute("mask")!)[5..^1];
		var path = frame.Descendants().Single(e => (string?)e.Attribute("id") == mask).Elements().Single();
		return 1 - double.Parse(
			Offset().Match((string?)path.Attribute("style") ?? "stroke-dashoffset:0").Groups[1].Value,
			System.Globalization.CultureInfo.InvariantCulture
		);
	}

	[Test]
	public void A_sweep_grows_each_task_up_to_the_playhead_and_names_it_when_reached()
	{
		var animation = MermaidAnimator.Compile(
			Gantt + "\nanimate once\n  step \"design\"\n    sweep to \"2024-01-06\"\n  step \"build\"\n    sweep to \"2024-01-11\"\n"
		);
		var afterDesign = Frame(animation, animation.Steps[0].End);
		Grown(afterDesign, "d1").Should().BeApproximately(1, 0.01);
		Grown(afterDesign, "b1").Should().BeApproximately(0, 0.01);
		var label = afterDesign.Descendants().Single(e => e.Name.LocalName == "text" && e.Value == "Build");
		IsHidden(label).Should().BeFalse("the playhead reached Build's start date");
		IsHidden(Frame(animation, TimeSpan.FromMilliseconds(400)).Descendants().Single(e => e.Name.LocalName == "text" && e.Value == "Build"))
			.Should()
			.BeTrue();

		var halfway = Frame(animation, animation.Steps[1].End);
		Grown(halfway, "b1").Should().BeApproximately(0.5, 0.02, "five of Build's ten days have passed");
	}

	[Test]
	public void A_bare_animate_on_a_gantt_sweeps_the_whole_chart_and_reveals_the_milestone()
	{
		var animation = MermaidAnimator.Compile(Gantt + "\nanimate once\n");
		var end = Frame(animation, animation.ScoreDuration);
		Grown(end, "b1").Should().BeApproximately(1, 0.01);
		var milestone = end.Descendants().Single(e => (string?)e.Attribute("data-id") == "l1");
		milestone.Descendants().Where(e => e.Name.LocalName is "path" or "polygon").Should().OnlyContain(p => !IsHidden(p));
	}

	[Test]
	public void Sweep_belongs_to_gantt_charts()
	{
		var act = () => MermaidAnimator.Compile(Timeline + "\nanimate\n  sweep\n");
		act.Should().Throw<AnimationException>().Which.Diagnostics.Single().Message.Should().Contain("gantt chart's playhead");
	}

	[Test]
	public void A_timeline_period_is_named_by_its_label_and_brings_its_events()
	{
		var animation = MermaidAnimator.Compile(Timeline + "\nanimate once\n  step \"one\"\n    show \"2019\"\n  step \"two\"\n    show \"2020\"\n");

		XElement Event(XDocument doc, string text) => doc.Descendants().Single(e => e.Name.LocalName == "text" && e.Value == text);

		var first = Frame(animation, animation.Steps[0].End);
		IsHidden(Event(first, "First release")).Should().BeFalse();
		IsHidden(Event(first, "Rate limits")).Should().BeTrue();
		var second = Frame(animation, animation.Steps[1].End);
		IsHidden(Event(second, "Rate limits")).Should().BeFalse();
	}

	[Test]
	public void Commits_land_in_order_after_the_links_into_them()
	{
		var animation = MermaidAnimator.Compile(Git + "\nanimate once\n");

		double FirstSeen(string id)
		{
			for (var t = 0.0; t <= animation.ScoreDuration.TotalMilliseconds; t += 50)
				if (!IsHidden(Frame(animation, TimeSpan.FromMilliseconds(t)).Descendants().Single(e => (string?)e.Attribute("data-id") == id)))
					return t;
			return double.MaxValue;
		}

		new[] { "a", "b", "c", "m" }.Select(FirstSeen).Should().BeInAscendingOrder().And.NotContain(double.MaxValue);

		// every link starts undrawn and is drawn by the time the commit it leads to has landed
		double LinkInk(XDocument frame, XElement link)
		{
			var id = ((string)frame
				.Descendants()
				.Single(
					e => (string?)e.Attribute("data-from") == (string?)link.Attribute("data-from") && (string?)e.Attribute("data-to") ==
						(string?)link.Attribute("data-to")
				)
				.Attribute("mask")!)[5..^1];
			var path = frame.Descendants().Single(e => (string?)e.Attribute("id") == id).Elements().Single();
			return double.Parse(
				Offset().Match((string?)path.Attribute("style") ?? "stroke-dashoffset:9").Groups[1].Value,
				System.Globalization.CultureInfo.InvariantCulture
			);
		}

		var start = Frame(animation, TimeSpan.Zero);
		var links = start.Descendants().Where(e => ((string?)e.Attribute("class") ?? "").Contains("gitgraph-link")).ToList();
		links.Should().NotBeEmpty();
		links.Select(l => LinkInk(start, l)).Should().OnlyContain(v => v > 0.99);
		var end = Frame(animation, animation.ScoreDuration);
		links.Select(l => LinkInk(end, l)).Should().OnlyContain(v => Math.Abs(v) < 0.01);
	}

	[Test]
	public void Elements_without_ids_are_named_by_label_or_number()
	{
		const string unnamed =
			"""
			gantt
			  dateFormat YYYY-MM-DD
			  Design : 2024-01-01, 5d
			  Build : 2024-01-06, 5d
			""";
		MermaidAnimator.Compile(unnamed + "\nanimate\n  step \"x\"\n    sweep\n    mark \"Build\" ok\n    badge #1 \"done\"\n").Steps.Should().HaveCount(1);
		var act = () => MermaidAnimator.Compile(unnamed + "\nanimate\n  mark \"Deploy\" ok\n");
		act.Should().Throw<AnimationException>().Which.Diagnostics.Single().Message.Should().Contain("\"Design\"").And.Contain("\"Build\"");
	}
}
