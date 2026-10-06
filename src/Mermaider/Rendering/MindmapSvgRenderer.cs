using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class MindmapSvgRenderer
{
	private const double WideAspect = 3.6;
	private const double TargetAspect = 1.3;
	private const double HorizontalGap = 56;
	private const double MaxLabelWidth = 190;
	private const double VerticalGap = 50;
	private const double NodePadX = 20;
	private const double NodePadY = 10;
	private const double Margin = DesignSystem.BoardMargin;


	internal static string Render(MindmapDiagram diagram, SvgRenderContext context)
	{
		var sb = RenderToBuilder(diagram, context);
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

	internal static StringBuilder RenderToBuilder(MindmapDiagram diagram, SvgRenderContext context)
	{
		var sb = SharedStringBuilderPool.Instance.Get();
		var ds = DesignSystem.For(context);

		var positioned = new List<PositionedMindmapNode>();
		var isVertical = false;
		LayoutBalanced(diagram.Root, positioned, context.Limits, transposed: false);

		// A flat, wide tree (long labels side by side) reads better with the branches above and below the root.
		var (flatW, flatH) = Extent(positioned);
		if (flatW > flatH * WideAspect)
		{
			var vertical = new List<PositionedMindmapNode>();
			LayoutBalanced(diagram.Root, vertical, context.Limits, transposed: true);
			var (vw, vh) = Extent(vertical);
			if (Math.Abs(Math.Log(vw / vh / TargetAspect)) < Math.Abs(Math.Log(flatW / flatH / TargetAspect)))
			{
				positioned = vertical;
				isVertical = true;
			}
		}

		var maxX = 0.0;
		var maxY = 0.0;
		foreach (var node in positioned)
		{
			var right = node.X + node.W;
			var bottom = node.Y + node.H;
			if (right > maxX)
				maxX = right;
			if (bottom > maxY)
				maxY = bottom;
		}

		var width = maxX + Margin;
		var height = maxY + Margin;

		StyleBlock.AppendSvgOpenTag(sb, width, height, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
		StyleBlock.AppendStyleBlock(sb, context.Styles);

		// links first: branches in their family stroke, rounded orthogonal like every other edge (the preset's bend radius)
		foreach (var node in positioned)
		{
			if (node.ParentCx is not null)
				AppendLink(sb, ds, node, Family(ds, node), isVertical, context.EdgeRadius);
		}

		foreach (var node in positioned)
			AppendNode(sb, ds, node, Family(ds, node));

		ds.Close(sb);
		return sb;
	}

	private sealed record PositionedMindmapNode(
		double X, double Y, double W, double H,
		string Label, MindmapShape Shape, int Branch,
		int Depth, double? ParentCx, double? ParentCy, double ParentHalfW = 0, double ParentHalfH = 0);

	// the root is the accent; every top-level branch takes a cluster family of its own (p1, p2 …)
	private static ColorFamily Family(DesignSystem ds, PositionedMindmapNode node) =>
		node.Branch < 0 ? ds.Accent : ds.Cluster(node.Branch);

	// Like mermaid.js, the root sits in the middle and its branches spread to both sides; the sides are balanced by height.
	private static (double W, double H) Extent(List<PositionedMindmapNode> nodes) =>
		(nodes.Max(n => n.X + n.W) - nodes.Min(n => n.X), nodes.Max(n => n.Y + n.H) - nodes.Min(n => n.Y));

	// transposed: laid out with x and y swapped (depth runs downwards, siblings spread sideways), then swapped back
	private static void LayoutBalanced(MindmapNode root, List<PositionedMindmapNode> result, ResourceLimits limits, bool transposed)
	{
		var (rootLabel, rootW, rootH) = Measure(root, 0, transposed);
		var rootNode = new PositionedMindmapNode(-rootW / 2, -rootH / 2, rootW, rootH, rootLabel, root.Shape, -1, 0, null, null);

		// each branch laid out to the right of x = 0 on its own
		var branches = new List<(List<PositionedMindmapNode> Nodes, double Height)>();
		for (var i = 0; i < root.Children.Count; i++)
		{
			var nodes = new List<PositionedMindmapNode>();
			var height = LayoutTree(root.Children[i], 0, 0, 1, nodes, limits, i + 1, transposed);
			branches.Add((nodes, height));
		}

		// contiguous split closest to half the total height: first part right, the rest left
		var split = 0;
		var best = double.MaxValue;
		for (var k = 0; k <= branches.Count; k++)
		{
			var right = branches.Take(k).Sum(b => b.Height) + (VerticalGap * Math.Max(0, k - 1));
			var left = branches.Skip(k).Sum(b => b.Height) + (VerticalGap * Math.Max(0, branches.Count - k - 1));
			var diff = Math.Abs(right - left);
			if (k > 0 && k < branches.Count && diff < best)
			{
				best = diff;
				split = k;
			}
		}

		if (branches.Count == 1)
			split = 1;

		result.Add(rootNode);
		PlaceSide(branches.Take(split).ToList(), +1, rootW, rootH, result);
		PlaceSide(branches.Skip(split).ToList(), -1, rootW, rootH, result);

		if (transposed)
		{
			for (var i = 0; i < result.Count; i++)
			{
				var n = result[i];
				result[i] = n with { X = n.Y, Y = n.X, W = n.H, H = n.W, ParentCx = n.ParentCy, ParentCy = n.ParentCx, ParentHalfW = n.ParentHalfH, ParentHalfH = n.ParentHalfW };
			}
		}

		// normalise to the canvas margin
		var minX = result.Min(n => n.X);
		var minY = result.Min(n => n.Y);
		for (var i = 0; i < result.Count; i++)
		{
			var n = result[i];
			result[i] = n with
			{
				X = n.X - minX + Margin,
				Y = n.Y - minY + Margin,
				ParentCx = n.ParentCx - minX + Margin,
				ParentCy = n.ParentCy - minY + Margin,
			};
		}
	}

	private static void PlaceSide(List<(List<PositionedMindmapNode> Nodes, double Height)> side, int dir, double rootW, double rootH, List<PositionedMindmapNode> result)
	{
		if (side.Count == 0)
			return;

		var sideHeight = side.Sum(b => b.Height) + (VerticalGap * (side.Count - 1));
		var y = -sideHeight / 2;
		var startX = (rootW / 2) + HorizontalGap;
		foreach (var (nodes, height) in side)
		{
			for (var i = 0; i < nodes.Count; i++)
			{
				var n = nodes[i];
				var x = dir > 0 ? startX + n.X : -startX - n.X - n.W;
				var px = n.ParentCx is { } pcx ? (dir > 0 ? startX + pcx : -startX - pcx) : (double?)null;
				var py = n.ParentCy is { } pcy ? pcy + y : (double?)null;
				var (phw, phh) = (n.ParentHalfW, n.ParentHalfH);
				// the branch's own node is added last and hangs off the root centre
				if (i == nodes.Count - 1)
				{
					px = 0;
					py = 0;
					(phw, phh) = (rootW / 2, rootH / 2);
				}

				result.Add(n with { X = x, Y = n.Y + y, ParentCx = px, ParentCy = py, ParentHalfW = phw, ParentHalfH = phh });
			}

			y += height + VerticalGap;
		}
	}

	private static (string Label, double W, double H) Measure(MindmapNode node, int depth, bool transposed)
	{
		var (label, w, h) = Measure(node, depth);
		return transposed ? (label, h, w) : (label, w, h);
	}

	private static (string Label, double W, double H) Measure(MindmapNode node, int depth)
	{
		var fontSizePx = DesignSystem.Px(Role(depth));
		var weight = MeasureWeight(depth);
		var label = Wrap(node.Label, fontSizePx, weight);
		var metrics = TextMetrics.MeasureMultiline(label.AsSpan(), fontSizePx, weight);
		var w = metrics.Width + (NodePadX * 2);
		var h = metrics.Height + (NodePadY * 2);
		if (node.Shape == MindmapShape.Circle)
			w = h = Math.Sqrt((w * w) + (h * h));
		else if (node.Shape == MindmapShape.Bang)
			(w, h) = (w * 1.35, h * 1.6);
		else if (node.Shape == MindmapShape.Cloud)
			(w, h) = (w * 1.2, h * 1.45);
		return (label, w, h);
	}

	private static double LayoutTree(MindmapNode node, double x, double y, int depth, List<PositionedMindmapNode> result, ResourceLimits limits, int branch, bool transposed)
	{
		ResourceGuard.CheckRecursionDepth(depth, limits);
		var (label, w, h) = Measure(node, depth, transposed);

		if (node.Children.Count == 0)
		{
			result.Add(new PositionedMindmapNode(x, y, w, h, label, node.Shape, branch, depth, null, null));
			return h;
		}

		var childX = x + w + HorizontalGap;
		var childY = y;
		var totalChildHeight = 0.0;

		var childPositions = new List<int>();
		foreach (var child in node.Children)
		{
			var childH = LayoutTree(child, childX, childY, depth + 1, result, limits, branch, transposed);
			childPositions.Add(result.Count - 1); // a node is added after its descendants
			childY += childH + VerticalGap;
			totalChildHeight += childH + VerticalGap;
		}
		totalChildHeight -= VerticalGap;

		var nodeY = y + (totalChildHeight / 2) - (h / 2);
		var nodeCx = x + (w / 2);
		var nodeCy = nodeY + (h / 2);

		result.Add(new PositionedMindmapNode(x, nodeY, w, h, label, node.Shape, branch, depth, null, null));

		for (var i = 0; i < childPositions.Count; i++)
		{
			var idx = childPositions[i];
			var child = result[idx];
			result[idx] = child with { ParentCx = nodeCx, ParentCy = nodeCy, ParentHalfW = w / 2, ParentHalfH = h / 2 };
		}

		return Math.Max(totalChildHeight, h);
	}

	// Long labels wrap into lines so a node never grows without bound.
	private static string Wrap(string text, double fontPx, int weight)
	{
		if (TextMetrics.MeasureTextWidth(text, fontPx, weight) <= MaxLabelWidth)
			return text;
		var lines = new List<string>();
		var current = "";
		foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			var candidate = current.Length == 0 ? word : current + " " + word;
			if (current.Length > 0 && TextMetrics.MeasureTextWidth(candidate, fontPx, weight) > MaxLabelWidth)
			{
				lines.Add(current);
				current = word;
			}
			else
			{
				current = candidate;
			}
		}

		if (current.Length > 0)
			lines.Add(current);
		return string.Join('\n', lines);
	}

	private static TypeRole Role(int depth) => depth == 0 ? TypeRole.Heading : TypeRole.Label;

	// root and first-level branches are set heavier; layout measures at the heaviest weight any preset uses
	private static int MeasureWeight(int depth) => depth <= 1 ? 700 : DesignSystem.MeasureWeight(TypeRole.Label);

	private static int Weight(DesignSystem ds, int depth) => depth switch
	{
		0 => 700,
		1 => ds.Spec.HeadingWeight,
		_ => ds.Weight(TypeRole.Label),
	};

	/// <summary>An elbow from the parent's edge to the child's near edge, bending halfway across the gap.</summary>
	private static void AppendLink(StringBuilder sb, DesignSystem ds, PositionedMindmapNode node, ColorFamily family, bool vertical, double radius)
	{
		var (pcx, pcy) = (node.ParentCx!.Value, node.ParentCy!.Value);
		var (cx, cy) = (node.X + (node.W / 2), node.Y + (node.H / 2));
		List<Point> points;
		if (vertical)
		{
			var dir = Math.Sign(cy - pcy);
			var sy = pcy + (dir * node.ParentHalfH);
			var ey = dir > 0 ? node.Y : node.Y + node.H;
			var mid = (sy + ey) / 2;
			points = [new(pcx, sy), new(pcx, mid), new(cx, mid), new(cx, ey)];
		}
		else
		{
			var dir = Math.Sign(cx - pcx);
			var sx = pcx + (dir * node.ParentHalfW);
			var ex = dir > 0 ? node.X : node.X + node.W;
			var mid = (sx + ex) / 2;
			points = [new(sx, pcy), new(mid, pcy), new(mid, cy), new(ex, cy)];
		}

		points = points.Select(p => new Point(Math.Round(p.X, 2), Math.Round(p.Y, 2))).ToList();
		if (Math.Abs(points[1].X - points[2].X) < 0.01 && Math.Abs(points[1].Y - points[2].Y) < 0.01)
			points = [points[0], points[3]]; // in line with its parent: a straight link
		_ = sb.Append("\n<path class=\"mindmap-link\" d=\"");
		SvgRenderer.BuildOrthogonalPath(sb, points, radius);
		_ = sb.Append("\" fill=\"none\" stroke=\"").Append(family.Stroke)
			.Append("\" stroke-width=\"").Append(DesignSystem.Num(ds.Spec.EdgeWidth + 0.25)).Append("\" stroke-linecap=\"round\" stroke-linejoin=\"round\" />");
	}

	// A scalloped outline: bumps along an ellipse, each drawn as a quadratic bulging outwards.
	private static void AppendCloud(StringBuilder sb, double cx, double cy, double rx, double ry, string paint)
	{
		const int bumps = 10;
		const double bulge = 1.28;
		_ = sb.Append("\n<path d=\"");
		for (var i = 0; i < bumps; i++)
		{
			var a0 = 2 * Math.PI * i / bumps;
			var a1 = 2 * Math.PI * (i + 1) / bumps;
			var am = (a0 + a1) / 2;
			var (x0, y0) = (cx + (Math.Cos(a0) * rx * 0.9), cy + (Math.Sin(a0) * ry * 0.9));
			var (x1, y1) = (cx + (Math.Cos(a1) * rx * 0.9), cy + (Math.Sin(a1) * ry * 0.9));
			var (qx, qy) = (cx + (Math.Cos(am) * rx * bulge), cy + (Math.Sin(am) * ry * bulge));
			if (i == 0)
				_ = sb.Append('M').Append(x0.SvgFormat()).Append(',').Append(y0.SvgFormat());
			_ = sb.Append(" Q").Append(qx.SvgFormat()).Append(',').Append(qy.SvgFormat()).Append(' ').Append(x1.SvgFormat()).Append(',').Append(y1.SvgFormat());
		}

		_ = sb.Append(" Z\" ").Append(paint).Append(" />");
	}

	private static void AppendNode(StringBuilder sb, DesignSystem ds, PositionedMindmapNode node, ColorFamily family)
	{
		var cx = node.X + (node.W / 2);
		var cy = node.Y + (node.H / 2);
		var sw = node.Depth == 0 ? ds.TerminalStrokeWidth : ds.NodeStrokeWidth;
		var paint = $"fill=\"{ds.NodeFill(family)}\" stroke=\"{ds.NodeStroke(family)}\" stroke-width=\"{sw}\"";

		_ = sb.Append("\n<g class=\"node mindmap-node\" data-depth=\"").Append(node.Depth).Append("\" data-label=\"");
		MultilineUtils.AppendEscapedAttr(sb, node.Label.AsSpan());
		_ = sb.Append("\">");
		switch (node.Shape)
		{
			case MindmapShape.Circle:
				var r = Math.Max(node.W, node.H) / 2;
				_ = sb.Append("\n<circle cx=\"").Append(cx.SvgFormat()).Append("\" cy=\"").Append(cy.SvgFormat())
					.Append("\" r=\"").Append(r.SvgFormat())
					.Append("\" ").Append(paint).Append(" />");
				break;
			case MindmapShape.Hexagon:
				var hy = node.H / 2;
				var inset = hy * 0.6;
				_ = sb.Append("\n<polygon points=\"")
					.Append((node.X + inset).SvgFormat()).Append(',').Append(node.Y.SvgFormat()).Append(' ')
					.Append((node.X + node.W - inset).SvgFormat()).Append(',').Append(node.Y.SvgFormat()).Append(' ')
					.Append((node.X + node.W).SvgFormat()).Append(',').Append(cy.SvgFormat()).Append(' ')
					.Append((node.X + node.W - inset).SvgFormat()).Append(',').Append((node.Y + node.H).SvgFormat()).Append(' ')
					.Append((node.X + inset).SvgFormat()).Append(',').Append((node.Y + node.H).SvgFormat()).Append(' ')
					.Append(node.X.SvgFormat()).Append(',').Append(cy.SvgFormat())
					.Append("\" ").Append(paint).Append(" stroke-linejoin=\"round\" />");
				break;
			case MindmapShape.Bang:
				_ = sb.Append("\n<polygon points=\"");
				const int spikes = 14;
				for (var i = 0; i < spikes * 2; i++)
				{
					var angle = Math.PI * i / spikes;
					var k = i % 2 == 0 ? 1.0 : 0.78;
					_ = sb.Append((cx + (Math.Cos(angle) * node.W / 2 * k)).SvgFormat()).Append(',')
						.Append((cy + (Math.Sin(angle) * node.H / 2 * k)).SvgFormat()).Append(' ');
				}

				_ = sb.Append("\" ").Append(paint).Append(" stroke-linejoin=\"round\" />");
				break;
			case MindmapShape.Cloud:
				AppendCloud(sb, cx, cy, node.W / 2, node.H / 2, paint);
				break;
			case MindmapShape.Square:
				_ = sb.Append('\n');
				ds.AppendBox(sb, node.X, node.Y, node.W, node.H, family);
				break;
			default:
				_ = sb.Append('\n');
				ds.AppendBox(sb, node.X, node.Y, node.W, node.H, family, Math.Min(ds.Spec.NodeRadius + 6, node.H / 2));
				break;
		}

		_ = sb.Append('\n');
		ds.AppendText(sb, node.Label, cx, cy, Role(node.Depth), weight: Weight(ds, node.Depth));
		_ = sb.Append("\n</g>");
	}
}
