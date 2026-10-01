using System.Text.RegularExpressions;
using AwesomeAssertions;
using Mermaider.Examples;

namespace Mermaider.Tests.Snapshots;

/// <summary>
/// Regression guard: counts visual edge crossings in rendered ER diagram SVGs
/// and asserts they do not exceed calibrated maximums.
///
/// Uses bezier-curve sampling to detect geometric crossings between pairs of
/// entity relationship paths. Each calibrated maximum is set at the current
/// crossing count — any increase is a regression.
///
/// Counts are verified counts from the bezier intersection pass, not permutation
/// counts from the layout algorithm, so they reflect what a user actually sees.
/// </summary>
public partial class ErEdgeCrossingTests
{
	/// <summary>Calibrated maximum visual crossings per ER slug (0 = no regressions allowed).</summary>
	private static readonly IReadOnlyDictionary<string, int> MaxCrossings =
		new Dictionary<string, int>(StringComparer.Ordinal)
		{
			["db-erd-02-shared-vocab"]            = 2,
			["db-erd-05-building-blocks"]          = 5,
			["db-erd-06-catalog"]                  = 0,
			["db-erd-07-source"]                   = 0,
			["db-erd-08-docset"]                   = 0,
			["db-erd-09-content"]                  = 0,
			["db-erd-10-navigation"]               = 0,
			["db-erd-11-link-graph"]               = 0,
			["db-erd-12-assembly"]                 = 0,
			["db-erd-13-codex"]                    = 0,
			["db-erd-14-release-notes"]            = 0,
			["db-erd-15-api-search"]               = 2,
			["db-erd-16-relationships-overview"]   = 0,
			["db-erd-17-catalog"]                  = 0,
			["db-erd-18-source"]                   = 1,
			// Unifying rank assignment across disconnected components (SeparateComponents = false
			// — see LayoutOptions and SkeletonComparisonTests class remarks) raised layer IR
			// agreement with mermaid.js to 100% for all three of db-erd-19/20/21 (was 71%/69%/95%),
			// by threading each diagram's previously-isolated pieces (CrossLinkItem/CrossLinkUri,
			// IncludeDirective/Snippet, Phantom/TableOfContents) back into the main component's
			// layer range, same as dagre. CrossingMinimizer's barycenter+pairwise-swap heuristic
			// doesn't always resolve the resulting denser layer the way dagre's median+resolve-
			// conflicts ordering would, costing 1-2 extra visual crossings each. Visual inspection
			// confirms dense-but-correct layouts, not degenerate output. Revisit once ordering gets
			// a fuller dagre-equivalent port.
			["db-erd-19-docset"]                   = 3,
			["db-erd-20-content"]                  = 9,
			["db-erd-21-navigation"]               = 1,
			["db-erd-22-link-graph"]               = 0,
			["db-erd-23-publishing"]               = 1,
			["db-erd-24-codex"]                    = 1,
			["db-erd-25-release-notes"]            = 1,
			// Network simplex brought this diagram to 100%/100% layer+order IR agreement with
			// mermaid.js (previously lower); the 1-crossing increase is a routing-corridor
			// side effect of the new rank assignment, not a node-ordering defect.
			["db-erd-26-api-reference"]            = 2,
			["db-erd-27-search"]                   = 0,
			["er-aliases"]                         = 0,
			["er-basic"]                           = 0,
			["er-cardinalities"]                   = 0,
			["er-complex"]                         = 0,
			["er-direction"]                       = 0,
			["er-optional-label"]                  = 0,
		};

	public static IEnumerable<DiagramExample> ErExamples() =>
		DiagramExamples.All.Where(e => e.Category == DiagramCategory.Er);

	[Test]
	[MethodDataSource(nameof(ErExamples))]
	public void EdgeCrossings_DoNotExceedBaseline(DiagramExample example)
	{
		var svg = MermaidRenderer.RenderSvg(example.Source);
		var crossings = CountVisualCrossings(svg);

		if (!MaxCrossings.TryGetValue(example.Slug, out var max))
		{
			// Unknown slug: assert 0 crossings so new diagrams are held to a high standard.
			crossings.Should().Be(0,
				$"'{example.Slug}' is not in MaxCrossings — new ER diagrams must render with 0 visual crossings, " +
				$"or add a calibrated entry to MaxCrossings. Got {crossings} crossing(s).");
			return;
		}

		crossings.Should().BeLessThanOrEqualTo(max,
			$"'{example.Slug}' has {crossings} visual edge crossing(s) but max is {max}. " +
			$"Crossing count increased — check layout regression in CrossingMinimizer or ER layout.");
	}

	/// <summary>
	/// Counts visual edge crossings in an ER SVG by sampling bezier curves and testing
	/// for segment intersections. Only counts crossings between distinct, non-adjacent edges.
	/// </summary>
	private static int CountVisualCrossings(string svg, int samplePoints = 30)
	{
		var paths = ExtractEdgePaths(svg);
		var sampled = paths.Select(p => (p.entity1, p.entity2, SamplePath(p.d, samplePoints))).ToList();

		var crossings = 0;
		for (var i = 0; i < sampled.Count; i++)
		{
			for (var j = i + 1; j < sampled.Count; j++)
			{
				var (e1a, e2a, ptsA) = sampled[i];
				var (e1b, e2b, ptsB) = sampled[j];
				// Skip edges that share an endpoint entity
				if (e1a == e1b || e2a == e2b || e1a == e2b || e2a == e1b)
					continue;
				if (PathsCross(ptsA, ptsB))
					crossings++;
			}
		}
		return crossings;
	}

	private static bool PathsCross(List<(double x, double y)> a, List<(double x, double y)> b)
	{
		for (var i = 0; i < a.Count - 1; i++)
		{
			for (var j = 0; j < b.Count - 1; j++)
			{
				if (SegmentsIntersect(a[i], a[i + 1], b[j], b[j + 1]))
					return true;
			}
		}
		return false;
	}

	private static bool SegmentsIntersect(
		(double x, double y) p1, (double x, double y) p2,
		(double x, double y) p3, (double x, double y) p4)
	{
		var dx1 = p2.x - p1.x; var dy1 = p2.y - p1.y;
		var dx2 = p4.x - p3.x; var dy2 = p4.y - p3.y;
		var denom = dx1 * dy2 - dy1 * dx2;
		if (Math.Abs(denom) < 1e-10) return false;
		var dx3 = p3.x - p1.x; var dy3 = p3.y - p1.y;
		var t = (dx3 * dy2 - dy3 * dx2) / denom;
		var u = (dx3 * dy1 - dy3 * dx1) / denom;
		return t > 1e-4 && t < 1 - 1e-4 && u > 1e-4 && u < 1 - 1e-4;
	}

	private static List<(double x, double y)> SamplePath(string d, int n)
	{
		var pts = new List<(double, double)>();
		var tokens = PathTokenRe().Matches(d);
		var i = 0;
		var cmd = ' ';
		(double x, double y) cur = (0, 0);

		while (i < tokens.Count)
		{
			var tok = tokens[i].Value;
			if (tok is "M" or "L" or "C" or "S")
			{
				cmd = tok[0]; i++; continue;
			}
			switch (cmd)
			{
				case 'M':
				{
					cur = (double.Parse(tok), double.Parse(tokens[i + 1].Value));
					pts.Add(cur); i += 2; break;
				}
				case 'L':
				{
					var p1 = (double.Parse(tok), double.Parse(tokens[i + 1].Value));
					for (var k = 1; k <= n; k++)
					{
						var tt = k / (double)n;
						pts.Add((cur.x + tt * (p1.Item1 - cur.x), cur.y + tt * (p1.Item2 - cur.y)));
					}
					cur = p1; i += 2; break;
				}
				case 'C':
				{
					var c1x = double.Parse(tok); var c1y = double.Parse(tokens[i + 1].Value);
					var c2x = double.Parse(tokens[i + 2].Value); var c2y = double.Parse(tokens[i + 3].Value);
					var ex = double.Parse(tokens[i + 4].Value); var ey = double.Parse(tokens[i + 5].Value);
					for (var k = 1; k <= n; k++)
					{
						var tt = k / (double)n;
						var mt = 1 - tt;
						pts.Add((
							mt * mt * mt * cur.x + 3 * mt * mt * tt * c1x + 3 * mt * tt * tt * c2x + tt * tt * tt * ex,
							mt * mt * mt * cur.y + 3 * mt * mt * tt * c1y + 3 * mt * tt * tt * c2y + tt * tt * tt * ey
						));
					}
					cur = (ex, ey); i += 6; break;
				}
				default: i++; break;
			}
		}
		return pts;
	}

	[GeneratedRegex(@"[MLCS]|[0-9.-]+", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
	private static partial Regex PathTokenRe();

	[GeneratedRegex(@"<path[^>]*data-entity1=""([^""]+)""[^>]*data-entity2=""([^""]+)""[^>]*d=""([^""]+)""",
		RegexOptions.None, matchTimeoutMilliseconds: 2000)]
	private static partial Regex EdgePathRe();

	private static List<(string entity1, string entity2, string d)> ExtractEdgePaths(string svg)
	{
		var result = new List<(string, string, string)>();
		foreach (Match m in EdgePathRe().Matches(svg))
			result.Add((m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value));
		return result;
	}
}
