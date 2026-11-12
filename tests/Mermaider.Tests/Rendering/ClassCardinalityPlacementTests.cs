using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

/// <summary>
/// Cardinalities sit beside their line near the class they belong to (issue #88): never on the relation label at the midpoint,
/// never on each other or on a class box, and always nearer their own end of the line than the other.
/// </summary>
public partial class ClassCardinalityPlacementTests
{
	private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

	// each expectation is "From>To:text:end", where end says which end of the relationship the cardinality belongs to
	[Test]
	[Arguments("classDiagram\n  Pond o-- \"many\" Duck : holds\n  Duck *-- \"2\" Wing : has", "Pond>Duck:many:to,Duck>Wing:2:to")]
	[Arguments("classDiagram\n  direction LR\n  Pond o-- \"many\" Duck : holds\n  Duck *-- \"2\" Wing : has", "Pond>Duck:many:to,Duck>Wing:2:to")]
	[Arguments("classDiagram\n  Customer \"1\" --> \"0..*\" Order : places\n  Order \"one\" *-- \"1..*\" LineItem : contains", "Customer>Order:1:from,Customer>Order:0..*:to,Order>LineItem:one:from,Order>LineItem:1..*:to")]
	[Arguments("classDiagram\n  direction LR\n  Customer \"1\" --> \"0..*\" Order : places\n  Order \"one\" *-- \"1..*\" LineItem : contains", "Customer>Order:1:from,Customer>Order:0..*:to,Order>LineItem:one:from,Order>LineItem:1..*:to")]
	[Arguments("classDiagram\n  Hub \"1\" --> \"many\" A : a\n  Hub \"one\" --> \"lots\" B : b\n  Hub \"single\" --> \"several\" C : c\n  A \"x\" --> \"y\" C", "Hub>A:1:from,Hub>A:many:to,Hub>B:one:from,Hub>B:lots:to,Hub>C:single:from,Hub>C:several:to,A>C:x:from,A>C:y:to")]
	public void Cardinalities_sit_beside_their_own_end_clear_of_labels(string source, string expectations)
	{
		var doc = XDocument.Parse(MermaidRenderer.RenderSvg(source));

		var classBoxes = doc
			.Descendants(Svg + "g")
			.Where(g => (string?)g.Attribute("class") == "class-node")
			.Select(g => (Id: (string)g.Attribute("data-id")!, Box: RectOf(g.Element(Svg + "rect")!)))
			.ToList();
		var cardinalities = new List<Box>();

		foreach (var expected in expectations.Split(','))
		{
			var parts = expected.Split(':');
			var (from, to) = (parts[0].Split('>')[0], parts[0].Split('>')[1]);
			var (text, atEnd) = (parts[1], parts[2] == "to");

			var path = doc.Descendants(Svg + "path").Single(
				p => (string?)p.Attribute("class") == "class-relationship" && (string?)p.Attribute("data-from") == from && (string?)p.Attribute("data-to") == to
			);
			var label = (string?)path.Attribute("data-label");
			var points = Number().Matches((string)path.Attribute("d")!).Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
			var start = (X: points[0], Y: points[1]);
			var end = (X: points[^2], Y: points[^1]);

			// the label group draws each pill as a rect followed by its text
			var group = doc.Descendants(Svg + "g").Single(
				g => (string?)g.Attribute("class") == "edge-label" && (string?)g.Attribute("data-from") == from && (string?)g.Attribute("data-to") == to
			);
			var pills = group.Elements(Svg + "rect").Zip(group.Elements(Svg + "text"), (r, t) => (Text: t.Value, Box: RectOf(r))).ToList();
			var pill = pills.Single(p => p.Text == text).Box;

			var (own, other) = atEnd ? (end, start) : (start, end);
			Distance(pill.Cx, pill.Cy, own.X, own.Y)
				.Should()
				.BeLessThan(Distance(pill.Cx, pill.Cy, other.X, other.Y), $"'{text}' belongs to the {parts[2]} end of {from} → {to}");

			if (label != null)
				pill.Intersects(pills.Single(p => p.Text == label).Box).Should().BeFalse($"'{text}' must clear the relation label '{label}'");

			foreach (var (id, box) in classBoxes)
				pill.Intersects(box).Should().BeFalse($"'{text}' must not cover class {id}");

			foreach (var placed in cardinalities)
				pill.Intersects(placed).Should().BeFalse($"'{text}' must not cover another cardinality");
			cardinalities.Add(pill);
		}
	}

	private static Box RectOf(XElement rect) => new(Attr(rect, "x"), Attr(rect, "y"), Attr(rect, "width"), Attr(rect, "height"));

	private static double Attr(XElement e, string name) => double.Parse((string)e.Attribute(name)!, CultureInfo.InvariantCulture);

	private static double Distance(double x0, double y0, double x1, double y1) => Math.Sqrt(((x1 - x0) * (x1 - x0)) + ((y1 - y0) * (y1 - y0)));

	[GeneratedRegex(@"-?\d+(?:\.\d+)?(?:[eE]-?\d+)?", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
	private static partial Regex Number();

	private readonly record struct Box(double X, double Y, double W, double H)
	{
		public double Cx => X + (W / 2);

		public double Cy => Y + (H / 2);

		public bool Intersects(Box o) => X < o.X + o.W && o.X < X + W && Y < o.Y + o.H && o.Y < Y + H;
	}
}
