using Mermaider.Models;
using Mermaider.Rendering;
using Mermaider.Text;
using Sugiyama;

namespace Mermaider.Layout;

/// <summary>
/// Lightweight ER diagram layout using the Sugiyama engine.
/// Replaces the MSAGL-based <c>ErLayoutEngine</c>.
/// </summary>
internal static class LightweightErLayoutEngine
{
	private const double Padding = 40;
	private const double BoxPadX = 14;
	private const double HeaderHeight = 34;
	private const double RowHeight = 22;
	private const double MinWidth = 140;
	private static readonly double AttrFontSize = RenderConstants.FontSizes.Member;
	private const double NodeSpacing = 90;
	private const double LayerSpacing = 90;

	internal static PositionedErDiagram Layout(ErDiagram diagram)
	{
		if (diagram.Entities.Count == 0)
			return new PositionedErDiagram { Width = 0, Height = 0, Entities = [], Relationships = [] };

		var entitySizes = new Dictionary<string, (double Width, double Height)>();
		foreach (var entity in diagram.Entities)
		{
			var headerTextW = TextMetrics.MeasureTextWidth(
				entity.Label, RenderConstants.FontSizes.NodeLabel, RenderConstants.FontWeights.NodeLabel);
			var maxAttrW = 0.0;
			foreach (var attr in entity.Attributes)
			{
				var attrText = $"{attr.Type}  {attr.Name}{(attr.Keys.Count > 0 ? "  " + string.Join(",", attr.Keys) : "")}";
				var w = TextMetrics.EstimateMonoTextWidth(attrText, AttrFontSize);
				if (w > maxAttrW) maxAttrW = w;
			}
			var width = Math.Max(MinWidth, Math.Max(headerTextW + BoxPadX * 2, maxAttrW + BoxPadX * 2));
			var height = HeaderHeight + Math.Max(entity.Attributes.Count, 1) * RowHeight;
			entitySizes[entity.Id] = (width, height);
		}

		var layoutNodes = new List<LayoutNode>(diagram.Entities.Count);
		foreach (var entity in diagram.Entities)
		{
			var (w, h) = entitySizes[entity.Id];
			layoutNodes.Add(new LayoutNode(entity.Id, w, h));
		}

		var layoutEdges = new List<LayoutEdge>(diagram.Relationships.Count);
		foreach (var rel in diagram.Relationships)
		{
			double labelW = 0, labelH = 0;
			if (rel.Label.Length > 0)
			{
				var metrics = TextMetrics.MeasureMultiline(rel.Label.AsSpan(), RenderConstants.FontSizes.EdgeLabel, RenderConstants.FontWeights.EdgeLabel);
				labelW = metrics.Width + 8;
				labelH = metrics.Height + 6;
			}
			layoutEdges.Add(new LayoutEdge(rel.Entity1, rel.Entity2, labelW, labelH));
		}

		var maxLabelW = layoutEdges.Count > 0
			? layoutEdges.Max(e => e.LabelWidth)
			: 0;
		var effectiveLayerSpacing = Math.Max(LayerSpacing, maxLabelW + 40);

		var layoutGraph = new LayoutGraph(LayoutDirection.LR, layoutNodes, layoutEdges, []);
		var result = SugiyamaLayout.Compute(layoutGraph, new LayoutOptions
		{
			Padding = Padding,
			NodeSpacing = NodeSpacing,
			LayerSpacing = effectiveLayerSpacing,
		});

		return ExtractPositioned(result, diagram, entitySizes);
	}

	private static PositionedErDiagram ExtractPositioned(
		LayoutResult result, ErDiagram diagram,
		Dictionary<string, (double Width, double Height)> entitySizes)
	{
		var entityLookup = diagram.Entities.ToDictionary(e => e.Id);
		var nodeLookup = result.Nodes.ToDictionary(n => n.Id);
		var positionedEntities = new List<PositionedErEntity>(diagram.Entities.Count);

		foreach (var entity in diagram.Entities)
		{
			if (!nodeLookup.TryGetValue(entity.Id, out var n)) continue;
			positionedEntities.Add(new PositionedErEntity
			{
				Id = entity.Id,
				Label = entity.Label,
				Attributes = entity.Attributes,
				X = n.X,
				Y = n.Y,
				Width = n.Width,
				Height = n.Height,
				HeaderHeight = HeaderHeight,
				RowHeight = RowHeight,
			});
		}

		var positionedRels = new List<PositionedErRelationship>(diagram.Relationships.Count);
		for (var i = 0; i < diagram.Relationships.Count; i++)
		{
			var rel = diagram.Relationships[i];
			var edge = result.Edges.FirstOrDefault(e => e.OriginalIndex == i);
			if (edge is null) continue;

			var points = edge.Points.Select(p => new Point(p.X, p.Y)).ToList();
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
			Width = result.Width,
			Height = result.Height,
			Entities = positionedEntities,
			Relationships = positionedRels,
		};
	}
}
