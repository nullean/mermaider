using Mermaider.Models;
using Mermaider.Rendering;
using Mermaider.Text;
using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Core.Layout;
using Microsoft.Msagl.Core.Routing;
using Microsoft.Msagl.Layout.Layered;
using MsaglEdge = Microsoft.Msagl.Core.Layout.Edge;
using MsaglNode = Microsoft.Msagl.Core.Layout.Node;
using MsaglPoint = Microsoft.Msagl.Core.Geometry.Point;
using PlaneTransformation = Microsoft.Msagl.Core.Geometry.Curves.PlaneTransformation;

namespace Mermaider.Layout.Msagl;

internal static class MsaglFlowchartLayout
{
	internal static PositionedGraph Layout(MermaidGraph graph, RenderOptions? options = null, StrictModeOptions? strict = null)
	{
		var padding = options?.Padding ?? LayoutDefaults.Padding;
		var nodeSpacing = options?.NodeSpacing ?? LayoutDefaults.NodeSpacing;
		var layerSpacing = options?.LayerSpacing ?? LayoutDefaults.LayerSpacing;

		var geometryGraph = new GeometryGraph();
		var msaglNodes = new Dictionary<string, MsaglNode>();

		foreach (var (id, node) in graph.Nodes)
		{
			var (w, h) = NodeSizing.Estimate(node.Label, node.Shape);
			var msaglNode = new MsaglNode(CurveFactory.CreateRectangle(w, h, new MsaglPoint(0, 0)), id);
			msaglNodes[id] = msaglNode;
			geometryGraph.Nodes.Add(msaglNode);
		}

		var edgeMap = new List<(MsaglEdge MsaglEdge, MermaidEdge MermaidEdge)>();
		foreach (var edge in graph.Edges)
		{
			if (!msaglNodes.TryGetValue(edge.Source, out var sourceNode) ||
				!msaglNodes.TryGetValue(edge.Target, out var targetNode))
				continue;

			var msaglEdge = new MsaglEdge(sourceNode, targetNode);
			if (edge.Label is { Length: > 0 })
			{
				var metrics = TextMetrics.MeasureMultiline(
					edge.Label.AsSpan(),
					RenderConstants.FontSizes.EdgeLabel,
					RenderConstants.FontWeights.EdgeLabel);
				msaglEdge.Label = new Label(metrics.Width + 8, metrics.Height + 6, msaglEdge);
			}
			edgeMap.Add((msaglEdge, edge));
			geometryGraph.Edges.Add(msaglEdge);
		}

		var settings = new SugiyamaLayoutSettings
		{
			NodeSeparation = nodeSpacing,
			LayerSeparation = layerSpacing,
			EdgeRoutingSettings =
			{
				EdgeRoutingMode = EdgeRoutingMode.Rectilinear,
				Padding = 4
			}
		};

		ConfigureDirection(settings, graph.Direction);

		var layout = new LayeredLayout(geometryGraph, settings);
		layout.Run();

		return ExtractPositioned(geometryGraph, graph, msaglNodes, edgeMap, padding, strict);
	}

	private static void ConfigureDirection(SugiyamaLayoutSettings settings, Direction direction)
	{
		settings.Transformation = direction switch
		{
			Direction.LR => new PlaneTransformation(0, -1, 0, 1, 0, 0),
			Direction.RL => new PlaneTransformation(0, 1, 0, -1, 0, 0),
			Direction.BT => new PlaneTransformation(-1, 0, 0, 0, -1, 0),
			_ => PlaneTransformation.UnitTransformation,
		};
	}

	private static PositionedGraph ExtractPositioned(
		GeometryGraph geometryGraph,
		MermaidGraph graph,
		Dictionary<string, MsaglNode> msaglNodes,
		List<(MsaglEdge MsaglEdge, MermaidEdge MermaidEdge)> edgeMap,
		double padding,
		StrictModeOptions? strict = null)
	{
		var bb = geometryGraph.BoundingBox;
		var offsetX = -bb.Left + padding;
		var offsetY = -bb.Bottom + padding;

		var positionedNodes = new List<PositionedNode>(graph.Nodes.Count);
		foreach (var (id, node) in graph.Nodes)
		{
			if (!msaglNodes.TryGetValue(id, out var msaglNode))
				continue;

			var center = msaglNode.Center;
			var w = msaglNode.BoundingBox.Width;
			var h = msaglNode.BoundingBox.Height;

			var inlineStyle = strict is null ? ResolveNodeStyle(id, graph) : null;
			var cssClass = strict is not null && graph.ClassAssignments.TryGetValue(id, out var cls) ? cls : null;

			positionedNodes.Add(new PositionedNode
			{
				Id = id,
				Label = node.Label,
				Shape = node.Shape,
				X = center.X - (w / 2) + offsetX,
				Y = center.Y - (h / 2) + offsetY,
				Width = w,
				Height = h,
				InlineStyle = inlineStyle,
				CssClassName = cssClass,
			});
		}

		var positionedEdges = new List<PositionedEdge>(edgeMap.Count);
		foreach (var (msaglEdge, mermaidEdge) in edgeMap)
		{
			var points = ExtractEdgePoints(msaglEdge, offsetX, offsetY);
			Point? labelPos = null;
			if (msaglEdge.Label != null)
			{
				var lc = msaglEdge.Label.Center;
				labelPos = new Point(lc.X + offsetX, lc.Y + offsetY);
			}

			positionedEdges.Add(new PositionedEdge
			{
				Source = mermaidEdge.Source,
				Target = mermaidEdge.Target,
				Label = mermaidEdge.Label,
				Style = mermaidEdge.Style,
				HasArrowStart = mermaidEdge.HasArrowStart,
				HasArrowEnd = mermaidEdge.HasArrowEnd,
				Points = points,
				LabelPosition = labelPos,
			});
		}

		var width = bb.Width + (padding * 2);
		var height = bb.Height + (padding * 2);

		return new PositionedGraph
		{
			Width = width,
			Height = height,
			Nodes = positionedNodes,
			Edges = positionedEdges,
			Groups = ExtractGroups(graph, msaglNodes, offsetX, offsetY),
		};
	}

	private static List<Point> ExtractEdgePoints(MsaglEdge edge, double offsetX, double offsetY)
	{
		var points = new List<Point>();
		var curve = edge.Curve;
		if (curve is null)
			return points;

		if (edge.EdgeGeometry?.SourceArrowhead?.TipPosition is { } srcTip)
			points.Add(new Point(srcTip.X + offsetX, srcTip.Y + offsetY));
		else
			points.Add(new Point(curve.Start.X + offsetX, curve.Start.Y + offsetY));

		switch (curve)
		{
			case LineSegment line:
				points.Add(new Point(line.End.X + offsetX, line.End.Y + offsetY));
				break;
			case Curve composite:
				foreach (var seg in composite.Segments)
				{
					if (seg is LineSegment ls)
					{
						points.Add(new Point(ls.End.X + offsetX, ls.End.Y + offsetY));
					}
					else
					{
						var steps = 4;
						for (var t = 1; t <= steps; t++)
						{
							var frac = seg.ParStart + ((seg.ParEnd - seg.ParStart) * t / steps);
							var p = seg[frac];
							points.Add(new Point(p.X + offsetX, p.Y + offsetY));
						}
					}
				}
				break;
			default:
				{
					var steps = 8;
					for (var t = 1; t <= steps; t++)
					{
						var frac = curve.ParStart + ((curve.ParEnd - curve.ParStart) * t / steps);
						var p = curve[frac];
						points.Add(new Point(p.X + offsetX, p.Y + offsetY));
					}
					break;
				}
		}

		if (edge.EdgeGeometry?.TargetArrowhead?.TipPosition is { } tgtTip)
		{
			if (points.Count > 0)
				points[^1] = new Point(tgtTip.X + offsetX, tgtTip.Y + offsetY);
			else
				points.Add(new Point(tgtTip.X + offsetX, tgtTip.Y + offsetY));
		}

		return points;
	}

	private static IReadOnlyList<PositionedGroup> ExtractGroups(
		MermaidGraph graph,
		Dictionary<string, MsaglNode> msaglNodes,
		double offsetX, double offsetY)
	{
		if (graph.Subgraphs.Count == 0)
			return [];

		var groups = new List<PositionedGroup>();
		foreach (var sg in graph.Subgraphs)
			groups.Add(ExtractGroup(sg, msaglNodes, offsetX, offsetY));
		return groups;
	}

	private static PositionedGroup ExtractGroup(
		MermaidSubgraph sg,
		Dictionary<string, MsaglNode> msaglNodes,
		double offsetX, double offsetY)
	{
		var groupPadding = 16.0;
		var headerHeight = 28.0;
		var minX = double.MaxValue;
		var minY = double.MaxValue;
		var maxX = double.MinValue;
		var maxY = double.MinValue;

		foreach (var nodeId in sg.NodeIds)
		{
			if (!msaglNodes.TryGetValue(nodeId, out var n))
				continue;
			var nbb = n.BoundingBox;
			minX = Math.Min(minX, nbb.Left + offsetX);
			minY = Math.Min(minY, nbb.Bottom + offsetY);
			maxX = Math.Max(maxX, nbb.Right + offsetX);
			maxY = Math.Max(maxY, nbb.Top + offsetY);
		}

		var childGroups = new List<PositionedGroup>();
		foreach (var child in sg.Children)
		{
			var childGroup = ExtractGroup(child, msaglNodes, offsetX, offsetY);
			childGroups.Add(childGroup);
			minX = Math.Min(minX, childGroup.X);
			minY = Math.Min(minY, childGroup.Y);
			maxX = Math.Max(maxX, childGroup.X + childGroup.Width);
			maxY = Math.Max(maxY, childGroup.Y + childGroup.Height);
		}

		if (Math.Abs(minX - double.MaxValue) < double.Epsilon)
		{
			minX = 0;
			minY = 0;
			maxX = 100;
			maxY = 60;
		}

		return new PositionedGroup
		{
			Id = sg.Id,
			Label = sg.Label,
			X = minX - groupPadding,
			Y = minY - groupPadding - headerHeight,
			Width = maxX - minX + (groupPadding * 2),
			Height = maxY - minY + (groupPadding * 2) + headerHeight,
			Children = childGroups,
		};
	}

	private static IReadOnlyDictionary<string, string>? ResolveNodeStyle(string nodeId, MermaidGraph graph)
	{
		Dictionary<string, string>? result = null;

		if (graph.ClassAssignments.TryGetValue(nodeId, out var className) &&
			graph.ClassDefs.TryGetValue(className, out var classDef))
		{
			result = new Dictionary<string, string>(classDef);
		}

		if (graph.NodeStyles.TryGetValue(nodeId, out var nodeStyle))
		{
			result ??= [];
			foreach (var kvp in nodeStyle)
				result[kvp.Key] = kvp.Value;
		}

		return result;
	}
}
