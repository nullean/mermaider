using System.Text.RegularExpressions;
using System.Xml.Linq;
using AwesomeAssertions;

namespace Mermaider.Animate.Tests;

/// <summary>What a screen reader and a printer get from an animated diagram.</summary>
public class AccessibilityTests
{
	private const string Flow = """
		flowchart LR
		  client[Client] --> lb[Balancer]
		  lb --> api[API]
		""";

	private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

	private static XElement Root(string svg) => XDocument.Parse(svg).Root!;

	[Test]
	public void The_description_tells_the_steps_in_order()
	{
		var root = Root(
			MermaidAnimator.RenderSvg(
				Flow + "\nanimate\n  build\n  step \"A request arrives\"\n    send client -> lb\n  step \"Is it healthy?\"\n    mark api fail\n"
			)
		);
		root.Element(Svg + "desc")!.Value.Should().Be("In 2 steps: 1. A request arrives. 2. Is it healthy?");
	}

	[Test]
	public void A_single_caption_is_the_description()
	{
		var root = Root(MermaidAnimator.RenderSvg(Flow + "\nanimate\n  step \"The balancer forwards it\"\n    send lb -> api\n"));
		root.Element(Svg + "desc")!.Value.Should().Be("The balancer forwards it.");
	}

	[Test]
	public void The_steps_follow_the_author_s_own_description_and_title()
	{
		var source = Flow.Replace("flowchart LR\n", "flowchart LR\n  accTitle: Orders\n  accDescr: How an order reaches the API\n") +
			"\nanimate\n  step \"Sent\"\n    send client -> lb\n  step \"Forwarded\"\n    send lb -> api\n";
		var root = Root(MermaidAnimator.RenderSvg(source));
		root.Elements(Svg + "desc").Should().ContainSingle().Which.Value.Should().Be("How an order reaches the API. In 2 steps: 1. Sent. 2. Forwarded.");
		var children = root.Elements().Select(e => e.Name.LocalName).ToList();
		children.IndexOf("desc").Should().Be(children.IndexOf("title") + 1, "the description follows the title");
		((string?)root.Attribute("aria-label")).Should().Be("Orders");
	}

	[Test]
	public void Without_captions_nothing_is_added()
	{
		var root = Root(MermaidAnimator.RenderSvg(Flow + "\nanimate\n  build\n"));
		root.Element(Svg + "desc").Should().BeNull();
	}

	[Test]
	public void Drawn_captions_and_tokens_are_hidden_from_screen_readers_and_lasting_marks_are_not()
	{
		var root = Root(
			MermaidAnimator.RenderSvg(Flow + "\nanimate\n  step \"First\"\n    send client -> lb \"GET /orders\"\n  step \"Second\"\n    badge api \"slow\"\n")
		);

		bool Hidden(XElement e) => e.AncestorsAndSelf().Any(a => (string?)a.Attribute("aria-hidden") == "true");

		var texts = root.Descendants(Svg + "text").ToList();
		// the captions (each with its counter) and the token's label are in the description or only in motion
		texts.Where(Hidden).Select(t => t.Value).Should().BeEquivalentTo(["1/2", "First", "2/2", "Second", "GET /orders"]);
		// the diagram's own labels and a badge, which stays until cleared, are read
		texts.Where(t => !Hidden(t)).Select(t => t.Value).Should().Contain(["Client", "Balancer", "API", "slow"]);
	}

	[Test]
	public void Printed_it_shows_how_the_story_ends()
	{
		var animation = MermaidAnimator.Compile(Flow + "\nanimate\n  step \"Down\"\n    mark api fail\n  step \"Still down\"\n    pulse lb\n");
		var svg = animation.Svg;
		var print = svg[svg.IndexOf("@media print {", StringComparison.Ordinal)..];
		print = print[..(print.IndexOf("\n}\n", StringComparison.Ordinal) + 3)];

		// every animated element stops and takes the frame where the score ends
		var animated = Regex.Matches(svg, @"(?m)^\.(ma[0-9a-f]{8}-a\d+) \{ animation:").Select(m => m.Groups[1].Value).ToList();
		var printed = Regex.Matches(print, @"\.(ma[0-9a-f]{8}-a\d+) \{ animation: none; ([^}]*); \}").ToDictionary(
			m => m.Groups[1].Value,
			m => m.Groups[2].Value
		);
		printed.Keys.Should().BeEquivalentTo(animated);

		var poster = Root(animation.RenderFrame(animation.ScoreDuration));
		foreach (var element in poster.Descendants().Where(e => e.Attribute("class") is { } c && Regex.IsMatch(c.Value, @"-a\d+\b")))
		{
			var cls = Regex.Match((string)element.Attribute("class")!, @"ma[0-9a-f]{8}-a\d+").Value;
			foreach (var declaration in printed[cls].Split("; "))
				((string)element.Attribute("style")!).Should().Contain(declaration, $"{cls} prints as the poster shows it");
		}
		// the print rules come last, so they win over the animations and the first-frame styles before them
		svg.LastIndexOf("@media print", StringComparison.Ordinal).Should().BeGreaterThan(svg.LastIndexOf("@supports", StringComparison.Ordinal));
	}
}
