using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AwesomeAssertions;
using Mermaider.Animate.Score;

namespace Mermaider.Animate.Tests;

/// <summary>becomes: the diagram moves into its next version.</summary>
public partial class BecomesTests
{
	private const string Monolith =
		"""
		flowchart LR
		  web[Web] --> app[Monolith]
		  app --> db[(Database)]

		animate once
		  step "before"
		    pulse app
		  step "split"
		    becomes app -> gw, orders, billing
		      flowchart LR
		        web[Web] --> gw[Gateway]
		        gw --> orders[Orders]
		        gw --> billing[Billing]
		        orders --> db[(Orders DB)]
		        billing --> bdb[(Billing DB)]
		  step "after"
		    tour web -> gw -> orders "GET"
		""";

	[GeneratedRegex("(^|;)opacity:0(;|$)")]
	private static partial Regex Hidden();

	private static bool Shown(XElement e) =>
		!e.AncestorsAndSelf().Any(
			a => Hidden().IsMatch((string?)a.Attribute("style") ?? "") || ((string?)a.Attribute("style") is null && (string?)a.Attribute("opacity") == "0")
		);

	private static List<XElement> Texts(XDocument doc, string text) => [.. doc.Descendants().Where(e => e.Name.LocalName == "text" && e.Value == text)];

	private static bool Visible(XDocument doc, string text) => Texts(doc, text).Any(Shown);

	/// <summary>The translation on the nearest moved ancestor of the visible element with this text.</summary>
	private static (double X, double Y) Moved(XDocument doc, string text)
	{
		var element = Texts(doc, text).First(Shown);
		foreach (var a in element.AncestorsAndSelf())
			if (Regex.Match((string?)a.Attribute("style") ?? "", @"translate\((-?[\d.]+)px,\s*(-?[\d.]+)px\)") is { Success: true } m)
				return (double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
		return (0, 0);
	}

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
	public void Becomes_takes_the_diagram_indented_under_it()
	{
		var lines = Monolith.Replace("\r\n", "\n").Split('\n');
		var diagnostics = new List<AnimationDiagnostic>();
		var score = ScoreParser.Parse(lines, ScoreParser.FindStart(lines), diagnostics);
		diagnostics.Should().BeEmpty();
		var becomes = score.Steps[1].Statements.Single().Should().BeOfType<BecomesStatement>().Subject;
		becomes.Source.Split('\n')[0].Should().Be("flowchart LR", "the block is dedented to its own margin");
		becomes.Source.Split('\n')[1].Should().Be("  web[Web] --> gw[Gateway]");
		lines[becomes.FirstLine - 1].Trim().Should().Be("flowchart LR");
		becomes.From.Select(t => t.Id).Should().Equal("app");
		becomes.Into.Select(t => t.Id).Should().Equal("gw", "orders", "billing");
		score.Steps[2].Statements.Single().Should().BeOfType<TourStatement>("the step after the block goes on as usual");

		Problems("flowchart LR\n  a --> b\n\nanimate\n  step \"x\"\n    becomes\n  step \"y\"\n    pulse a\n")
			.Should()
			.Contain(d => d.Line == 6 && d.Message.Contains("needs the next version"));
		Problems("flowchart LR\n  a --> b\n\nanimate\n  step \"x\"\n    becomes a, b -> c, d\n      flowchart LR\n        c --> d\n")
			.Should()
			.Contain(d => d.Message.Contains("not both"));
		Problems("flowchart LR\n  a --> b\n\nanimate\n  step \"x\"\n    becomes\n      sequenceDiagram\n        a->>b: hi\n")
			.Should()
			.ContainSingle()
			.Which
			.Should()
			.Match<AnimationDiagnostic>(d => d.Line == 7 && d.Message.Contains("same type"));
		Problems("flowchart LR\n  a --> b\n\nanimate\n  highlight a\n  step \"x\"\n    becomes\n      flowchart LR\n        a --> c\n")
			.Should()
			.ContainSingle()
			.Which
			.Message
			.Should()
			.Contain("cannot follow a diagram that becomes another");
	}

	[Test]
	public void The_versions_swap_and_the_score_goes_on_with_the_next()
	{
		var animation = MermaidAnimator.Compile(Monolith);
		var before = XDocument.Parse(animation.RenderFrame(animation.Steps[0].End));
		var after = XDocument.Parse(animation.RenderFrame(animation.Steps[1].End));
		Visible(before, "Monolith").Should().BeTrue();
		Visible(before, "Gateway").Should().BeFalse("the next version waits, hidden");
		Texts(before, "Web").Count(Shown).Should().Be(1, "the next version's Web is hidden with it");
		Visible(after, "Monolith").Should().BeFalse("the monolith is not in the next version");
		new[] { "Web", "Gateway", "Orders", "Billing", "Orders DB", "Billing DB" }.Should().OnlyContain(t => Visible(after, t));

		// before the swap the shared Web has moved to where the next version has it
		var step = animation.Steps[1];
		var frames = Enumerable.Range(1, 99).Select(p => XDocument.Parse(animation.RenderFrame(step.Start + ((step.End - step.Start) * (p / 100.0))))).ToList();
		var last = frames.Last(d => !Visible(d, "Gateway"));
		var moved = Moved(last, "Web");
		(Math.Abs(moved.X) + Math.Abs(moved.Y)).Should().BeGreaterThan(10);

		// what is new arrives after the swap, not with it
		var swapped = frames.First(d => Visible(d, "Gateway"));
		Visible(swapped, "Billing DB").Should().BeFalse("Billing DB waits for the split to reach its places");
		Visible(after, "Database").Should().BeFalse("a node whose label changed crossfades into its new one");
		Texts(after, "Web").Count(Shown).Should().Be(1, "the old and new Web are never both shown once the swap is done");
		animation.Svg.Should().Contain(">GET<", "the step after names the next version's nodes");
	}

	[Test]
	public void The_picture_holds_both_versions()
	{
		var first = XDocument.Parse(MermaidAnimator.RenderSvg("flowchart LR\n  a --> b\n\nanimate\n  step \"x\"\n    pulse a\n"));
		var both = XDocument.Parse(
			MermaidAnimator.RenderSvg(
				"flowchart LR\n  a --> b\n\nanimate\n  step \"x\"\n    becomes\n      flowchart LR\n        a --> b\n        b --> c\n        c --> d\n"
			)
		);

		double Width(XDocument d) => double.Parse(((string)d.Root!.Attribute("viewBox")!).Split(' ')[2], CultureInfo.InvariantCulture);

		var next = XDocument.Parse(Mermaider.MermaidRenderer.RenderSvg("flowchart LR\n  a --> b\n  b --> c\n  c --> d\n"));
		Width(both).Should().BeGreaterThan(Width(first)).And.BeGreaterThanOrEqualTo(Width(next));
	}

	[Test]
	public void A_split_comes_out_of_its_source()
	{
		var animation = MermaidAnimator.Compile(Monolith);
		var step = animation.Steps[1];
		// a moment after the swap the gateway is shown, near where the monolith stood, and moving away from it
		XDocument At(double fraction) => XDocument.Parse(animation.RenderFrame(step.Start + ((step.End - step.Start) * fraction)));

		var early = Enumerable.Range(30, 40).Select(p => At(p / 100.0)).First(d => Visible(d, "Gateway"));
		var offset = Moved(early, "Orders");
		(Math.Abs(offset.X) + Math.Abs(offset.Y)).Should().BeGreaterThan(20, "orders starts at the monolith's place, not its own");
		Moved(XDocument.Parse(animation.RenderFrame(step.End)), "Orders").Should().Be((0, 0), "and ends at its own");
	}

	[Test]
	public void With_motion_reduced_a_split_rests_in_place()
	{
		// with movement off, what stays is each element's first frame: a new node's has to be its own place
		var svg = MermaidAnimator.RenderSvg(Monolith);
		var doc = XDocument.Parse(svg);
		var classes = doc
			.Descendants()
			.First(e => (string?)e.Attribute("data-id") == "orders")
			.AncestorsAndSelf()
			.SelectMany(a => ((string?)a.Attribute("class") ?? "").Split(' '))
			.Where(c => Regex.IsMatch(c, @"^ma[0-9a-f]{8}-a\d+$"))
			.ToList();
		classes.Should().NotBeEmpty();
		var firstFrames = svg[svg.IndexOf("@supports (timeline-trigger", StringComparison.Ordinal)..];
		foreach (var cls in classes)
		{
			var rule = Regex.Match(firstFrames, $@"\.{cls} \{{([^}}]*)\}}").Groups[1].Value;
			var moved = Regex.Match(rule, @"translate\((-?[\d.]+)px,(-?[\d.]+)px\)");
			if (moved.Success)
				(Math.Abs(double.Parse(moved.Groups[1].Value, CultureInfo.InvariantCulture)) + Math.Abs(
					double.Parse(moved.Groups[2].Value, CultureInfo.InvariantCulture)
				)).Should().BeLessThan(0.01, $".{cls} starts where the node is");
		}
	}

	[Test]
	public void A_merge_takes_its_parts_into_the_new_node()
	{
		const string merge =
			"""
			flowchart LR
			  lb --> api1
			  lb --> api2

			animate once
			  step "one service"
			    becomes api1, api2 -> api
			      flowchart LR
			        lb --> api
			""";
		var animation = MermaidAnimator.Compile(merge);
		var step = animation.Steps[0];
		var before = XDocument.Parse(animation.RenderFrame(step.Start));
		// just before the swap both parts are still shown, and they have moved toward each other
		var frames = Enumerable.Range(1, 60).Select(p => XDocument.Parse(animation.RenderFrame(step.Start + ((step.End - step.Start) * (p / 100.0))))).ToList();
		var converged = frames.Last(d => Visible(d, "api1") && Visible(d, "api2"));

		double Top(XDocument d, string id, (double X, double Y) moved) =>
			double.Parse(
				(string)d.Descendants().First(e => (string?)e.Attribute("data-id") == id).Descendants().First(e => e.Name.LocalName == "rect").Attribute("y")!,
				CultureInfo.InvariantCulture
			) + moved.Y;

		var apart = Math.Abs(Top(before, "api2", (0, 0)) - Top(before, "api1", (0, 0)));
		apart.Should().BeGreaterThan(20);
		Math
			.Abs(Top(converged, "api2", Moved(converged, "api2")) - Top(converged, "api1", Moved(converged, "api1")))
			.Should()
			.BeLessThan(2, "the parts meet where the new node is");
		Moved(before, "api1").Should().Be((0, 0));
		Visible(XDocument.Parse(animation.RenderFrame(step.End)), "api").Should().BeTrue();
	}

	[Test]
	public void A_callout_after_becomes_points_into_the_next_version()
	{
		var animation = MermaidAnimator.Compile(Monolith.Replace("    tour web -> gw -> orders \"GET\"", "    callout billing \"its own database next\""));
		var end = XDocument.Parse(animation.RenderFrame(animation.Steps[2].End - TimeSpan.FromMilliseconds(300)));
		end
			.Descendants()
			.Where(e => e.Name.LocalName == "text" && e.Value.StartsWith("its own", StringComparison.Ordinal))
			.Should()
			.ContainSingle()
			.Which
			.Should()
			.Match<XElement>(e => Shown(e));
	}

	[Test]
	public void A_loop_returns_to_the_first_version()
	{
		var animation = MermaidAnimator.Compile(Monolith.Replace("animate once", "animate"));
		var end = XDocument.Parse(animation.RenderFrame(animation.Duration - TimeSpan.FromMilliseconds(1)));
		Visible(end, "Monolith").Should().BeTrue();
		Visible(end, "Gateway").Should().BeFalse();
		Moved(end, "Web").Should().Be((0, 0));
	}
}
