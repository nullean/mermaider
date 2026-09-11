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

		// A node's stored (X, Y) is its canonical top-left corner. RL and BT negate one
		// axis, which turns "top-left" into a different corner visually: the corner with
		// the *smaller* canonical coordinate ends up with the *larger* visual one once the
		// node's own extent is added back in. Transforming just the stored corner silently
		// keeps the wrong corner as the emitted (X, Y), offsetting every real node — and,
		// via Normalize's shared min-based offset below, every edge point too — by the
		// node's own width or height. TransformBox derives the correct min corner (and,
		// for LR/RL, the swapped width/height) directly per direction, so TD/LR still
		// reduce to a plain reassignment with no extra floating-point error.
		for (var i = 0; i < graph.NodeCount; i++)
		{
			var isReal = i < graph.RealNodeCount;
			var w = isReal ? graph.NodeWidths[i] : 0;
			var h = isReal ? graph.NodeHeights[i] : 0;

			var (x, y, nw, nh) = TransformBox(graph.X[i], graph.Y[i], w, h, direction);
			graph.X[i] = x;
			graph.Y[i] = y;

			if (isReal)
			{
				graph.NodeWidths[i] = nw;
				graph.NodeHeights[i] = nh;
			}
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
	/// Transform a canonical top-left corner (x, y, w, h) to the visual top-left corner
	/// for the given direction. On the axis a direction negates, the far corner
	/// (coordinate + extent) becomes the new min, so that axis's output is
	/// -(coordinate + extent) instead of a plain reassignment.
	/// </summary>
	private static (double X, double Y, double W, double H) TransformBox(double x, double y, double w, double h, Direction direction) =>
		direction switch
		{
			Direction.LR => (y, x, h, w),
			Direction.RL => (-(y + h), x, h, w),
			Direction.BT => (x, -(y + h), w, h),
			_ => (x, y, w, h),
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
			if (graph.X[i] < minX)
				minX = graph.X[i];
			if (graph.Y[i] < minY)
				minY = graph.Y[i];
		}

		foreach (var route in routes)
		{
			foreach (var p in route.Points)
			{
				if (p.X < minX)
					minX = p.X;
				if (p.Y < minY)
					minY = p.Y;
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
