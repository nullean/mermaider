using System.Globalization;
using System.Xml.Linq;
using AwesomeAssertions;
using Mermaider;

namespace Mermaider.Tests;

/// <summary>
/// Asserts on label/node overlap in the rendered SVG rather than on computed spacing, so these describe the
/// symptom a reader sees — clipped edge/relationship labels — rather than the arithmetic that produces it.
/// </summary>
public class EdgeLabelLayerSpacingTests
{
	private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

	private const string LongLabelsLr = """
		graph LR
		    A[Start] -->|a considerably long edge label here| B[Middle]
		    B -->|another rather long edge label| C[End]
		""";

	private const string LongLabelsTd = """
		graph TD
		    A[Start] -->|a considerably long edge label here| B[Middle]
		    B -->|another rather long edge label| C[End]
		""";

	// A tall/narrow (wrapped) label: the vertical extent that must size a TB layer gap
	// is its HEIGHT, not its width — the inverse shape of LongLabelsErLr below.
	private const string LongLabelsErTb = """
		erDiagram
		    direction TB
		    START ||--o{ MIDDLE : "a<br/>b<br/>c<br/>d<br/>e<br/>f<br/>g<br/>h"
		""";

	private const string LongLabelsErLr = """
		erDiagram
		    direction LR
		    START ||--o{ MIDDLE : "a considerably long relationship label here"
		    MIDDLE ||--o{ END : "another rather long relationship label"
		""";

	[Test]
	public void Lr_long_edge_labels_do_not_collide_with_nodes()
	{
		CountFlowchartCollisions(MermaidRenderer.RenderSvg(LongLabelsLr))
			.Should().Be(0, "a label wider than the layer gap is painted over by the nodes either side");
	}

	[Test]
	public void Td_long_edge_labels_still_do_not_collide()
	{
		CountFlowchartCollisions(MermaidRenderer.RenderSvg(LongLabelsTd))
			.Should().Be(0, "the vertical axis was already correct and must stay that way");
	}

	[Test]
	public void Lr_without_edge_labels_is_unaffected()
	{
		var svg = MermaidRenderer.RenderSvg("graph LR\n    A[Start] --> B[Middle]\n    B --> C[End]");
		CountFlowchartCollisions(svg).Should().Be(0);
	}

	[Test]
	public void Er_tb_long_relationship_labels_do_not_collide_with_entities()
	{
		CountErCollisions(MermaidRenderer.RenderSvg(LongLabelsErTb))
			.Should().Be(0, "a label taller than the vertical layer gap is painted over by the entities above/below it");
	}

	[Test]
	public void Er_lr_long_relationship_labels_still_do_not_collide()
	{
		CountErCollisions(MermaidRenderer.RenderSvg(LongLabelsErLr))
			.Should().Be(0, "the horizontal axis was already correct and must stay that way");
	}

	/// <summary>Counts rects inside an "edge-label" group overlapping rects inside a "node" group.</summary>
	private static int CountFlowchartCollisions(string svg)
	{
		var doc = XDocument.Parse(svg);
		var nodes = RectsInGroup(doc, "node");
		var labels = RectsInGroup(doc, "edge-label");
		return labels.Sum(l => nodes.Count(n => Overlaps(l, n)));
	}

	/// <summary>
	/// Counts relationship-label rects overlapping entity rects. Entity rects live inside
	/// &lt;g class="entity"&gt; (<c>ErSvgRenderer.AppendEntityBox</c>); relationship-label rects are
	/// emitted directly under &lt;svg&gt; with no wrapping group (<c>AppendRelationshipLabel</c>).
	/// </summary>
	private static int CountErCollisions(string svg)
	{
		var doc = XDocument.Parse(svg);
		var entities = RectsInGroup(doc, "entity");
		var labels = doc.Root!.Elements(Svg + "rect").Select(ToRect).ToList();
		return labels.Sum(l => entities.Count(e => Overlaps(l, e)));
	}

	private static List<(double X, double Y, double W, double H)> RectsInGroup(XDocument doc, string groupClass) =>
		doc.Descendants(Svg + "g")
			.Where(g => (g.Attribute("class")?.Value ?? "").Split(' ').Contains(groupClass))
			.SelectMany(g => g.Elements(Svg + "rect"))
			.Select(ToRect)
			.ToList();

	private static (double X, double Y, double W, double H) ToRect(XElement rect) => (
		double.Parse(rect.Attribute("x")!.Value, CultureInfo.InvariantCulture),
		double.Parse(rect.Attribute("y")!.Value, CultureInfo.InvariantCulture),
		double.Parse(rect.Attribute("width")!.Value, CultureInfo.InvariantCulture),
		double.Parse(rect.Attribute("height")!.Value, CultureInfo.InvariantCulture));

	private static bool Overlaps((double X, double Y, double W, double H) a, (double X, double Y, double W, double H) b) =>
		a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H;
}
