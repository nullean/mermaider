using System.Text.RegularExpressions;
using System.Xml.Linq;
using AwesomeAssertions;

namespace Mermaider.Animate.Tests;

/// <summary>Kanban moves, trees, numbered relations and the other graph-like types.</summary>
public partial class BoardTests
{
	private const string Board =
		"""
		kanban
		  Todo
		    a[Alpha]
		    b[Beta]
		  Doing
		  Done
		    c[Gamma]
		""";

	private const string Mind =
		"""
		mindmap
		  root((Root))
		    Left
		      L1
		      L2
		    Right
		      R1
		""";

	[GeneratedRegex(@"translate\((-?[\d.]+)px,(-?[\d.]+)px\)")]
	private static partial Regex Translate();

	[GeneratedRegex(@"(^|;)opacity:0\.22(;|$)")]
	private static partial Regex Dimmed();

	[GeneratedRegex("(^|;)opacity:0(;|$)")]
	private static partial Regex Hidden();

	private static XElement ById(XDocument doc, string id) => doc.Descendants().Single(e => (string?)e.Attribute("data-id") == id);

	private static (double X, double Y) Moved(XElement card)
	{
		var style = card
			.AncestorsAndSelf()
			.Select(a => (string?)a.Attribute("style") ?? "")
			.FirstOrDefault(s => s.Contains("translate(", StringComparison.Ordinal));
		if (style is null)
			return (0, 0);
		var m = Translate().Match(style);
		return (double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), double.Parse(
			m.Groups[2].Value,
			System.Globalization.CultureInfo.InvariantCulture
		));
	}

	[Test]
	public void A_moved_card_lands_in_its_new_column_and_the_cards_below_close_up()
	{
		var animation = MermaidAnimator.Compile(Board + "\nanimate once\n  step \"go\"\n    move a to Done\n");
		var svg = XDocument.Parse(MermaidRenderer.RenderSvg(Board));

		double X(string id) =>
			double.Parse(
				(string)ById(svg, id).Descendants().First(e => e.Name.LocalName == "rect").Attribute("x")!,
				System.Globalization.CultureInfo.InvariantCulture
			);

		double Y(string id) =>
			double.Parse(
				(string)ById(svg, id).Descendants().First(e => e.Name.LocalName == "rect").Attribute("y")!,
				System.Globalization.CultureInfo.InvariantCulture
			);

		var end = XDocument.Parse(animation.RenderFrame(animation.Steps[0].End));
		var (dx, dy) = Moved(ById(end, "a"));
		(X("a") + dx).Should().BeApproximately(X("c"), 0.5, "the card keeps its inset in the new column");
		(Y("a") + dy).Should().BeGreaterThan(Y("c"), "it lands under the card already there");
		var (_, betaDy) = Moved(ById(end, "b"));
		(Y("b") + betaDy).Should().BeApproximately(Y("a"), 0.5, "Beta moves up into Alpha's place");

		// the header counts follow: Todo 2 -> 1, Done 1 -> 2
		static bool Shown(XElement e) =>
			e.AncestorsAndSelf().All(a => a.Attribute("style") is { } style ? !Hidden().IsMatch((string)style) : (string?)a.Attribute("opacity") != "0");

		var counts = end
			.Descendants()
			.Where(e => e.Name.LocalName == "text" && e.Value.Length == 1 && char.IsDigit(e.Value[0]) && Shown(e))
			.Select(e => e.Value)
			.ToList();
		counts.Should().BeEquivalentTo(["1", "0", "2"], "Todo has 1, Doing 0 and Done 2");
	}

	[Test]
	public void Moving_twice_keeps_the_inset_of_the_column_a_card_was_drawn_in()
	{
		var animation = MermaidAnimator.Compile(Board + "\nanimate once\n  step \"one\"\n    move a to Doing\n  step \"two\"\n    move a to Done\n");
		var svg = XDocument.Parse(MermaidRenderer.RenderSvg(Board));

		double X(string id) =>
			double.Parse(
				(string)ById(svg, id).Descendants().First(e => e.Name.LocalName == "rect").Attribute("x")!,
				System.Globalization.CultureInfo.InvariantCulture
			);

		var (dx, _) = Moved(ById(XDocument.Parse(animation.RenderFrame(animation.Steps[1].End)), "a"));
		(X("a") + dx).Should().BeApproximately(X("c"), 0.5);
	}

	[Test]
	public void Focus_on_a_tree_node_keeps_its_subtree_lit()
	{
		var animation = MermaidAnimator.Compile(Mind + "\nanimate once\n  step \"left\"\n    focus \"Left\"\n");
		var frame = XDocument.Parse(animation.RenderFrame(animation.Steps[0].End - TimeSpan.FromMilliseconds(100)));

		bool IsDimmed(string label) =>
			frame
				.Descendants()
				.Single(e => (string?)e.Attribute("data-label") == label && ((string?)e.Attribute("class") ?? "").Contains("mindmap-node"))
				.AncestorsAndSelf()
				.Any(a => Dimmed().IsMatch((string?)a.Attribute("style") ?? ""));

		IsDimmed("Left").Should().BeFalse();
		IsDimmed("L1").Should().BeFalse();
		IsDimmed("L2").Should().BeFalse();
		IsDimmed("Right").Should().BeTrue();
		IsDimmed("R1").Should().BeTrue();
	}

	[Test]
	public void Expand_grows_a_subtree_that_was_hidden_until_then()
	{
		var animation = MermaidAnimator.Compile(Mind + "\nanimate once\n  step \"grow\"\n    expand \"Left\"\n");

		bool IsHidden(XDocument doc, string label) =>
			doc
				.Descendants()
				.Single(e => (string?)e.Attribute("data-label") == label && ((string?)e.Attribute("class") ?? "").Contains("mindmap-node"))
				.AncestorsAndSelf()
				.Any(a => Hidden().IsMatch((string?)a.Attribute("style") ?? ""));

		var start = XDocument.Parse(animation.RenderFrame(TimeSpan.Zero));
		IsHidden(start, "L1").Should().BeTrue();
		IsHidden(start, "R1").Should().BeFalse("only Left's subtree is expanded");
		IsHidden(XDocument.Parse(animation.RenderFrame(animation.Steps[0].End)), "L2").Should().BeFalse();
	}

	[Test]
	public void A_c4_dynamic_diagram_plays_its_relations_in_their_numbered_order()
	{
		const string c4 =
			"""
			C4Dynamic
			  Person(user, "User")
			  Container(web, "Web")
			  ContainerDb(db, "DB")
			  Rel(web, db, "2. reads")
			  Rel(user, web, "1. browses")
			""";
		var animation = MermaidAnimator.Compile(c4 + "\nanimate once\n");
		var tokens = Regex.Matches(animation.Svg, @"<text[^>]*>(browses|reads)</text>").Select(m => m.Groups[1].Value).ToList();
		tokens.Should().Equal("browses", "reads");
	}

	[Test]
	[Arguments("classDiagram\n  Order --> Line\n  Line --> Product\n", "Order", "Line", "Product")]
	[Arguments("erDiagram\n  ORDER ||--o{ LINE : has\n  LINE }o--|| PRODUCT : refs\n", "ORDER", "LINE", "PRODUCT")]
	[Arguments("block-beta\n  a b c\n  a --> b\n  b --> c\n", "a", "b", "c")]
	public void Graph_like_types_take_tours(string diagram, string first, string middle, string last)
	{
		var animation = MermaidAnimator.Compile(diagram + $"\nanimate once\n  step \"walk\"\n    tour {first} -> {middle} -> {last} \"hop\"\n");
		animation.Svg.Should().Contain(">hop<");
	}
}
