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
		var animation = MermaidAnimator.Compile("quadrantChart\n  A: [0.2, 0.8]\n  B: [0.7, 0.3]\n  C: [0.5, 0.5]\n\nanimate once\n  build\n");
		new[] { "A", "B", "C" }.Select(l => FirstSeen(animation, d => Text(d, l))).Should().BeInAscendingOrder().And.NotContain(double.MaxValue);
	}

	[Test]
	public void A_treemap_section_and_its_header_come_before_its_tiles()
	{
		const string source = "treemap-beta\n\"Budget\"\n  \"Eng\"\n    \"Platform\": 40\n    \"Product\": 30\n  \"Sales\": 20\n\nanimate once\n  build\n";
		var animation = MermaidAnimator.Compile(source);
		var header = FirstSeen(animation, d => Text(d, "Eng"));
		var tile = FirstSeen(animation, d => Text(d, "Platform"));
		header.Should().BeLessThan(tile);
		IsHidden(Text(XDocument.Parse(animation.RenderFrame(TimeSpan.Zero)), "Budget")).Should().BeTrue("a header waits for its section");
	}

	[Test]
	public void Venn_labels_arrive_with_their_sets_and_the_overlap_with_the_last()
	{
		var animation = MermaidAnimator.Compile("venn-beta\n  set A\n  set B\n  union A,B[\"Both\"]\n\nanimate once\n  build\n");
		var a = FirstSeen(animation, d => Text(d, "A"));
		var b = FirstSeen(animation, d => Text(d, "B"));
		var both = FirstSeen(animation, d => Text(d, "Both"));
		a.Should().BeLessThan(b);
		both.Should().BeGreaterThanOrEqualTo(b);
	}

	[Test]
	public void A_labelled_venn_overlap_lights_the_region_its_sets_share_after_them()
	{
		const string source = "venn-beta\n  set A\n  set B\n  union A,B[\"Both\"]\n\nanimate once\n  build\n";
		var animation = MermaidAnimator.Compile(source);
		var doc = XDocument.Parse(animation.Svg);
		// the region is a fill clipped by each set's circle in turn, after the circles and before the names
		var region = doc.Descendants().Single(e => e.Name.LocalName == "rect" && (string?)e.Attribute("fill-opacity") == "0.32");
		var clips = region.Ancestors().Select(g => (string?)g.Attribute("clip-path")).OfType<string>().ToList();
		clips.Should().HaveCount(2);
		var circles = clips
			.Select(c => doc.Descendants().Single(e => e.Name.LocalName == "clipPath" && $"url(#{(string?)e.Attribute("id")})" == c))
			.Select(c => c.Elements().Single())
			.Select(c => ((string?)c.Attribute("cx"), (string?)c.Attribute("cy")))
			.ToList();
		var sets = doc
			.Descendants()
			.Where(e => e.Name.LocalName == "g" && ((string?)e.Attribute("class") ?? "").Contains("venn-set"))
			.Select(g => g.Elements().Single(c => c.Name.LocalName == "circle"))
			.Select(c => ((string?)c.Attribute("cx"), (string?)c.Attribute("cy")));
		circles.Should().BeEquivalentTo(sets);
		// painted over the circles and under every name: a sibling of the sets, before the labels
		static bool IsSet(XElement e) => e.DescendantsAndSelf().Any(d => ((string?)d.Attribute("class") ?? "").Contains("venn-set"));

		var outermost = region.Ancestors().First(a => a.Parent!.Elements().Any(IsSet));
		var siblings = outermost.Parent!.Elements().ToList();
		var setsAt = siblings.FindLastIndex(e => IsSet(e));
		var namesAt = siblings.FindIndex(e => e.DescendantsAndSelf().Any(d => d.Name.LocalName == "text"));
		siblings.IndexOf(outermost).Should().BeGreaterThan(setsAt).And.BeLessThan(namesAt);

		// it arrives with its label, after both sets
		XElement Region(XDocument d) => d.Descendants().Single(e => e.Name.LocalName == "rect" && (string?)e.Attribute("fill-opacity") == "0.32");

		var lit = FirstSeen(animation, Region);
		lit.Should().BeGreaterThan(FirstSeen(animation, d => Text(d, "B")));
		FirstSeen(animation, d => Text(d, "Both")).Should().Be(lit);
	}

	[Test]
	public void Focus_on_a_venn_overlap_keeps_its_region_lit()
	{
		var animation = MermaidAnimator.Compile("venn-beta\n  set A\n  set B\n  union A,B[\"Both\"]\n\nanimate once\n  focus \"Both\"\n");
		var end = XDocument.Parse(animation.RenderFrame(animation.ScoreDuration));

		double Opacity(XElement e) =>
			e
				.AncestorsAndSelf()
				.Select(a => Regex.Match((string?)a.Attribute("style") ?? "", "opacity:([\\d.]+)"))
				.Where(m => m.Success)
				.Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
				.Aggregate(1.0, (x, y) => x * y);

		var region = end.Descendants().Single(e => e.Name.LocalName == "rect" && (string?)e.Attribute("fill-opacity") == "0.32");
		Opacity(region).Should().Be(1);
		Opacity(Text(end, "Both")).Should().Be(1);
		Opacity(Text(end, "A")).Should().BeLessThan(0.5, "the sets dim around their overlap");
	}

	[Test]
	public void A_marked_journey_task_marks_its_face_with_a_round_ring()
	{
		const string source = "journey\n  section S\n    Browse: 5: Me\n    Pay: 2: Me\n\nanimate once\n  mark \"Pay\" fail\n";
		var doc = XDocument.Parse(MermaidAnimator.Compile(source).Svg);
		var face = doc
			.Descendants()
			.Where(e => ((string?)e.Attribute("class") ?? "").Split(' ').Contains("face") && (string?)e.Attribute("data-score") == "2")
			.Single();
		// the mark sits beside the face in the face's wrapper, so it appears and moves with it
		var ring = face.Parent!
			.Elements()
			.SelectMany(g => g.Elements())
			.Single(r => r.Name.LocalName == "rect" && (string?)r.Attribute("fill-opacity") == "0.14");
		double
			.Parse((string)ring.Attribute("rx")!, System.Globalization.CultureInfo.InvariantCulture)
			.Should()
			.Be(double.Parse((string)ring.Attribute("width")!, System.Globalization.CultureInfo.InvariantCulture) / 2, "a face's ring is round");
	}

	[Test]
	public void Packet_fields_fill_in_bit_order()
	{
		var animation = MermaidAnimator.Compile("packet-beta\n  0-15: \"One\"\n  16-31: \"Two\"\n  32-63: \"Three\"\n\nanimate once\n  build\n");
		new[] { "One", "Two", "Three" }.Select(l => FirstSeen(animation, d => Text(d, l))).Should().BeInAscendingOrder();
	}

	[Test]
	public void A_journey_task_brings_its_face_and_the_line_draws_through()
	{
		var animation = MermaidAnimator.Compile("journey\n  section S\n    Browse: 5: Me\n    Pay: 1: Me\n\nanimate once\n  build\n");
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
