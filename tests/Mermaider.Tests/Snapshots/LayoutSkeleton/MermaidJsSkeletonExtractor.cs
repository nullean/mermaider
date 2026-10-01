using System.Text.Json;
using System.Text.RegularExpressions;
using Mermaider.Models;
using Mermaider.Parsing;

namespace Mermaider.Tests.Snapshots.LayoutSkeleton;

/// <summary>
/// Builds a <see cref="LayoutSkeleton"/> from a real mermaid.js-rendered SVG (produced by
/// <c>mmdc</c> — see <see cref="Mermaider.Tests.MmdcRenderer"/>).
///
/// Node centers and sizes come straight from the SVG: each entity is
/// <c>&lt;g class="node default" id="...entity-{Name}-{idx}" ... transform="translate(cx,cy)"&gt;</c>
/// wrapping one of two shapes depending on whether the entity has an attribute table:
///  - With attributes: a clean (non hand-drawn) <c>&lt;g class="outer-path"&gt;&lt;path d="M-hw -hh L..."&gt;</c>
///    whose first M gives the half-width/half-height directly.
///  - Label-only (no <c>{ ... }</c> block in the source — most docs-builder examples are this
///    shape): a plain <c>&lt;rect class="basic label-container" x="-hw" y="-hh" width="w" height="h"/&gt;</c>.
/// Both must be checked — an SVG with only label-only entities has zero matches for the first
/// pattern, and earlier missed this: node extraction silently returned zero boxes, and
/// SkeletonComparer's "nothing to compare" fallback then reported 100% agreement instead of
/// failing. That combination (silent empty extraction + vacuous-pass metric) is exactly why
/// calibration originally looked better than it was — see SkeletonComparer's guards, which now
/// throw instead of defaulting to 1.0.
///
/// Edge geometry comes from each relationship path's <c>data-points</c> attribute — base64-
/// encoded JSON <c>[{"x":..,"y":..}, ...]</c>, giving exact waypoints with no `d`-attribute
/// parsing needed. Edge *identity* (which entities, which label) is not re-derived from the
/// SVG at all: mermaid.js's own edge id embeds a global edge index
/// (<c>id_entity-{From}-i_entity-{To}-j_{globalIndex}</c>) that matches the 0-based
/// declaration order of relationships in the diagram source, so edges are zipped against
/// <see cref="ErParser"/>'s parse of that same source instead of scraping labels out of
/// foreignObject/tspan markup.
/// </summary>
internal static partial class MermaidJsSkeletonExtractor
{
	private const int TimeoutMs = 2000;

	public static LayoutSkeleton Extract(string svg, string diagramSource)
	{
		var (cleaned, _) = DiagramPreprocessor.Process(diagramSource);
		var lines = MermaidRenderer.PreprocessLines(cleaned);
		var diagram = ErParser.Parse(lines);
		var relationships = diagram.Relationships;
		var entityIds = diagram.Entities.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);

		// mermaid.js 12.0.0 has its own quirks worth filtering rather than tripping over: e.g.
		// an `erDiagram` with a leading `direction TD` directive renders two extra, spurious
		// "node default" groups literally named "direction" and "TD" (mermaid.js's own ER
		// parser briefly mistakes the directive for entity declarations). Restricting to ids
		// ErParser actually knows about keeps this extractor honest without special-casing
		// individual diagrams.
		var boxes = ExtractNodeBoxes(svg).Where(b => entityIds.Contains(b.Id)).ToList();

		// Fail loudly on a partial/total extraction miss rather than silently returning a
		// skeleton with fewer (or zero) nodes than the diagram actually has — SkeletonComparer
		// treats "nothing in common to compare" as an error for exactly this reason: a quiet
		// empty skeleton must never be allowed to look like a perfect comparison result.
		if (boxes.Count != diagram.Entities.Count)
		{
			var foundIds = boxes.Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
			var missing = diagram.Entities.Select(e => e.Id).Where(id => !foundIds.Contains(id));
			throw new InvalidOperationException(
				$"mermaid.js SVG node extraction found {boxes.Count} box(es) but the diagram source has " +
				$"{diagram.Entities.Count} entities. Missing: {string.Join(", ", missing)}.");
		}

		var geometricEdges = ExtractEdgesInDeclarationOrder(svg);

		if (geometricEdges.Count != relationships.Count)
		{
			throw new InvalidOperationException(
				$"mermaid.js SVG has {geometricEdges.Count} relationshipLine path(s) but the diagram source parses to " +
				$"{relationships.Count} relationship(s) — cannot align edges by declaration order.");
		}

		var edges = new List<(string Entity1, string Entity2, string Label, Point Start, Point End)>(relationships.Count);
		for (var i = 0; i < relationships.Count; i++)
		{
			var rel = relationships[i];
			var (start, end) = geometricEdges[i];
			edges.Add((rel.Entity1, rel.Entity2, rel.Label, start, end));
		}

		return LayoutSkeletonBuilder.Build(boxes, edges);
	}

	private static List<(string Id, double X, double Y, double W, double H)> ExtractNodeBoxes(string svg)
	{
		// Keyed by id so the two shape patterns can't double-count the same entity (they're
		// mutually exclusive — an entity renders as exactly one of the two shapes).
		var boxes = new Dictionary<string, (string Id, double X, double Y, double W, double H)>(StringComparer.Ordinal);

		foreach (Match m in AttributedNodePattern().Matches(svg))
		{
			var id = m.Groups[1].Value;
			var cx = ParseDouble(m.Groups[2].Value);
			var cy = ParseDouble(m.Groups[3].Value);
			var halfW = Math.Abs(ParseDouble(m.Groups[4].Value));
			var halfH = Math.Abs(ParseDouble(m.Groups[5].Value));
			boxes[id] = (id, cx - halfW, cy - halfH, halfW * 2, halfH * 2);
		}

		foreach (Match m in LabelOnlyNodePattern().Matches(svg))
		{
			var id = m.Groups[1].Value;
			if (boxes.ContainsKey(id))
				continue;
			var cx = ParseDouble(m.Groups[2].Value);
			var cy = ParseDouble(m.Groups[3].Value);
			var relX = ParseDouble(m.Groups[4].Value); // already the top-left offset from center (negative)
			var relY = ParseDouble(m.Groups[5].Value);
			var w = ParseDouble(m.Groups[6].Value);
			var h = ParseDouble(m.Groups[7].Value);
			boxes[id] = (id, cx + relX, cy + relY, w, h);
		}

		return boxes.Values.ToList();
	}

	private static double ParseDouble(string s) =>
		double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

	// Returns (Start, End) point pairs ordered by mermaid.js's own global edge index (the
	// trailing _{N} in data-id), which is the 0-based declaration order of relationships in
	// the diagram source.
	private static List<(Point Start, Point End)> ExtractEdgesInDeclarationOrder(string svg)
	{
		var indexed = new List<(int Index, Point Start, Point End)>();
		foreach (Match m in EdgePattern().Matches(svg))
		{
			var index = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
			var points = DecodePoints(m.Groups[2].Value);
			if (points.Count < 2)
				continue;
			indexed.Add((index, points[0], points[^1]));
		}

		return indexed.OrderBy(e => e.Index).Select(e => (e.Start, e.End)).ToList();
	}

	private static List<Point> DecodePoints(string base64)
	{
		var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64));
		using var doc = JsonDocument.Parse(json);
		var result = new List<Point>();
		foreach (var el in doc.RootElement.EnumerateArray())
			result.Add(new Point(el.GetProperty("x").GetDouble(), el.GetProperty("y").GetDouble()));
		return result;
	}

	[GeneratedRegex(
		@"<g class=""node default"" id=""[^""]*?entity-([A-Za-z0-9_]+)-\d+""[^>]*transform=""translate\((-?[\d.]+),\s*(-?[\d.]+)\)""[^>]*><g class=""outer-path""[^>]*><path d=""M(-?[\d.]+)\s(-?[\d.]+)\sL",
		RegexOptions.None, TimeoutMs)]
	private static partial Regex AttributedNodePattern();

	[GeneratedRegex(
		@"<g class=""node default"" id=""[^""]*?entity-([A-Za-z0-9_]+)-\d+""[^>]*transform=""translate\((-?[\d.]+),\s*(-?[\d.]+)\)""[^>]*><rect class=""basic label-container""[^>]*x=""(-?[\d.]+)""\s*y=""(-?[\d.]+)""\s*width=""([\d.]+)""\s*height=""([\d.]+)""",
		RegexOptions.None, TimeoutMs)]
	private static partial Regex LabelOnlyNodePattern();

	[GeneratedRegex(
		@"<path[^>]*class=""[^""]*relationshipLine[^""]*""[^>]*data-id=""id_entity-[A-Za-z0-9_]+-\d+_entity-[A-Za-z0-9_]+-\d+_(\d+)""[^>]*data-points=""([^""]+)""",
		RegexOptions.None, TimeoutMs)]
	private static partial Regex EdgePattern();
}
