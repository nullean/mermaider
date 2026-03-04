using Mermaider.Models;
using Mermaider.Rendering;
using Mermaider.Text;
using Sugiyama;

namespace Mermaider.Layout;

/// <summary>
/// Lightweight class diagram layout using the Sugiyama engine.
/// Replaces the MSAGL-based <c>ClassLayoutEngine</c>.
/// </summary>
internal static class LightweightClassLayoutEngine
{
	private const double Padding = 40;
	private const double BoxPadX = 8;
	private const double HeaderBaseHeight = 32;
	private const double AnnotationHeight = 16;
	private const double MemberRowHeight = 20;
	private const double SectionPadY = 8;
	private const double EmptySectionHeight = 8;
	private const double MinWidth = 120;
	private static readonly double MemberFontSize = RenderConstants.FontSizes.Member;
	private const double NodeSpacing = 40;
	private const double LayerSpacing = 60;

	internal static PositionedClassDiagram Layout(ClassDiagram diagram)
	{
		if (diagram.Classes.Count == 0)
			return new PositionedClassDiagram { Width = 0, Height = 0, Classes = [], Relationships = [] };

		var classSizes = new Dictionary<string, (double Width, double Height, double HeaderHeight, double AttrHeight, double MethodHeight)>();

		foreach (var cls in diagram.Classes)
		{
			var headerHeight = cls.Annotation != null
				? HeaderBaseHeight + AnnotationHeight
				: HeaderBaseHeight;

			var attrHeight = cls.Attributes.Count > 0
				? (cls.Attributes.Count * MemberRowHeight) + SectionPadY
				: EmptySectionHeight;

			var methodHeight = cls.Methods.Count > 0
				? (cls.Methods.Count * MemberRowHeight) + SectionPadY
				: EmptySectionHeight;

			var headerTextW = TextMetrics.MeasureTextWidth(cls.Label, RenderConstants.FontSizes.NodeLabel, RenderConstants.FontWeights.NodeLabel);
			var maxAttrW = MaxMemberWidth(cls.Attributes);
			var maxMethodW = MaxMemberWidth(cls.Methods);
			var width = Math.Max(MinWidth, Math.Max(headerTextW + (BoxPadX * 2), Math.Max(maxAttrW + (BoxPadX * 2), maxMethodW + (BoxPadX * 2))));
			var height = headerHeight + attrHeight + methodHeight;

			classSizes[cls.Id] = (width, height, headerHeight, attrHeight, methodHeight);
		}

		var layoutNodes = new List<LayoutNode>(diagram.Classes.Count);
		foreach (var cls in diagram.Classes)
		{
			var (w, h, _, _, _) = classSizes[cls.Id];
			layoutNodes.Add(new LayoutNode(cls.Id, w, h));
		}

		var layoutEdges = new List<LayoutEdge>(diagram.Relationships.Count);
		foreach (var rel in diagram.Relationships)
		{
			double labelW = 0, labelH = 0;
			if (rel.Label is { Length: > 0 })
			{
				var metrics = TextMetrics.MeasureMultiline(rel.Label.AsSpan(), RenderConstants.FontSizes.EdgeLabel, RenderConstants.FontWeights.EdgeLabel);
				labelW = metrics.Width + 8;
				labelH = metrics.Height + 6;
			}
			layoutEdges.Add(new LayoutEdge(rel.From, rel.To, labelW, labelH));
		}

		var layoutSubgraphs = diagram.Namespaces
			.Select(ns => new LayoutSubgraph(ns.Name, ns.Name, ns.ClassIds, []))
			.ToList();

		var layoutDir = diagram.Direction switch
		{
			Direction.LR => LayoutDirection.LR,
			Direction.RL => LayoutDirection.RL,
			Direction.BT => LayoutDirection.BT,
			_ => LayoutDirection.TD,
		};
		var layoutGraph = new LayoutGraph(layoutDir, layoutNodes, layoutEdges, layoutSubgraphs);
		var result = SugiyamaLayout.Compute(layoutGraph, new LayoutOptions
		{
			Padding = Padding,
			NodeSpacing = NodeSpacing,
			LayerSpacing = LayerSpacing,
		});

		return ExtractPositioned(result, diagram, classSizes);
	}

	private static PositionedClassDiagram ExtractPositioned(
		LayoutResult result,
		ClassDiagram diagram,
		Dictionary<string, (double Width, double Height, double HeaderHeight, double AttrHeight, double MethodHeight)> classSizes)
	{
		var nodeLookup = result.Nodes.ToDictionary(n => n.Id);
		var positionedClasses = new List<PositionedClassNode>(diagram.Classes.Count);

		foreach (var cls in diagram.Classes)
		{
			if (!nodeLookup.TryGetValue(cls.Id, out var n))
				continue;
			var size = classSizes[cls.Id];
			positionedClasses.Add(new PositionedClassNode
			{
				Id = cls.Id,
				Label = cls.Label,
				Annotation = cls.Annotation,
				Attributes = cls.Attributes,
				Methods = cls.Methods,
				X = n.X,
				Y = n.Y,
				Width = n.Width,
				Height = n.Height,
				HeaderHeight = size.HeaderHeight,
				AttrHeight = size.AttrHeight,
				MethodHeight = size.MethodHeight,
				InlineStyle = ResolveNodeStyle(cls.Id, diagram),
			});
		}

		var positionedRels = new List<PositionedClassRelationship>(diagram.Relationships.Count);
		for (var i = 0; i < diagram.Relationships.Count; i++)
		{
			var rel = diagram.Relationships[i];
			var edge = result.Edges.FirstOrDefault(e => e.OriginalIndex == i);
			if (edge is null)
				continue;

			var points = edge.Points.Select(p => new Point(p.X, p.Y)).ToList();
			Point? labelPos = edge.LabelPosition is { } lp ? new Point(lp.X, lp.Y) : null;

			positionedRels.Add(new PositionedClassRelationship
			{
				From = rel.From,
				To = rel.To,
				Type = rel.Type,
				MarkerAt = rel.MarkerAt,
				Label = rel.Label,
				FromCardinality = rel.FromCardinality,
				ToCardinality = rel.ToCardinality,
				Points = points,
				LabelPosition = labelPos,
			});
		}

		var notes = new List<PositionedGraphNote>(diagram.Notes.Count);
		var maxX = result.Width;
		var maxY = result.Height;

		foreach (var note in diagram.Notes)
		{
			var textW = TextMetrics.MeasureTextWidth(
				note.Text, RenderConstants.FontSizes.EdgeLabel, RenderConstants.FontWeights.EdgeLabel) + 20;
			var noteW = Math.Max(120.0, textW);
			var noteH = RenderConstants.FontSizes.EdgeLabel + 16;

			if (note.TargetClassId != null && nodeLookup.TryGetValue(note.TargetClassId, out var target))
			{
				var noteX = target.X + target.Width + 10;
				var noteY = target.Y;
				notes.Add(new PositionedGraphNote { Text = note.Text, X = noteX, Y = noteY, Width = noteW, Height = noteH });
				maxX = Math.Max(maxX, noteX + noteW + 10);
				maxY = Math.Max(maxY, noteY + noteH + 10);
			}
			else
			{
				notes.Add(new PositionedGraphNote { Text = note.Text, X = Padding, Y = maxY + 10, Width = noteW, Height = noteH });
				maxY += noteH + 20;
			}
		}

		var nsGroups = result.Groups.Select(MapGroup).ToList();

		return new PositionedClassDiagram
		{
			Width = maxX,
			Height = maxY,
			Classes = positionedClasses,
			Relationships = positionedRels,
			Notes = notes,
			Namespaces = nsGroups,
		};
	}

	private static PositionedGroup MapGroup(LayoutGroupResult g) =>
		new()
		{
			Id = g.Id,
			Label = g.Label,
			X = g.X,
			Y = g.Y,
			Width = g.Width,
			Height = g.Height,
			Children = g.Children.Select(MapGroup).ToList(),
		};

	private static IReadOnlyDictionary<string, string>? ResolveNodeStyle(string nodeId, ClassDiagram diagram)
	{
		Dictionary<string, string>? result = null;

		if (diagram.ClassAssignments.TryGetValue(nodeId, out var className) &&
			diagram.ClassDefs.TryGetValue(className, out var classDef))
		{
			result = new Dictionary<string, string>(classDef);
		}

		if (diagram.NodeStyles.TryGetValue(nodeId, out var nodeStyle))
		{
			result ??= [];
			foreach (var kvp in nodeStyle)
				result[kvp.Key] = kvp.Value;
		}

		return result;
	}

	private static double MaxMemberWidth(IReadOnlyList<ClassMember> members)
	{
		var maxW = 0.0;
		foreach (var m in members)
		{
			var vis = m.Visibility switch
			{
				ClassVisibility.Public => "+ ",
				ClassVisibility.Private => "- ",
				ClassVisibility.Protected => "# ",
				ClassVisibility.Package => "~ ",
				_ => "",
			};
			var name = m.IsMethod ? $"{m.Name}({m.Params ?? ""})" : m.Name;
			var type = m.Type != null ? $": {m.Type}" : "";
			var text = $"{vis}{name}{type}";
			var w = TextMetrics.EstimateMonoTextWidth(text, MemberFontSize);
			if (w > maxW)
				maxW = w;
		}
		return maxW;
	}
}
