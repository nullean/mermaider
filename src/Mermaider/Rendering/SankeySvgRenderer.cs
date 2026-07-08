using System.Globalization;
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

	private static readonly string[] NodeColors =
	[
		"#4e79a7", "#f28e2b", "#e15759", "#76b7b2",
		"#59a14f", "#edc948", "#b07aa1", "#ff9da7",
		"#9c755f", "#bab0ac", "#86bcb6", "#8cd17d",
	];

	private sealed class NodeLayout
	{
		public required string Name { get; init; }
		public int Layer { get; set; }
		public double Value { get; set; }
		public double Y0 { get; set; }
		public double Y1 { get; set; }
		public double X0 { get; set; }
		public double X1 { get; set; }
		public string Color { get; set; } = NodeColors[0];
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
	}

	internal static string Render(SankeyDiagram diagram, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
	{
		var sb = RenderToBuilder(diagram, colors, font, transparent, strict, accessibility, diagramType);
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

	internal static StringBuilder RenderToBuilder(SankeyDiagram diagram, DiagramColors colors, string font, bool transparent, StrictModeOptions? strict = null, AccessibilityInfo? accessibility = null, DiagramType? diagramType = null)
	{
		var sb = SharedStringBuilderPool.Instance.Get();

		if (diagram.Links.Count == 0)
		{
			StyleBlock.AppendSvgOpenTag(sb, 320, 120, colors, transparent, accessibility, diagramType);
			StyleBlock.AppendStyleBlock(sb, font, strict);
			_ = sb.Append("\n</svg>");
			return sb;
		}

		var (nodes, links, layerCount) = Layout(diagram);
		var width = DefaultWidth;
		var height = DefaultHeight;

		StyleBlock.AppendSvgOpenTag(sb, width, height, colors, transparent, accessibility, diagramType);
		StyleBlock.AppendStyleBlock(sb, font, strict);
		_ = sb.Append("\n<defs>\n</defs>\n");

		// Links first (under nodes)
		foreach (var link in links)
			AppendLink(sb, link);

		foreach (var node in nodes.Values)
			AppendNode(sb, node, layerCount);

		_ = sb.Append("\n</svg>");
		return sb;
	}

	private static (Dictionary<string, NodeLayout> Nodes, List<LinkLayout> Links, int LayerCount) Layout(SankeyDiagram diagram)
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

		var layerCount = nodes.Values.Max(n => n.Layer) + 1;
		if (layerCount < 1)
			layerCount = 1;

		// Color nodes
		var colorIdx = 0;
		foreach (var n in nodes.Values.OrderBy(n => n.Name, StringComparer.Ordinal))
			n.Color = NodeColors[colorIdx++ % NodeColors.Length];

		// Horizontal positions
		var chartW = DefaultWidth - (Margin * 2) - 120; // leave room for labels
		var chartH = DefaultHeight - (Margin * 2);
		var layerGap = layerCount <= 1 ? 0 : chartW / (layerCount - 1);

		foreach (var n in nodes.Values)
		{
			n.X0 = Margin + 60 + (n.Layer * layerGap);
			n.X1 = n.X0 + NodeWidth;
		}

		// Vertical stack per layer, proportional heights
		var byLayer = nodes.Values.GroupBy(n => n.Layer).OrderBy(g => g.Key);
		foreach (var group in byLayer)
		{
			var list = group.OrderByDescending(n => n.Value).ThenBy(n => n.Name, StringComparer.Ordinal).ToList();
			var total = list.Sum(n => n.Value);
			if (total <= 0)
				total = list.Count;

			var usable = chartH - (NodePad * Math.Max(0, list.Count - 1));
			var y = Margin;
			foreach (var n in list)
			{
				var h = Math.Max(4, usable * (n.Value / total));
				n.Y0 = y;
				n.Y1 = y + h;
				n.OutCursor = n.Y0;
				n.InCursor = n.Y0;
				y = n.Y1 + NodePad;
			}
		}

		// Link vertical slots (source out, target in)
		var linkLayouts = new List<LinkLayout>(edges.Count);
		foreach (var (s, t, v) in edges.OrderBy(e => nodes[e.Source].Layer).ThenBy(e => e.Source).ThenBy(e => e.Target))
		{
			var src = nodes[s];
			var tgt = nodes[t];
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
		var color = link.Source.Color;

		// Ribbon path: source edge → cubic → target edge → back
		_ = sb.Append("\n<path d=\"M ").Append(F(x0)).Append(' ').Append(F(link.Sy0))
			.Append(" C ").Append(F(midX)).Append(' ').Append(F(link.Sy0))
			.Append(' ').Append(F(midX)).Append(' ').Append(F(link.Ty0))
			.Append(' ').Append(F(x1)).Append(' ').Append(F(link.Ty0))
			.Append(" L ").Append(F(x1)).Append(' ').Append(F(link.Ty1))
			.Append(" C ").Append(F(midX)).Append(' ').Append(F(link.Ty1))
			.Append(' ').Append(F(midX)).Append(' ').Append(F(link.Sy1))
			.Append(' ').Append(F(x0)).Append(' ').Append(F(link.Sy1))
			.Append(" Z\" fill=\"").Append(color).Append("\" fill-opacity=\"0.45\" stroke=\"none\" />");
	}

	private static void AppendNode(StringBuilder sb, NodeLayout node, int layerCount)
	{
		_ = sb.Append("\n<rect x=\"").Append(F(node.X0)).Append("\" y=\"").Append(F(node.Y0))
			.Append("\" width=\"").Append(F(NodeWidth)).Append("\" height=\"").Append(F(Math.Max(1, node.Y1 - node.Y0)))
			.Append("\" fill=\"").Append(node.Color).Append("\" stroke=\"none\" rx=\"2\" ry=\"2\" />");

		// Labels: left of first layer, right of last, otherwise right of node
		var isLeft = node.Layer == 0;
		var isRight = node.Layer == layerCount - 1 && layerCount > 1;
		double lx;
		string anchor;
		if (isLeft)
		{
			lx = node.X0 - LabelPad;
			anchor = "end";
		}
		else if (isRight)
		{
			lx = node.X1 + LabelPad;
			anchor = "start";
		}
		else
		{
			lx = node.X1 + LabelPad;
			anchor = "start";
		}

		var midY = (node.Y0 + node.Y1) * 0.5;
		_ = sb.Append("\n<text x=\"").Append(F(lx)).Append("\" y=\"").Append(F(midY))
			.Append("\" text-anchor=\"").Append(anchor)
			.Append("\" dy=\"").Append(RenderConstants.TextBaselineShift)
			.Append("\" font-size=\"").Append(LabelFontSize)
			.Append("\" fill=\"var(--_text)\">");
		MultilineUtils.AppendEscapedXml(sb, node.Name.AsSpan());
		_ = sb.Append("</text>");
	}

	private static string F(double value) =>
		value.ToString("0.##", CultureInfo.InvariantCulture);
}
