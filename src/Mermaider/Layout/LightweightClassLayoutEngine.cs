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
	// Box sizes come from EntityBoxSizing: the shared entity geometry (class / ER / requirement) every layout provider uses.
	private const double NodeSpacing = 20;
	private const double LayerSpacing = 60;

	/// <summary>Room above a namespace's members for the shared container header (28px strip / tab / chip).</summary>
	private const double NamespaceHeader = DesignSystem.StripHeight;

	private const double NamespaceLayerSpacing = 76;

	private const double LollipopSize = 20;

	// circle (diameter 16) + gap + one line of label
	private const double LollipopHeight = 16 + 6 + 18;

	private const string LollipopSuffix = "__lollipop";

	internal static PositionedClassDiagram Layout(ClassDiagram diagram)
	{
		if (diagram.Classes.Count == 0)
			return new PositionedClassDiagram { Width = 0, Height = 0, Classes = [], Relationships = [] };

		diagram = SplitDeclaredLollipopTargets(diagram);

		// A class is a lollipop target if it only appears as the To side of Lollipop relationships
		// and never as the From side of any relationship or the To side of a non-lollipop relationship.
		var lollipopTargets = new HashSet<string>(
			diagram.Relationships
				.Where(r => r.Type == ClassRelationType.Lollipop)
				.Select(r => r.To));
		foreach (var rel in diagram.Relationships)
		{
			if (rel.Type != ClassRelationType.Lollipop)
				_ = lollipopTargets.Remove(rel.To);
			_ = lollipopTargets.Remove(rel.From);
		}

		var classSizes = new Dictionary<string, (double Width, double Height, double HeaderHeight, double AttrHeight, double MethodHeight)>();

		foreach (var cls in diagram.Classes)
		{
			if (lollipopTargets.Contains(cls.Id))
			{
				// the lollipop is a circle with its name underneath: the node is as wide as that name so neighbours never overlap it
				var labelW = TextMetrics.MeasureTextWidth(cls.Label, RenderConstants.FontSizes.NodeLabel, RenderConstants.FontWeights.NodeLabel);
				var lollipopWidth = Math.Max(LollipopSize, labelW + 8);
				classSizes[cls.Id] = (lollipopWidth, LollipopHeight, LollipopHeight, 0, 0);
				continue;
			}

			classSizes[cls.Id] = EntityBoxSizing.Class(cls);
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

		var layoutDir = diagram.Direction switch
		{
			Direction.LR => LayoutDirection.LR,
			Direction.RL => LayoutDirection.RL,
			Direction.BT => LayoutDirection.BT,
			_ => LayoutDirection.TD,
		};
		// Namespaces are flat (no nesting) in Mermaid's class-diagram grammar, so each becomes a
		// single top-level LayoutSubgraph with no children. Feeding this into the Sugiyama
		// engine (rather than leaving Subgraphs empty, as before) lets NestingGraphRanker keep
		// each namespace's members rank-compact during layout itself — previously, namespace
		// boxes were purely a post-hoc bounding-box wrap around wherever Sugiyama happened to
		// place the member nodes, with nothing preventing a member of one namespace from
		// sharing a rank with (and rendering inside the box of) a sibling namespace's member.
		var namespaceSubgraphs = diagram.Namespaces.Count == 0
			? []
			: diagram.Namespaces
				.Select(ns => new LayoutSubgraph(ns.Name, ns.Name, ns.ClassIds, []))
				.ToList();
		var layoutGraph = new LayoutGraph(layoutDir, layoutNodes, layoutEdges, namespaceSubgraphs);
		var result = HierarchicalLayout.Compute(layoutGraph, new LayoutOptions
		{
			Padding = Padding,
			NodeSpacing = NodeSpacing,
			// namespace boxes wrap their members after layout: leave room for two borders and a header strip between layers
			LayerSpacing = diagram.Namespaces.Count > 0 ? NamespaceLayerSpacing : LayerSpacing,
			NaturalBackEdgeRouting = true,
			ForceBottomExitFanOut = true,
		});

		return ExtractPositioned(result, diagram, classSizes, lollipopTargets);
	}

	/// <summary>
	/// mermaid.js draws the lollipop as its own circle and keeps a declared class (one with a modifier or members) as a separate box. Collapsing
	/// such a class into the circle would silently drop its declared members, so it gets a separate lollipop node instead; a class that only
	/// appears as a lollipop target stays a plain circle.
	/// </summary>
	private static ClassDiagram SplitDeclaredLollipopTargets(ClassDiagram diagram)
	{
		var declared = diagram.Classes
			.Where(c => c.Annotation is { Length: > 0 } || c.Attributes.Count > 0 || c.Methods.Count > 0)
			.Select(c => c.Id)
			.ToHashSet();
		var split = diagram.Relationships
			.Where(r => r.Type == ClassRelationType.Lollipop && declared.Contains(r.To))
			.Select(r => r.To)
			.Distinct()
			.ToList();
		if (split.Count == 0)
			return diagram;

		var classes = diagram.Classes.ToList();
		foreach (var id in split)
		{
			var original = diagram.Classes.First(c => c.Id == id);
			classes.Add(new ClassNode { Id = id + LollipopSuffix, Label = original.Label, Attributes = [], Methods = [] });
		}

		var relationships = diagram.Relationships
			.Select(r => r.Type == ClassRelationType.Lollipop && split.Contains(r.To) ? r with { To = r.To + LollipopSuffix } : r)
			.ToList();
		return diagram with { Classes = classes, Relationships = relationships };
	}

	private static PositionedClassDiagram ExtractPositioned(
		LayoutResult result,
		ClassDiagram diagram,
		Dictionary<string, (double Width, double Height, double HeaderHeight, double AttrHeight, double MethodHeight)> classSizes,
		HashSet<string> lollipopTargets)
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
				IsLollipopTarget = lollipopTargets.Contains(cls.Id),
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
			// note body text (s tier) with 16px side padding and room for the rail / ticks / sticker quote
			var metrics = TextMetrics.MeasureMultiline(note.Text.AsSpan(), DesignSystem.Px(TypeRole.Body), 400);
			var noteW = Math.Max(120.0, metrics.Width + 36);
			var noteH = Math.Max(DesignSystem.RowHeight + 8, metrics.Height + 20);

			if (note.TargetClassId != null && nodeLookup.TryGetValue(note.TargetClassId, out var target))
			{
				var noteX = target.X + target.Width + 24;
				var noteY = target.Y;
				notes.Add(new PositionedGraphNote
				{
					Text = note.Text,
					X = noteX,
					Y = noteY,
					Width = noteW,
					Height = noteH,
					LineFrom = new Point(noteX, noteY + (noteH / 2)),
					LineTo = new Point(target.X + target.Width, target.Y + (target.Height / 2)),
				});
				maxX = Math.Max(maxX, noteX + noteW + 10);
				maxY = Math.Max(maxY, noteY + noteH + 10);
			}
			else
			{
				notes.Add(new PositionedGraphNote { Text = note.Text, X = Padding, Y = maxY + 10, Width = noteW, Height = noteH });
				maxY += noteH + 20;
			}
		}

		const double nsPad = 12;
		var positionedNs = new List<PositionedClassNamespace>(diagram.Namespaces.Count);
		foreach (var ns in diagram.Namespaces)
		{
			var minX = double.MaxValue;
			var minY = double.MaxValue;
			var maxNsX = double.MinValue;
			var maxNsY = double.MinValue;
			foreach (var clsId in ns.ClassIds)
			{
				var positioned = positionedClasses.FirstOrDefault(c => c.Id == clsId);
				if (positioned is null)
					continue;
				minX = Math.Min(minX, positioned.X);
				minY = Math.Min(minY, positioned.Y);
				maxNsX = Math.Max(maxNsX, positioned.X + positioned.Width);
				maxNsY = Math.Max(maxNsY, positioned.Y + positioned.Height);
			}
			if (minX == double.MaxValue)
				continue;
			positionedNs.Add(new PositionedClassNamespace
			{
				Name = ns.Name,
				X = minX - nsPad,
				Y = minY - nsPad - NamespaceHeader,
				Width = maxNsX - minX + (nsPad * 2),
				Height = maxNsY - minY + (nsPad * 2) + NamespaceHeader,
			});
		}

		// Equalize namespace box widths so vertically stacked namespaces span the same X range.
		if (positionedNs.Count > 1)
		{
			var unifiedX = positionedNs.Min(n => n.X);
			var unifiedRight = positionedNs.Max(n => n.X + n.Width);
			var unifiedWidth = unifiedRight - unifiedX;
			for (var i = 0; i < positionedNs.Count; i++)
			{
				var n = positionedNs[i];
				positionedNs[i] = n with { X = unifiedX, Width = unifiedWidth };
			}
		}

		return new PositionedClassDiagram
		{
			Width = maxX,
			Height = maxY,
			Classes = positionedClasses,
			Relationships = positionedRels,
			Notes = notes,
			Namespaces = positionedNs,
		};
	}
}
