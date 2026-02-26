namespace Sugiyama.Internal;

/// <summary>
/// Phase 6: Transform coordinates from the canonical TD (top-down) layout
/// to the requested direction (LR, RL, BT). A single pass that swaps/negates
/// X/Y values — no matrix allocations.
/// </summary>
internal static class DirectionTransform
{
	internal enum Direction { TD, LR, BT, RL }

	internal static void Run(GraphBuffer graph, List<EdgeRouter.RoutedEdge> routes, Direction direction)
	{
		if (direction == Direction.TD)
			return;

		for (var i = 0; i < graph.NodeCount; i++)
		{
			var (x, y) = Transform(graph.X[i], graph.Y[i], direction);
			graph.X[i] = x;
			graph.Y[i] = y;
		}

		// For LR/RL, swap node width/height for real nodes
		if (direction is Direction.LR or Direction.RL)
		{
			for (var i = 0; i < graph.RealNodeCount; i++)
				(graph.NodeWidths[i], graph.NodeHeights[i]) = (graph.NodeHeights[i], graph.NodeWidths[i]);
		}

		foreach (var route in routes)
		{
			for (var i = 0; i < route.Points.Count; i++)
			{
				var (x, y) = Transform(route.Points[i].X, route.Points[i].Y, direction);
				route.Points[i] = new LayoutPoint(x, y);
			}

			if (route.LabelPosition is { } lp)
			{
				var (lx, ly) = Transform(lp.X, lp.Y, direction);
				route.SetLabelPosition(new LayoutPoint(lx, ly));
			}
		}
	}

	private static (double X, double Y) Transform(double x, double y, Direction direction) =>
		direction switch
		{
			Direction.LR => (y, x),
			Direction.RL => (-y, x),
			Direction.BT => (x, -y),
			_ => (x, y),
		};

	/// <summary>
	/// Normalize coordinates so all values are non-negative (shift to origin).
	/// Called after direction transform and before extracting the final result.
	/// </summary>
	internal static (double OffsetX, double OffsetY) Normalize(
		GraphBuffer graph, List<EdgeRouter.RoutedEdge> routes, double padding)
	{
		var minX = double.MaxValue;
		var minY = double.MaxValue;

		for (var i = 0; i < graph.NodeCount; i++)
		{
			if (graph.X[i] < minX) minX = graph.X[i];
			if (graph.Y[i] < minY) minY = graph.Y[i];
		}

		foreach (var route in routes)
		{
			foreach (var p in route.Points)
			{
				if (p.X < minX) minX = p.X;
				if (p.Y < minY) minY = p.Y;
			}
		}

		var offsetX = -minX + padding;
		var offsetY = -minY + padding;

		for (var i = 0; i < graph.NodeCount; i++)
		{
			graph.X[i] += offsetX;
			graph.Y[i] += offsetY;
		}

		foreach (var route in routes)
		{
			for (var i = 0; i < route.Points.Count; i++)
				route.Points[i] = new LayoutPoint(route.Points[i].X + offsetX, route.Points[i].Y + offsetY);

			if (route.LabelPosition is { } lp)
				route.SetLabelPosition(new LayoutPoint(lp.X + offsetX, lp.Y + offsetY));
		}

		return (offsetX, offsetY);
	}
}
