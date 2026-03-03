using System.Globalization;
using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class VennSvgRenderer
{
	private const double BaseRadius = 120;
	private const double CenterX = 300;
	private const double CenterY = 200;
	private const double LabelFontSize = 13;
	private const double UnionLabelFontSize = 11;
	private const double FillOpacity = 0.35;

	private static readonly string[] SetColors =
	[
		"#4e79a7", "#f28e2b", "#e15759", "#76b7b2",
		"#59a14f", "#edc948", "#b07aa1", "#ff9da7",
	];

	internal static string Render(VennDiagram diagram, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null)
	{
		var sb = RenderToBuilder(diagram, colors, font, transparent, strict);
		try
		{
			return sb.ToString();
		}
		finally
		{
			_ = sb.Clear();
			SharedStringBuilderPool.Instance.Return(sb);
		}
	}

	internal static StringBuilder RenderToBuilder(VennDiagram diagram, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null)
	{
		var sb = SharedStringBuilderPool.Instance.Get();

		var n = diagram.Sets.Count;
		var width = CenterX * 2;
		var height = CenterY * 2;

		StyleBlock.AppendSvgOpenTag(sb, width, height, colors, transparent);
		StyleBlock.AppendStyleBlock(sb, font, strict);
		_ = sb.Append("\n<defs>\n</defs>\n");

		if (n == 0)
		{
			_ = sb.Append("\n</svg>");
			return sb;
		}

		var positions = ComputePositions(n);
		var setPositions = new Dictionary<string, (double X, double Y)>();

		for (var i = 0; i < n; i++)
		{
			var set = diagram.Sets[i];
			var (px, py) = positions[i];
			setPositions[set.Id] = (px, py);
			var color = SetColors[i % SetColors.Length];
			var r = BaseRadius;

			_ = sb.Append("\n<circle cx=\"").Append(F(px)).Append("\" cy=\"").Append(F(py))
				.Append("\" r=\"").Append(F(r))
				.Append("\" fill=\"").Append(color)
				.Append("\" fill-opacity=\"").Append(F(FillOpacity))
				.Append("\" stroke=\"").Append(color)
				.Append("\" stroke-width=\"2\" />");

			var labelAngle = n <= 1 ? 0 : (2 * Math.PI * i / n) - (Math.PI / 2);
			var labelDist = n <= 1 ? 0 : r * 0.6;
			var lx = px + (labelDist * Math.Cos(labelAngle));
			var ly = py + (labelDist * Math.Sin(labelAngle));

			_ = sb.Append("\n<text x=\"").Append(F(lx)).Append("\" y=\"").Append(F(ly))
				.Append("\" text-anchor=\"middle\" dy=\"0.35em\" font-size=\"")
				.Append(LabelFontSize).Append("\" font-weight=\"600\" fill=\"var(--_text)\">");
			MultilineUtils.AppendEscapedXml(sb, set.Label.AsSpan());
			_ = sb.Append("</text>");
		}

		foreach (var union in diagram.Unions)
		{
			if (union.Label is not { Length: > 0 })
				continue;

			var ux = 0.0;
			var uy = 0.0;
			var count = 0;
			foreach (var id in union.SetIds)
			{
				if (setPositions.TryGetValue(id, out var pos))
				{
					ux += pos.X;
					uy += pos.Y;
					count++;
				}
			}
			if (count == 0)
				continue;

			ux /= count;
			uy /= count;

			_ = sb.Append("\n<text x=\"").Append(F(ux)).Append("\" y=\"").Append(F(uy))
				.Append("\" text-anchor=\"middle\" dy=\"0.35em\" font-size=\"")
				.Append(UnionLabelFontSize).Append("\" font-weight=\"500\" fill=\"var(--_text)\">");
			MultilineUtils.AppendEscapedXml(sb, union.Label.AsSpan());
			_ = sb.Append("</text>");
		}

		_ = sb.Append("\n</svg>");
		return sb;
	}

	private static List<(double X, double Y)> ComputePositions(int n)
	{
		var positions = new List<(double X, double Y)>(n);
		if (n == 1)
		{
			positions.Add((CenterX, CenterY));
		}
		else if (n == 2)
		{
			var offset = BaseRadius * 0.55;
			positions.Add((CenterX - offset, CenterY));
			positions.Add((CenterX + offset, CenterY));
		}
		else
		{
			var arrangeRadius = BaseRadius * 0.6;
			for (var i = 0; i < n; i++)
			{
				var angle = (2 * Math.PI * i / n) - (Math.PI / 2);
				positions.Add((CenterX + (arrangeRadius * Math.Cos(angle)), CenterY + (arrangeRadius * Math.Sin(angle))));
			}
		}
		return positions;
	}

	private static string F(double value) =>
		value.ToString("0.##", CultureInfo.InvariantCulture);
}
