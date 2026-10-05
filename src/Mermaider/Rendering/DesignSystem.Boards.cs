using System.Text;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>
/// Board parts shared by block, mindmap, kanban, user journey and timeline: the page frame (margin, title row), the accent
/// story line (timeline axis, journey curve), moments on it, journey faces and word wrapping on the type tiers.
/// </summary>
internal sealed partial class DesignSystem
{
	/// <summary>Page margin around a board.</summary>
	internal const double BoardMargin = 40;

	/// <summary>Vertical centre of the title row.</summary>
	internal const double BoardTitleCy = 40;

	/// <summary>Where content starts below a title row.</summary>
	internal const double BoardTitledTop = 72;

	/// <summary>Top of a board's content: below the title row when there is one, else the margin.</summary>
	internal static double BoardTop(bool titled) => titled ? BoardTitledTop : BoardMargin;

	/// <summary>
	/// Space a container header takes before its content, the same for every preset so layout never depends on the style
	/// (the strip is 28, the chip 36, the tab 16; content starts at strip + 16).
	/// </summary>
	internal const double BoardHeaderSpace = StripHeight + 16;

	/// <summary>Width of the accent story line (timeline axis, journey curve): half a step heavier than the edges.</summary>
	internal string StoryLineWidth => Num(Spec.EdgeWidth + 0.5);

	/// <summary>An accent polyline through <paramref name="points"/> (round joins), the "story line" of a board.</summary>
	internal void AppendStoryLine(StringBuilder sb, IReadOnlyList<Models.Point> points, string cssClass)
	{
		if (points.Count < 2)
			return;
		_ = sb.Append("\n<path class=\"").Append(cssClass).Append("\" d=\"M").Append(Num(points[0].X)).Append(',').Append(Num(points[0].Y));
		for (var i = 1; i < points.Count; i++)
			_ = sb.Append(" L").Append(Num(points[i].X)).Append(',').Append(Num(points[i].Y));
		_ = sb.Append("\" fill=\"none\" stroke=\"").Append(ColorFamily.AccentBase).Append("\" stroke-width=\"").Append(StoryLineWidth)
			.Append("\" stroke-linecap=\"round\" stroke-linejoin=\"round\" />");
	}

	/// <summary>An accent arrowhead whose tip is at (<paramref name="tipX"/>, <paramref name="cy"/>), pointing right.</summary>
	internal void AppendStoryArrow(StringBuilder sb, double tipX, double cy)
	{
		var (len, half) = Spec.Marker switch
		{
			MarkerKind.Thin => (9.0, 4.0),
			MarkerKind.Chunky => (10.0, 5.5),
			_ => (9.0, 4.5),
		};
		var a = ColorFamily.AccentBase;
		if (Spec.Marker == MarkerKind.Thin)
		{
			_ = sb.Append("\n<path d=\"M").Append(Num(tipX - len)).Append(',').Append(Num(cy - half))
				.Append(" L").Append(Num(tipX)).Append(',').Append(Num(cy))
				.Append(" L").Append(Num(tipX - len)).Append(',').Append(Num(cy + half))
				.Append("\" fill=\"none\" stroke=\"").Append(a).Append("\" stroke-width=\"1.25\" stroke-linecap=\"round\" stroke-linejoin=\"round\" />");
			return;
		}

		_ = sb.Append("\n<polygon points=\"").Append(Num(tipX)).Append(',').Append(Num(cy)).Append(' ')
			.Append(Num(tipX - len)).Append(',').Append(Num(cy - half)).Append(' ')
			.Append(Num(tipX - len)).Append(',').Append(Num(cy + half))
			.Append("\" fill=\"").Append(a).Append("\" stroke=\"").Append(a).Append("\" stroke-width=\"1.5\" stroke-linejoin=\"round\" />");
	}

	/// <summary>A moment on a story line: page-coloured dot (square in Blueprint) ringed in the family stroke.</summary>
	internal void AppendMoment(StringBuilder sb, double cx, double cy, ColorFamily family, double r = 6)
	{
		if (Spec.Terminal == TerminalKind.Square)
		{
			_ = sb.Append("\n<rect x=\"").Append(Num(cx - r)).Append("\" y=\"").Append(Num(cy - r))
				.Append("\" width=\"").Append(Num(r * 2)).Append("\" height=\"").Append(Num(r * 2))
				.Append("\" fill=\"var(--bg)\" stroke=\"").Append(family.Stroke).Append("\" stroke-width=\"2\" />");
			return;
		}

		_ = sb.Append("\n<circle cx=\"").Append(Num(cx)).Append("\" cy=\"").Append(Num(cy)).Append("\" r=\"").Append(Num(r))
			.Append("\" fill=\"").Append(Spec.Terminal == TerminalKind.Disc ? family.Soft : "var(--bg)")
			.Append("\" stroke=\"").Append(family.Stroke).Append("\" stroke-width=\"2\" />");
	}

	/// <summary>Radius of a journey face.</summary>
	internal const double FaceRadius = 16;

	/// <summary>
	/// A journey score face in the section <paramref name="family"/>: soft fill, family stroke, features in the family ink.
	/// Sentiment reads from the mouth and the face's height on the curve, never from colour alone.
	/// </summary>
	internal void AppendFace(StringBuilder sb, double cx, double cy, int score, ColorFamily family)
	{
		const double r = FaceRadius;
		var ink = family.Ink;
		_ = sb.Append("\n<g class=\"face\" data-score=\"").Append(score).Append("\">");
		_ = sb.Append("\n  <circle cx=\"").Append(Num(cx)).Append("\" cy=\"").Append(Num(cy)).Append("\" r=\"").Append(Num(r))
			.Append("\" fill=\"").Append(family.Soft).Append("\" stroke=\"").Append(Spec.OutlineWidth > 0 ? family.Stroke : family.Edge)
			.Append("\" stroke-width=\"1.75\" />");
		foreach (var ex in new[] { cx - 5.5, cx + 5.5 })
		{
			_ = sb.Append("\n  <circle cx=\"").Append(Num(ex)).Append("\" cy=\"").Append(Num(cy - 3.5))
				.Append("\" r=\"1.75\" fill=\"").Append(ink).Append("\" />");
		}

		_ = sb.Append("\n  <path class=\"mouth\" d=\"");
		_ = score switch
		{
			> 3 => sb.Append('M').Append(Num(cx - 6.5)).Append(',').Append(Num(cy + 3))
				.Append(" Q").Append(Num(cx)).Append(',').Append(Num(cy + 10)).Append(' ').Append(Num(cx + 6.5)).Append(',').Append(Num(cy + 3)),
			< 3 => sb.Append('M').Append(Num(cx - 6.5)).Append(',').Append(Num(cy + 9))
				.Append(" Q").Append(Num(cx)).Append(',').Append(Num(cy + 2)).Append(' ').Append(Num(cx + 6.5)).Append(',').Append(Num(cy + 9)),
			_ => sb.Append('M').Append(Num(cx - 5.5)).Append(',').Append(Num(cy + 6)).Append(" H").Append(Num(cx + 5.5)),
		};
		_ = sb.Append("\" fill=\"none\" stroke=\"").Append(ink).Append("\" stroke-width=\"1.75\" stroke-linecap=\"round\" />\n</g>");
	}

	/// <summary>A small round marker dot (kanban card family, priority role) of radius <paramref name="r"/>.</summary>
	internal static void AppendDot(StringBuilder sb, double cx, double cy, string color, double r = 3.5) =>
		sb.Append("<circle cx=\"").Append(Num(cx)).Append("\" cy=\"").Append(Num(cy)).Append("\" r=\"").Append(Num(r))
			.Append("\" fill=\"").Append(color).Append("\" />");

	/// <summary>
	/// Word-wraps <paramref name="text"/> to lines no wider than <paramref name="maxWidth"/> at the tier of <paramref name="role"/>.
	/// Browsers set text a little wider than the metrics, so this wraps slightly early. Measured at the heaviest weight any
	/// preset uses for the role (or <paramref name="weight"/>), so line breaks never depend on the style.
	/// </summary>
	internal static List<string> Wrap(string text, double maxWidth, TypeRole role, int? weight = null)
	{
		var px = Px(role);
		var w = weight ?? MeasureWeight(role);
		var limit = maxWidth * 0.95;
		var lines = new List<string>();
		foreach (var paragraph in text.Split('\n'))
		{
			var current = "";
			foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
			{
				var candidate = current.Length == 0 ? word : current + " " + word;
				if (current.Length > 0 && TextMetrics.MeasureTextWidth(candidate, px, w) > limit)
				{
					lines.Add(current);
					current = word;
				}
				else
				{
					current = candidate;
				}
			}

			lines.Add(current);
		}

		return lines.Count == 0 ? [""] : lines;
	}

	/// <summary>The weight layout measures a role with: the heaviest any preset draws it at.</summary>
	internal static int MeasureWeight(TypeRole role) => role switch
	{
		TypeRole.Title or TypeRole.Heading => 700,
		TypeRole.Body or TypeRole.Meta => 400,
		TypeRole.Caption => 500,
		_ => 600,
	};

	/// <summary>Height of <paramref name="lines"/> lines of text at the tier of <paramref name="role"/>.</summary>
	internal static double TextHeight(int lines, TypeRole role) => Math.Max(1, lines) * Px(role) * TextMetrics.LineHeightRatio;
}
