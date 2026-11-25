using System.Text.RegularExpressions;
using System.Xml.Linq;
using AwesomeAssertions;

namespace Mermaider.Animate.Tests;

public partial class AnimatorTests
{
	private const string Flow = """
		flowchart LR
		  client[Client] --> lb[Balancer]
		  lb --> api[API]
		  api --> db[(DB)]
		""";

	private static string StyleOf(string svg)
	{
		var root = XDocument.Parse(svg).Root!;
		var styles = root.Elements().Where(e => e.Name.LocalName == "style").ToList();
		styles.Should().HaveCountGreaterThanOrEqualTo(2, "the renderer's stylesheet and the animation's");
		return styles[0].Value;
	}

	[Test]
	public void Without_an_animate_block_the_diagram_renders_as_it_always_did()
	{
		MermaidAnimator.HasAnimation(Flow).Should().BeFalse();
		MermaidAnimator.RenderSvg(Flow).Should().Be(MermaidRenderer.RenderSvg(Flow));
	}

	[Test]
	public void Build_brings_a_flowchart_in_in_flow_order()
	{
		var animation = MermaidAnimator.Compile(Flow + "\nanimate once\n  build\n");
		animation.Steps.Should().HaveCount(1);
		XDocument.Parse(animation.Svg).Should().NotBeNull();

		// at the start nothing is visible; when the score ends everything is
		var first = Visible(animation.RenderFrame(TimeSpan.Zero));
		first.Should().BeEmpty();
		var last = Visible(animation.RenderFrame(animation.ScoreDuration));
		last.Should().BeEquivalentTo(["client", "lb", "api", "db"]);

		// nodes appear left to right
		var order = new[] { "client", "lb", "api", "db" }.Select(id => FirstSeen(animation, id)).ToList();
		order.Should().BeInAscendingOrder();
	}

	[Test]
	public void Unknown_names_are_reported_with_what_the_diagram_has()
	{
		var act = () => MermaidAnimator.Compile(Flow + "\nanimate\n  send client -> nope\n  send client -> db\n");
		var error = act.Should().Throw<AnimationException>().Which;
		error.Diagnostics.Should().HaveCount(2);
		error.Diagnostics[0].Message.Should().Contain("no node 'nope'").And.Contain("Known: client, lb, api, db");
		error.Diagnostics[1].Message.Should().Contain("no edge between client and db").And.Contain("goes to: lb");
		error.Diagnostics.Select(d => d.Line).Should().Equal(6, 7);
	}

	[Test]
	public void A_run_through_a_missing_transition_says_which_ones_exist()
	{
		const string machine =
			"""
			stateDiagram-v2
			  [*] --> Idle
			  Idle --> Loading : fetch
			  Loading --> Ready : ok
			animate
			  run Idle -> Ready
			""";
		var act = () => MermaidAnimator.Compile(machine);
		act
			.Should()
			.Throw<AnimationException>()
			.Which
			.Diagnostics
			.Single()
			.Message
			.Should()
			.Be("There is no transition Idle -> Ready. From Idle the diagram allows: Loading.");
	}

	[Test]
	public void A_token_travels_the_edge_and_is_on_it_halfway()
	{
		var animation = MermaidAnimator.Compile(Flow + "\nanimate once\n  send client -> lb \"GET\"\n");
		var svg = XDocument.Parse(MermaidRenderer.RenderSvg(Flow));
		var edge = svg.Descendants().Single(e => (string?)e.Attribute("data-from") == "client" && (string?)e.Attribute("data-to") == "lb");
		var y = double.Parse(Regex.Match((string)edge.Attribute("d")!, @"M[\d.]+,([\d.]+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

		var step = animation.Steps.Single();
		var positions = Enumerable
			.Range(1, 9)
			.Select(i => step.Start + TimeSpan.FromMilliseconds(i * (step.End - step.Start).TotalMilliseconds / 10))
			.Select(t => TokenAt(animation.RenderFrame(t)))
			.Where(p => p is not null)
			.Select(p => p!.Value)
			.ToList();
		positions.Should().NotBeEmpty();
		positions.Should().OnlyContain(p => Math.Abs(p.Y - y) < 0.01, "the edge is a horizontal line");
		positions.Select(p => p.X).Should().BeInAscendingOrder("the token moves from client towards the balancer");
	}

	[Test]
	public void Many_animations_on_one_page_never_share_a_class_or_keyframe()
	{
		var a = MermaidAnimator.RenderSvg(Flow + "\nanimate\n");
		var b = MermaidAnimator.RenderSvg(Flow + "\nanimate crisp\n");
		var classes = new Func<string, HashSet<string>>(svg => [.. Regex.Matches(StyleOf(svg), @"\.(ma[0-9a-f]{8}-[a-z]\d+)").Select(m => m.Groups[1].Value)]);
		classes(a).Should().NotBeEmpty();
		classes(a).Overlaps(classes(b)).Should().BeFalse();
	}

	[Test]
	public void Output_is_deterministic()
	{
		var source = Flow + "\nanimate\n  step \"x\"\n    tour client -> lb -> api\n";
		MermaidAnimator.RenderSvg(source).Should().Be(MermaidAnimator.RenderSvg(source));
	}

	[Test]
	public void Reduced_motion_keeps_content_changes_and_stops_movement()
	{
		var svg = MermaidAnimator.RenderSvg(Flow + "\nanimate\n  step \"go\"\n    send client -> lb\n    mark lb ok\n");
		var style = StyleOf(svg);
		var reduced = style[style.IndexOf("@media (prefers-reduced-motion: reduce)", StringComparison.Ordinal)..];
		var doc = XDocument.Parse(svg);

		string ClassOf(XElement e) => ((string)e.Attribute("class")!).Split(' ').Last();

		// the token moves, so under reduced motion it does not animate at all (and its base opacity hides it)
		var token = doc.Descendants().Single(
			e => e.Name.LocalName == "g" && e.Elements().Any(c => c.Name.LocalName == "circle") && e.Attribute("class") is not null
		);
		reduced.Should().Contain($".{ClassOf(token)} {{ animation: none; }}");

		// the mark is a content change: it still happens, as a cut
		var mark = doc.Descendants().Single(e => e.Name.LocalName == "rect" && (string?)e.Attribute("fill-opacity") == "0.14").Parent!;
		var keyframes = Regex.Match(reduced, $@"\.{ClassOf(mark)} \{{ animation: (ma[0-9a-f]{{8}}-k\d+) ").Groups[1].Value;
		keyframes.Should().NotBeEmpty();
		style.Should().MatchRegex($@"@keyframes {keyframes} \{{[^\n]*steps\(1, jump-start\)");
	}

	[Test]
	public void Still_renders_the_last_frame_without_any_animation()
	{
		var svg = MermaidAnimator.RenderSvg(Flow + "\nanimate still\n  step \"go\"\n    mark api fail\n");
		svg.Should().NotContain("@keyframes");
		XDocument
			.Parse(svg)
			.Descendants()
			.Should()
			.Contain(e => (string?)e.Attribute("style") == "opacity:1" && e.Elements().Any(r => (string?)r.Attribute("fill-opacity") == "0.14"));
	}

	[Test]
	public void Scrub_binds_every_animation_to_a_view_timeline_behind_supports()
	{
		var svg = MermaidAnimator.RenderSvg(Flow + "\nanimate scrub\n");
		var style = StyleOf(svg);
		style.Should().Contain("@supports (animation-timeline: view())");
		style.Should().MatchRegex(@"view-timeline-name: --ma[0-9a-f]{8};");
		style.Should().NotContain("infinite");
	}

	[Test]
	public void Sequence_plays_every_message_in_order()
	{
		const string sequence =
			"""
			sequenceDiagram
			  A->>B: one
			  B-->>A: two
			  A->>C: three
			animate once
			""";
		var animation = MermaidAnimator.Compile(sequence);
		var doc = XDocument.Parse(animation.Svg);
		doc.Descendants().Count(e => e.Name.LocalName == "mask").Should().Be(3);
		var labels = new[] { "one", "two", "three" }.Select(label => FirstShown(animation, label)).ToList();
		labels.Should().BeInAscendingOrder();
	}

	// ---- helpers ------------------------------------------------------------------------------------------------

	/// <summary>Ids of the nodes visible in a frame: nothing around them is at opacity 0.</summary>
	private static List<string> Visible(string frame) =>
		XDocument
			.Parse(frame)
			.Descendants()
			.Where(e => ((string?)e.Attribute("class") ?? "").Split(' ').Contains("node") && e.Attribute("data-id") is not null)
			.Where(e => !IsHidden(e))
			.Select(e => (string)e.Attribute("data-id")!)
			.ToList();

	private static bool IsHidden(XElement element) => element.AncestorsAndSelf().Any(a => HiddenPattern().IsMatch((string?)a.Attribute("style") ?? ""));

	[GeneratedRegex(@"(^|;)opacity:0(;|$)")]
	private static partial Regex HiddenPattern();

	private static double FirstSeen(AnimatedDiagram animation, string id)
	{
		for (var t = 0.0; t <= animation.ScoreDuration.TotalMilliseconds; t += 50)
			if (Visible(animation.RenderFrame(TimeSpan.FromMilliseconds(t))).Contains(id))
				return t;
		return double.MaxValue;
	}

	private static double FirstShown(AnimatedDiagram animation, string label)
	{
		for (var t = 0.0; t <= animation.ScoreDuration.TotalMilliseconds; t += 50)
		{
			var text = XDocument
				.Parse(animation.RenderFrame(TimeSpan.FromMilliseconds(t)))
				.Descendants()
				.Single(e => e.Name.LocalName == "text" && e.Value == label);
			var hidden = IsHidden(text);
			if (!hidden)
				return t;
		}
		return double.MaxValue;
	}

	private static (double X, double Y)? TokenAt(string frame)
	{
		var token = XDocument
			.Parse(frame)
			.Descendants()
			.Where(e => e.Name.LocalName == "g" && e.Elements().Any(c => c.Name.LocalName == "circle"))
			.Select(e => (string?)e.Attribute("style"))
			.FirstOrDefault(s => s is not null && s.Contains("translate(", StringComparison.Ordinal) && !HiddenPattern().IsMatch(s));
		if (token is null)
			return null;
		var m = TranslatePattern().Match(token);
		return (double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), double.Parse(
			m.Groups[2].Value,
			System.Globalization.CultureInfo.InvariantCulture
		));
	}

	[GeneratedRegex(@"translate\((-?[\d.]+)px,(-?[\d.]+)px\)")]
	private static partial Regex TranslatePattern();
}
