using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

internal static class SankeySvgRenderer
{
	private const double DefaultWidth = 800;
	private const double DefaultHeight = 400;
	private const double Margin = 24;
	private const double NodeWidth = 12;
	private const double NodePad = 14;
	private const double LabelPad = 8;
	private const string LabelFontSize = RenderConstants.FsVar.S;


	private sealed class NodeLayout
	{
		public required string Name { get; init; }
		public int Layer { get; set; }
		public double Value { get; set; }
		public double Y0 { get; set; }
		public double Y1 { get; set; }
		public double X0 { get; set; }
		public double X1 { get; set; }
		public string Color { get; set; } = CategoricalPalette.Blue;
		public double OutCursor { get; set; }
		public double InCursor { get; set; }
	}

	private sealed class LinkLayout
	{
		public required NodeLayout Source { get; init; }
		public required NodeLayout Target { get; init; }
		public double Value { get; init; }
		public double Sy0 { get; set; }
		public double Sy1 { get; set; }
		public double Ty0 { get; set; }
		public double Ty1 { get; set; }
		public int Index { get; set; }
	}

	internal static string Render(SankeyDiagram diagram, SvgRenderContext context)
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

	internal static StringBuilder RenderToBuilder(SankeyDiagram diagram, SvgRenderContext context)
	{
		var sb = SharedStringBuilderPool.Instance.Get();

		if (diagram.Links.Count == 0)
		{
			StyleBlock.AppendSvgOpenTag(sb, 320, 120, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
			StyleBlock.AppendStyleBlock(sb, context.Styles.Font, context.Styles.Strict, context.Styles.FontScale, context.Styles.MonoFont);
			_ = sb.Append("\n</svg>");
			return sb;
		}

		var (nodes, links, layerCount) = Layout(diagram, context.Styles.Colors);
		var width = DefaultWidth;
		var height = DefaultHeight;

		StyleBlock.AppendSvgOpenTag(sb, width, height, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
		StyleBlock.AppendStyleBlock(sb, context.Styles.Font, context.Styles.Strict, context.Styles.FontScale, context.Styles.MonoFont);

		// Emit gradient defs, one per link
		_ = sb.Append("\n<defs>");
		foreach (var link in links)
		{
			_ = sb.Append("\n<linearGradient id=\"sankey-grad-").Append(link.Index)
				.Append("\" gradientUnits=\"userSpaceOnUse\" x1=\"").Append(link.Source.X1.SvgFormat())
				.Append("\" y1=\"0\" x2=\"").Append(link.Target.X0.SvgFormat()).Append("\" y2=\"0\">")
				.Append("<stop offset=\"0%\" stop-color=\"").Append(link.Source.Color).Append("\" stop-opacity=\"0.5\" />")
				.Append("<stop offset=\"100%\" stop-color=\"").Append(link.Target.Color).Append("\" stop-opacity=\"0.5\" />")
				.Append("</linearGradient>");
		}
		_ = sb.Append("\n</defs>\n");

		// Links first (under nodes)
		foreach (var link in links)
			AppendLink(sb, link);

		foreach (var node in nodes.Values)
			AppendNode(sb, node, layerCount);

		_ = sb.Append("\n</svg>");
		return sb;
	}

	private static (Dictionary<string, NodeLayout> Nodes, List<LinkLayout> Links, int LayerCount) Layout(SankeyDiagram diagram, DiagramColors colors)
	{
		var nodes = new Dictionary<string, NodeLayout>(StringComparer.Ordinal);
		var edges = new List<(string Source, string Target, double Value)>();

		foreach (var link in diagram.Links)
		{
			if (!nodes.ContainsKey(link.Source))
				nodes[link.Source] = new NodeLayout { Name = link.Source };
			if (!nodes.ContainsKey(link.Target))
				nodes[link.Target] = new NodeLayout { Name = link.Target };
			edges.Add((link.Source, link.Target, link.Value));
		}

		// Outgoing totals for node sizing
		var outSum = new Dictionary<string, double>(StringComparer.Ordinal);
		var inSum = new Dictionary<string, double>(StringComparer.Ordinal);
		foreach (var (s, t, v) in edges)
		{
			outSum[s] = outSum.GetValueOrDefault(s) + v;
			inSum[t] = inSum.GetValueOrDefault(t) + v;
		}

		foreach (var n in nodes.Values)
			n.Value = Math.Max(outSum.GetValueOrDefault(n.Name), inSum.GetValueOrDefault(n.Name));

		// Layer assignment: longest-path from sources (nodes with no incoming)
		var indegree = new Dictionary<string, int>(StringComparer.Ordinal);
		var outgoing = new Dictionary<string, List<string>>(StringComparer.Ordinal);
		foreach (var name in nodes.Keys)
		{
			indegree[name] = 0;
			outgoing[name] = [];
		}
		foreach (var (s, t, _) in edges)
		{
			if (s == t)
				continue;
			indegree[t] = indegree.GetValueOrDefault(t) + 1;
			outgoing[s].Add(t);
		}

		var queue = new Queue<string>();
		foreach (var (name, deg) in indegree)
		{
			if (deg == 0)
			{
				nodes[name].Layer = 0;
				queue.Enqueue(name);
			}
		}

		// If fully cyclic, pin first node at 0
		if (queue.Count == 0 && nodes.Count > 0)
		{
			var first = nodes.Keys.First();
			nodes[first].Layer = 0;
			queue.Enqueue(first);
		}

		while (queue.Count > 0)
		{
			var u = queue.Dequeue();
			foreach (var v in outgoing[u])
			{
				nodes[v].Layer = Math.Max(nodes[v].Layer, nodes[u].Layer + 1);
				indegree[v]--;
				if (indegree[v] == 0)
					queue.Enqueue(v);
			}
		}

		// Residual SCCs / partial cycles never hit indegree 0 — relax edges with a hard layer cap.
		var layerCap = Math.Max(1, nodes.Count - 1);
		for (var iter = 0; iter < nodes.Count; iter++)
		{
			foreach (var (s, t, _) in edges)
			{
				if (string.Equals(s, t, StringComparison.Ordinal))
					continue;
				var next = Math.Min(layerCap, nodes[s].Layer + 1);
				if (next > nodes[t].Layer)
					nodes[t].Layer = next;
			}
		}

		// Compact ranks to dense 0..k-1 so empty/inflated layers don't waste columns
		// or cram nodes to the right after cycle handling.
		var distinctLayers = nodes.Values
			.Select(n => n.Layer)
			.Distinct()
			.OrderBy(l => l)
			.ToArray();
		var layerRemap = new Dictionary<int, int>(distinctLayers.Length);
		for (var i = 0; i < distinctLayers.Length; i++)
			layerRemap[distinctLayers[i]] = i;
		foreach (var n in nodes.Values)
			n.Layer = layerRemap[n.Layer];

		var layerCount = distinctLayers.Length;
		if (layerCount < 1)
			layerCount = 1;

		// Color nodes
		var colorIdx = 0;
		foreach (var n in nodes.Values.OrderBy(n => n.Name, StringComparer.Ordinal))
			n.Color = colors.PaletteAt(colorIdx++);

		// Horizontal positions
		var chartW = DefaultWidth - (Margin * 2) - 120; // leave room for labels
		var chartH = DefaultHeight - (Margin * 2);
		var layerGap = layerCount <= 1 ? 0 : chartW / (layerCount - 1);

		foreach (var n in nodes.Values)
		{
			n.X0 = Margin + 60 + (n.Layer * layerGap);
			n.X1 = n.X0 + NodeWidth;
		}

		// Vertical stack per layer — global proportional heights.
		// Compute the max layer total to establish a single value→height scale,
		// so node heights are proportional across ALL layers (not just within each layer).
		var byLayer = nodes.Values.GroupBy(n => n.Layer).OrderBy(g => g.Key).ToList();

		double maxLayerTotal = 0;
		foreach (var group in byLayer)
		{
			var layerTotal = group.Sum(n => n.Value);
			if (layerTotal > maxLayerTotal)
				maxLayerTotal = layerTotal;
		}
		if (maxLayerTotal <= 0 || double.IsNaN(maxLayerTotal) || double.IsInfinity(maxLayerTotal))
			maxLayerTotal = 1;

		foreach (var group in byLayer)
		{
			var list = group.OrderByDescending(n => n.Value).ThenBy(n => n.Name, StringComparer.Ordinal).ToList();
			var padTotal = NodePad * Math.Max(0, list.Count - 1);
			var usable = Math.Max(list.Count * 4.0, chartH - padTotal);

			// Scale each node by its fraction of the max-layer total
			var stackHeight = 0.0;
			var heights = new double[list.Count];
			for (var i = 0; i < list.Count; i++)
			{
				heights[i] = Math.Max(2, usable * (list[i].Value / maxLayerTotal));
				stackHeight += heights[i];
			}
			stackHeight += padTotal;

			// Center the stack vertically within chartH
			var offset = Margin + Math.Max(0, (chartH - stackHeight) / 2);
			var y = offset;
			for (var i = 0; i < list.Count; i++)
			{
				list[i].Y0 = y;
				list[i].Y1 = y + heights[i];
				list[i].OutCursor = list[i].Y0;
				list[i].InCursor = list[i].Y0;
				y = list[i].Y1 + NodePad;
			}
		}

		// Link vertical slots (source out, target in).
		// Skip self-loops and feedback/same-layer edges (would draw leftward or zero-span ribbons).
		var linkLayouts = new List<LinkLayout>(edges.Count);
		var linkIndex = 0;
		foreach (var (s, t, v) in edges.OrderBy(e => nodes[e.Source].Layer).ThenBy(e => e.Source).ThenBy(e => e.Target))
		{
			if (string.Equals(s, t, StringComparison.Ordinal))
				continue;

			var src = nodes[s];
			var tgt = nodes[t];
			if (tgt.Layer <= src.Layer)
				continue;

			var srcSpan = src.Y1 - src.Y0;
			var tgtSpan = tgt.Y1 - tgt.Y0;
			var srcTotal = Math.Max(outSum.GetValueOrDefault(s), 1e-9);
			var tgtTotal = Math.Max(inSum.GetValueOrDefault(t), 1e-9);
			var srcH = srcSpan * (v / srcTotal);
			var tgtH = tgtSpan * (v / tgtTotal);

			var link = new LinkLayout
			{
				Source = src,
				Target = tgt,
				Value = v,
				Sy0 = src.OutCursor,
				Sy1 = src.OutCursor + srcH,
				Ty0 = tgt.InCursor,
				Ty1 = tgt.InCursor + tgtH,
				Index = linkIndex++,
			};
			src.OutCursor += srcH;
			tgt.InCursor += tgtH;
			linkLayouts.Add(link);
		}

		return (nodes, linkLayouts, layerCount);
	}

	private static void AppendLink(StringBuilder sb, LinkLayout link)
	{
		var x0 = link.Source.X1;
		var x1 = link.Target.X0;
		var midX = (x0 + x1) * 0.5;

		// Ribbon path: source edge → cubic → target edge → back
		_ = sb.Append("\n<path d=\"M ").Append(x0.SvgFormat()).Append(' ').Append(link.Sy0.SvgFormat())
			.Append(" C ").Append(midX.SvgFormat()).Append(' ').Append(link.Sy0.SvgFormat())
			.Append(' ').Append(midX.SvgFormat()).Append(' ').Append(link.Ty0.SvgFormat())
			.Append(' ').Append(x1.SvgFormat()).Append(' ').Append(link.Ty0.SvgFormat())
			.Append(" L ").Append(x1.SvgFormat()).Append(' ').Append(link.Ty1.SvgFormat())
			.Append(" C ").Append(midX.SvgFormat()).Append(' ').Append(link.Ty1.SvgFormat())
			.Append(' ').Append(midX.SvgFormat()).Append(' ').Append(link.Sy1.SvgFormat())
			.Append(' ').Append(x0.SvgFormat()).Append(' ').Append(link.Sy1.SvgFormat())
			.Append(" Z\" fill=\"url(#sankey-grad-").Append(link.Index).Append(")\" stroke=\"none\" />");
	}

	private static void AppendNode(StringBuilder sb, NodeLayout node, int layerCount)
	{
		_ = sb.Append("\n<rect x=\"").Append(node.X0.SvgFormat()).Append("\" y=\"").Append(node.Y0.SvgFormat())
			.Append("\" width=\"").Append(NodeWidth.SvgFormat()).Append("\" height=\"").Append(Math.Max(1, node.Y1 - node.Y0).SvgFormat())
			.Append("\" fill=\"").Append(node.Color).Append("\" stroke=\"none\" rx=\"2\" ry=\"2\" />");

		// Labels: left for all columns except the last
		var isLeft = node.Layer < layerCount - 1;
		var lx = isLeft ? node.X0 - LabelPad : node.X1 + LabelPad;
		var anchor = isLeft ? "end" : "start";

		var midY = (node.Y0 + node.Y1) * 0.5;
		var label = $"{node.Name} {FormatValue(node.Value)}";
		_ = sb.Append("\n<text x=\"").Append(lx.SvgFormat()).Append("\" y=\"").Append(midY.SvgFormat())
			.Append("\" text-anchor=\"").Append(anchor)
			.Append("\" dy=\"").Append(RenderConstants.TextBaselineShift)
			.Append("\" font-size=\"").Append(LabelFontSize)
			.Append("\" fill=\"var(--_text)\">");
		MultilineUtils.AppendEscapedXml(sb, label.AsSpan());
		_ = sb.Append("</text>");
	}

	private static string FormatValue(double value)
	{
		if (value == Math.Floor(value))
			return ((long)value).ToString(System.Globalization.CultureInfo.InvariantCulture);
		return value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
	}

}
