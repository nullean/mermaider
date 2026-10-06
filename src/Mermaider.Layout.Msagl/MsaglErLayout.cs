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

internal static class MsaglErLayout
{
	private const double Padding = 40;
	private const double HeaderHeight = EntityBoxSizing.ErHeaderHeight;
	private const double RowHeight = EntityBoxSizing.ErRowHeight;
	private const double NodeSpacing = 70;
	private const double LayerSpacing = 90;

	internal static PositionedErDiagram Layout(ErDiagram diagram)
	{
		if (diagram.Entities.Count == 0)
			return new PositionedErDiagram { Width = 0, Height = 0, Entities = [], Relationships = [] };

		var entitySizes = new Dictionary<string, (double Width, double Height)>();

		foreach (var entity in diagram.Entities)
		{
			// the same box geometry the built-in layout uses, so the renderer's header / attribute rows fit
			entitySizes[entity.Id] = EntityBoxSizing.Entity(entity, EntityBoxSizing.EntityDegree(diagram, entity.Id));
		}

		var geometryGraph = new GeometryGraph();
		var msaglNodes = new Dictionary<string, MsaglNode>();

		foreach (var entity in diagram.Entities)
		{
			var (w, h) = entitySizes[entity.Id];
			var msaglNode = new MsaglNode(CurveFactory.CreateRectangle(w, h, new MsaglPoint(0, 0)), entity.Id);
			msaglNodes[entity.Id] = msaglNode;
			geometryGraph.Nodes.Add(msaglNode);
		}

		var edgeMap = new List<(MsaglEdge MsaglEdge, ErRelationship Rel)>();
		foreach (var rel in diagram.Relationships)
		{
			if (!msaglNodes.TryGetValue(rel.Entity1, out var sourceNode) ||
				!msaglNodes.TryGetValue(rel.Entity2, out var targetNode))
				continue;

			var msaglEdge = new MsaglEdge(sourceNode, targetNode);
			if (rel.Label.Length > 0)
			{
				var metrics = TextMetrics.MeasureMultiline(rel.Label.AsSpan(), RenderConstants.FontSizes.EdgeLabel, RenderConstants.FontWeights.EdgeLabel);
				msaglEdge.Label = new Label(metrics.Width + 8, metrics.Height + 6, msaglEdge);
			}
			edgeMap.Add((msaglEdge, rel));
			geometryGraph.Edges.Add(msaglEdge);
		}

		var settings = new SugiyamaLayoutSettings
		{
			NodeSeparation = NodeSpacing,
			LayerSeparation = LayerSpacing,
			EdgeRoutingSettings = { EdgeRoutingMode = EdgeRoutingMode.Rectilinear, Padding = 4 },
			Transformation = new PlaneTransformation(0, -1, 0, 1, 0, 0)
		};

		var layout = new LayeredLayout(geometryGraph, settings);
		layout.Run();

		return ExtractPositioned(geometryGraph, diagram, msaglNodes, edgeMap);
	}

	private static PositionedErDiagram ExtractPositioned(
		GeometryGraph geometryGraph,
		ErDiagram diagram,
		Dictionary<string, MsaglNode> msaglNodes,
		List<(MsaglEdge MsaglEdge, ErRelationship Rel)> edgeMap)
	{
		var bb = geometryGraph.BoundingBox;
		var offsetX = -bb.Left + Padding;
		var offsetY = -bb.Bottom + Padding;

		var entityLookup = diagram.Entities.ToDictionary(e => e.Id);
		var positionedEntities = new List<PositionedErEntity>(diagram.Entities.Count);

		foreach (var (id, msaglNode) in msaglNodes)
		{
			if (!entityLookup.TryGetValue(id, out var entity))
				continue;
			var center = msaglNode.Center;
			var w = msaglNode.BoundingBox.Width;
			var h = msaglNode.BoundingBox.Height;

			positionedEntities.Add(new PositionedErEntity
			{
				Id = entity.Id,
				Label = entity.Label,
				Attributes = entity.Attributes,
				X = center.X - (w / 2) + offsetX,
				Y = center.Y - (h / 2) + offsetY,
				Width = w,
				Height = h,
				HeaderHeight = HeaderHeight,
				RowHeight = RowHeight,
			});
		}

		var positionedRels = new List<PositionedErRelationship>(edgeMap.Count);
		foreach (var (msaglEdge, rel) in edgeMap)
		{
			var points = ExtractEdgePoints(msaglEdge, offsetX, offsetY);
			positionedRels.Add(new PositionedErRelationship
			{
				Entity1 = rel.Entity1,
				Entity2 = rel.Entity2,
				Cardinality1 = rel.Cardinality1,
				Cardinality2 = rel.Cardinality2,
				Label = rel.Label,
				Identifying = rel.Identifying,
				Points = points,
			});
		}

		return new PositionedErDiagram
		{
			Width = bb.Width + (Padding * 2),
			Height = bb.Height + (Padding * 2),
			Entities = positionedEntities,
			Relationships = positionedRels,
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
						points.Add(new Point(ls.End.X + offsetX, ls.End.Y + offsetY));
					else
					{
						for (var t = 1; t <= 4; t++)
						{
							var frac = seg.ParStart + ((seg.ParEnd - seg.ParStart) * t / 4.0);
							var p = seg[frac];
							points.Add(new Point(p.X + offsetX, p.Y + offsetY));
						}
					}
				}
				break;
			default:
				for (var t = 1; t <= 8; t++)
				{
					var frac = curve.ParStart + ((curve.ParEnd - curve.ParStart) * t / 8.0);
					var p = curve[frac];
					points.Add(new Point(p.X + offsetX, p.Y + offsetY));
				}
				break;
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
}
