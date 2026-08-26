using System.Text.RegularExpressions;
using AwesomeAssertions;
using Mermaider;

namespace Mermaider.Tests;

/// <summary>
/// Asserts on label/node overlap in the rendered SVG rather than on computed spacing, so these describe the
/// symptom a reader sees — clipped edge labels on LR/RL — rather than the arithmetic that produces it.
/// </summary>
public class EdgeLabelLayerSpacingTests
{
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

	[Test]
	public void Lr_long_edge_labels_do_not_collide_with_nodes()
	{
		CountLabelNodeCollisions(MermaidRenderer.RenderSvg(LongLabelsLr))
			.Should().Be(0, "a label wider than the layer gap is painted over by the nodes either side");
	}

	[Test]
	public void Td_long_edge_labels_still_do_not_collide()
	{
		CountLabelNodeCollisions(MermaidRenderer.RenderSvg(LongLabelsTd))
			.Should().Be(0, "the vertical axis was already correct and must stay that way");
	}

	[Test]
	public void Lr_without_edge_labels_is_unaffected()
	{
		var svg = MermaidRenderer.RenderSvg("graph LR\n    A[Start] --> B[Middle]\n    B --> C[End]");
		CountLabelNodeCollisions(svg).Should().Be(0);
	}

	/// <summary>Counts label rects overlapping node rects.</summary>
	private static int CountLabelNodeCollisions(string svg)
	{
		var rects = Regex.Matches(svg, @"<rect\b[^>]*>")
			.Select(m => m.Value)
			.Select(Parse)
			.Where(r => r is not null)
			.Select(r => r!.Value)
			.ToList();

		// No class hooks to key off, so split by area: node boxes are materially larger than label chips.
		if (rects.Count < 2) return 0;
		var median = rects.Select(r => r.W * r.H).OrderBy(a => a).ElementAt(rects.Count / 2);
		var nodes = rects.Where(r => r.W * r.H >= median).ToList();
		var labels = rects.Where(r => r.W * r.H < median).ToList();

		return labels.Sum(l => nodes.Count(n => Overlaps(l, n)));
	}

	private static bool Overlaps((double X, double Y, double W, double H) a, (double X, double Y, double W, double H) b) =>
		a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H;

	private static (double X, double Y, double W, double H)? Parse(string tag)
	{
		double? Get(string name) =>
			Regex.Match(tag, name + @"=""([-\d.]+)""") is { Success: true } m
				? double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
				: null;

		return Get("x") is { } x && Get("y") is { } y && Get("width") is { } w && Get("height") is { } h
			? (x, y, w, h)
			: null;
	}
}
