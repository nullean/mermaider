using System;
using System.Collections.Generic;
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

namespace Mermaider.Layout.Msagl;

internal static class MsaglClassLayout
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

		var geometryGraph = new GeometryGraph();
		var msaglNodes = new Dictionary<string, MsaglNode>();

		foreach (var cls in diagram.Classes)
		{
			var (w, h, _, _, _) = classSizes[cls.Id];
			var msaglNode = new MsaglNode(CurveFactory.CreateRectangle(w, h, new MsaglPoint(0, 0)), cls.Id);
			msaglNodes[cls.Id] = msaglNode;
			geometryGraph.Nodes.Add(msaglNode);
		}

		var edgeMap = new List<(MsaglEdge MsaglEdge, ClassRelationship Rel)>();
		foreach (var rel in diagram.Relationships)
		{
			if (!msaglNodes.TryGetValue(rel.From, out var sourceNode) ||
				!msaglNodes.TryGetValue(rel.To, out var targetNode))
				continue;

			var msaglEdge = new MsaglEdge(sourceNode, targetNode);
			if (rel.Label is { Length: > 0 })
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
			EdgeRoutingSettings = { EdgeRoutingMode = EdgeRoutingMode.Rectilinear, Padding = 4 }
		};

		var layout = new LayeredLayout(geometryGraph, settings);
		layout.Run();

		return ExtractPositioned(geometryGraph, diagram, classSizes, msaglNodes, edgeMap);
	}

	private static PositionedClassDiagram ExtractPositioned(
		GeometryGraph geometryGraph,
		ClassDiagram diagram,
		Dictionary<string, (double Width, double Height, double HeaderHeight, double AttrHeight, double MethodHeight)> classSizes,
		Dictionary<string, MsaglNode> msaglNodes,
		List<(MsaglEdge MsaglEdge, ClassRelationship Rel)> edgeMap)
	{
		var bb = geometryGraph.BoundingBox;
		var offsetX = -bb.Left + Padding;
		var offsetY = -bb.Bottom + Padding;

		var classLookup = diagram.Classes.ToDictionary(c => c.Id);
		var positionedClasses = new List<PositionedClassNode>(diagram.Classes.Count);

		foreach (var (id, msaglNode) in msaglNodes)
		{
			if (!classLookup.TryGetValue(id, out var cls))
				continue;
			var size = classSizes[id];
			var center = msaglNode.Center;
			var w = msaglNode.BoundingBox.Width;
			var h = msaglNode.BoundingBox.Height;

			positionedClasses.Add(new PositionedClassNode
			{
				Id = cls.Id,
				Label = cls.Label,
				Annotation = cls.Annotation,
				Attributes = cls.Attributes,
				Methods = cls.Methods,
				X = center.X - (w / 2) + offsetX,
				Y = center.Y - (h / 2) + offsetY,
				Width = w,
				Height = h,
				HeaderHeight = size.HeaderHeight,
				AttrHeight = size.AttrHeight,
				MethodHeight = size.MethodHeight,
			});
		}

		var positionedRels = new List<PositionedClassRelationship>(edgeMap.Count);
		foreach (var (msaglEdge, rel) in edgeMap)
		{
			var points = ExtractEdgePoints(msaglEdge, offsetX, offsetY);
			Point? labelPos = null;
			if (msaglEdge.Label != null)
			{
				var lc = msaglEdge.Label.Center;
				labelPos = new Point(lc.X + offsetX, lc.Y + offsetY);
			}

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

		return new PositionedClassDiagram
		{
			Width = bb.Width + (Padding * 2),
			Height = bb.Height + (Padding * 2),
			Classes = positionedClasses,
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

	private static double MaxMemberWidth(IReadOnlyList<ClassMember> members)
	{
		var maxW = 0.0;
		foreach (var m in members)
		{
			var text = MemberToString(m);
			var w = TextMetrics.EstimateMonoTextWidth(text, MemberFontSize);
			if (w > maxW)
				maxW = w;
		}
		return maxW;
	}

	internal static string MemberToString(ClassMember m)
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
		return $"{vis}{name}{type}";
	}
}
