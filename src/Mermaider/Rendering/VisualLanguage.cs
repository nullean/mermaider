using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>
/// The one definition of the diagram look shared by flowchart, state, ER and class renderers:
/// a cluster colour becomes a darker border with a light tint of the same hue as fill; subgraphs and namespaces tint
/// deeper per nesting level; labels are pills with a line-weight border. Renderers use these helpers instead of
/// inlining the numbers, so the diagram types cannot drift apart.
/// </summary>
internal static class VisualLanguage
{
	/// <summary>Node / entity-body tint of the cluster colour, in percent.</summary>
	internal const int NodeTint = 16;

	/// <summary>Entity / class header tint of the cluster colour, in percent.</summary>
	internal const int HeaderTint = 24;

	/// <summary>Subgraph / namespace tint at nesting depth 0, in percent; every level adds <see cref="GroupTintStep"/>.</summary>
	internal const int GroupTintBase = 6;

	internal const int GroupTintStep = 3;

	/// <summary>Lightness change that turns a cluster colour into its border colour.</summary>
	internal const double BorderDarken = -0.12;

	/// <summary>Subgraph borders stay close to the palette colour itself.</summary>
	internal const double GroupBorderDarken = -0.02;

	/// <summary>
	/// Fill for shapes that carry a conventional meaning, under user style and role classes and above the cluster colour:
	/// decision (diamond / state choice) = accent tint, terminal (stadium) = neutral tint, data store (cylinder) = muted tint.
	/// Null for every other shape (they keep the cluster colour).
	/// </summary>
	internal static string? ShapeFill(NodeShape shape) => shape switch
	{
		NodeShape.Diamond => "var(--_accent-fill)",
		NodeShape.Stadium => "color-mix(in srgb, var(--fg) 9%, var(--bg))",
		NodeShape.Cylinder => "color-mix(in srgb, var(--_text-muted) 18%, var(--bg))",
		_ => null,
	};

	/// <summary>Border override of a meaningful shape (data store only); null keeps the cluster border.</summary>
	internal static string? ShapeStroke(NodeShape shape) => shape == NodeShape.Cylinder ? "var(--_text-muted)" : null;

	/// <summary>Terminals read as stronger: their border is this much heavier than a normal node's.</summary>
	internal const double TerminalExtraStroke = 0.5;

	/// <summary>A tint of <paramref name="color"/> over the page background.</summary>
	internal static string Tint(string color, int percent) => $"color-mix(in srgb, {color} {percent}%, var(--bg))";

	internal static string Border(string color) => ColorUtils.AdjustLightness(color, BorderDarken);

	internal static string GroupBorder(string color) => ColorUtils.AdjustLightness(color, GroupBorderDarken);

	internal static string GroupFill(string color, int depth) => Tint(color, GroupTintBase + (depth * GroupTintStep));

	/// <summary>
	/// Appends the pill behind an edge label: ~2px of clear padding around the text and a border as thick as the lines,
	/// so it reads as part of the line. Only the <c>&lt;rect&gt;</c> element is written; callers add their own whitespace.
	/// </summary>
	internal static void AppendLabelPill(StringBuilder sb, double centreX, double centreY, double textWidth, double textHeight)
	{
		var w = ErSvgRenderer.LabelBoxWidth(textWidth);
		var h = textHeight + ErSvgRenderer.LabelPadY;
		var r = Math.Min(RenderConstants.Radii.EdgeLabel, h / 2);
		_ = sb.Append("<rect x=\"").Append(centreX - (w / 2)).Append("\" y=\"").Append(centreY - (h / 2))
			.Append("\" width=\"").Append(w).Append("\" height=\"").Append(h)
			.Append("\" rx=\"").Append(r).Append("\" ry=\"").Append(r)
			.Append("\" fill=\"var(--bg)\" stroke=\"var(--_line)\" stroke-width=\"").Append(RenderConstants.StrokeWidths.Connector).Append("\" />");
	}

	/// <summary>
	/// The tinted header of an entity / class box: a path with rounded top corners and a square bottom, so the box border drawn
	/// on top reads as one rounded rectangle.
	/// </summary>
	internal static void AppendHeaderPath(StringBuilder sb, double x, double y, double width, double headerHeight, double radius, string fill)
	{
		_ = sb.Append("  <path d=\"M").Append(x).Append(',').Append(y + headerHeight)
			.Append(" L").Append(x).Append(',').Append(y + radius)
			.Append(" Q").Append(x).Append(',').Append(y).Append(' ').Append(x + radius).Append(',').Append(y)
			.Append(" L").Append(x + width - radius).Append(',').Append(y)
			.Append(" Q").Append(x + width).Append(',').Append(y).Append(' ').Append(x + width).Append(',').Append(y + radius)
			.Append(" L").Append(x + width).Append(',').Append(y + headerHeight)
			.Append(" Z\" fill=\"").Append(fill).Append("\" />\n");
	}

	/// <summary>Point halfway along a polyline's length (not the middle vertex).</summary>
	internal static Point PathMidpoint(IReadOnlyList<Point> points)
	{
		if (points.Count == 0)
			return new Point(0, 0);
		if (points.Count == 1)
			return points[0];

		var totalLength = 0.0;
		for (var i = 1; i < points.Count; i++)
			totalLength += Dist(points[i - 1], points[i]);

		var remaining = totalLength / 2;
		for (var i = 1; i < points.Count; i++)
		{
			var segLen = Dist(points[i - 1], points[i]);
			if (remaining <= segLen)
			{
				var t = remaining / segLen;
				return new Point(
					points[i - 1].X + (t * (points[i].X - points[i - 1].X)),
					points[i - 1].Y + (t * (points[i].Y - points[i - 1].Y)));
			}

			remaining -= segLen;
		}

		return points[^1];
	}

	private static double Dist(Point a, Point b) =>
		Math.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)));

	private static readonly string NoteTextAttrs = RenderConstants.TextAttrs.EdgeLabelCenterFill + "var(--_accent-text)\"";

	/// <summary>A note box with its dashed connector to the annotated node.</summary>
	internal static void AppendNote(StringBuilder sb, PositionedGraphNote note)
	{
		_ = sb.Append("\n<g class=\"note\">");
		if (note.LineFrom is { } lf && note.LineTo is { } lt)
		{
			_ = sb.Append("\n  <line x1=\"").Append(lf.X).Append("\" y1=\"").Append(lf.Y)
				.Append("\" x2=\"").Append(lt.X).Append("\" y2=\"").Append(lt.Y)
				.Append("\" stroke=\"var(--_accent-stroke)\" stroke-width=\"1.5\" stroke-dasharray=\"4 3\" />");
		}

		_ = sb.Append("\n  <rect x=\"").Append(note.X).Append("\" y=\"").Append(note.Y)
			.Append("\" width=\"").Append(note.Width).Append("\" height=\"").Append(note.Height)
			.Append("\" rx=\"6\" ry=\"6\"")
			.Append(" fill=\"var(--_accent-fill)\" stroke=\"var(--_accent-stroke)\" stroke-width=\"")
			.Append(RenderConstants.StrokeWidths.InnerBox).Append("\" />\n  ");

		MultilineUtils.AppendMultilineText(
			sb, note.Text,
			note.X + (note.Width / 2), note.Y + (note.Height / 2),
			RenderConstants.FontSizes.EdgeLabel,
			NoteTextAttrs);
		_ = sb.Append("\n</g>");
	}
}
