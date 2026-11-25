using System.Text.RegularExpressions;
using System.Xml.Linq;
using AwesomeAssertions;

namespace Mermaider.Animate.Tests;

/// <summary>Quadrant, treemap, venn, packet, journey and requirement diagrams: marks arrive in order, with their parts.</summary>
public partial class OrderedTypeTests
{
	[GeneratedRegex("(^|;)opacity:0(;|$)")]
	private static partial Regex Hidden();

	private static bool IsHidden(XElement e) => e.AncestorsAndSelf().Any(a => Hidden().IsMatch((string?)a.Attribute("style") ?? ""));

	private static double FirstSeen(AnimatedDiagram animation, Func<XDocument, XElement> find)
	{
		for (var t = 0.0; t <= animation.ScoreDuration.TotalMilliseconds; t += 40)
			if (!IsHidden(find(XDocument.Parse(animation.RenderFrame(TimeSpan.FromMilliseconds(t))))))
				return t;
		return double.MaxValue;
	}

	private static XElement Text(XDocument doc, string text) => doc.Descendants().First(e => e.Name.LocalName == "text" && e.Value == text);

	[Test]
	public void Quadrant_points_arrive_in_order()
	{
		var animation = MermaidAnimator.Compile("quadrantChart\n  A: [0.2, 0.8]\n  B: [0.7, 0.3]\n  C: [0.5, 0.5]\n\nanimate once\n");
		new[] { "A", "B", "C" }.Select(l => FirstSeen(animation, d => Text(d, l))).Should().BeInAscendingOrder().And.NotContain(double.MaxValue);
	}

	[Test]
	public void A_treemap_section_and_its_header_come_before_its_tiles()
	{
		const string source = "treemap-beta\n\"Budget\"\n  \"Eng\"\n    \"Platform\": 40\n    \"Product\": 30\n  \"Sales\": 20\n\nanimate once\n";
		var animation = MermaidAnimator.Compile(source);
		var header = FirstSeen(animation, d => Text(d, "Eng"));
		var tile = FirstSeen(animation, d => Text(d, "Platform"));
		header.Should().BeLessThan(tile);
		IsHidden(Text(XDocument.Parse(animation.RenderFrame(TimeSpan.Zero)), "Budget")).Should().BeTrue("a header waits for its section");
	}

	[Test]
	public void Venn_labels_arrive_with_their_sets_and_the_overlap_with_the_last()
	{
		var animation = MermaidAnimator.Compile("venn-beta\n  set A\n  set B\n  union A,B[\"Both\"]\n\nanimate once\n");
		var a = FirstSeen(animation, d => Text(d, "A"));
		var b = FirstSeen(animation, d => Text(d, "B"));
		var both = FirstSeen(animation, d => Text(d, "Both"));
		a.Should().BeLessThan(b);
		both.Should().BeGreaterThanOrEqualTo(b);
	}

	[Test]
	public void Packet_fields_fill_in_bit_order()
	{
		var animation = MermaidAnimator.Compile("packet-beta\n  0-15: \"One\"\n  16-31: \"Two\"\n  32-63: \"Three\"\n\nanimate once\n");
		new[] { "One", "Two", "Three" }.Select(l => FirstSeen(animation, d => Text(d, l))).Should().BeInAscendingOrder();
	}

	[Test]
	public void A_journey_task_brings_its_face_and_the_line_draws_through()
	{
		var animation = MermaidAnimator.Compile("journey\n  section S\n    Browse: 5: Me\n    Pay: 1: Me\n\nanimate once\n");
		var start = XDocument.Parse(animation.RenderFrame(TimeSpan.Zero));
		start.Descendants().Where(e => ((string?)e.Attribute("class") ?? "") == "mouth").Should().OnlyContain(m => IsHidden(m));
		start.Descendants().Single(e => ((string?)e.Attribute("class") ?? "").Contains("journey-line")).Attribute("mask").Should().NotBeNull();
	}

	[Test]
	public void Requirement_relations_carry_tokens()
	{
		const string source =
			"requirementDiagram\n  requirement r {\n    id: 1\n    text: x\n  }\n  element e {\n    type: svc\n  }\n  e - satisfies -> r\n\nanimate\n  send e -> r \"satisfies\"\n";
		MermaidAnimator.Compile(source).Svg.Should().Contain(">satisfies<");
	}

	[Test]
	public void A_badge_can_name_its_target_by_label()
	{
		var animation = MermaidAnimator.Compile("quadrantChart\n  Search: [0.2, 0.8]\n\nanimate once\n  step \"x\"\n    badge \"Search\" \"do first\" as ok\n");
		animation.Svg.Should().Contain(">do first<");
	}
}
