using Mermaid.Models;
using Mermaid.Text;

namespace Mermaid.Rendering;

internal static class NodeSizing
{
	internal static (double Width, double Height) Estimate(string label, NodeShape shape)
	{
		var metrics = TextMetrics.MeasureMultiline(label.AsSpan(), RenderConstants.FontSizes.NodeLabel, RenderConstants.FontWeights.NodeLabel);

		var width = metrics.Width + RenderConstants.NodePadding.Horizontal * 2;
		var height = metrics.Height + RenderConstants.NodePadding.Vertical * 2;

		switch (shape)
		{
			case NodeShape.Diamond:
			{
				var side = Math.Max(width, height) + RenderConstants.NodePadding.DiamondExtra;
				width = side;
				height = side;
				break;
			}
			case NodeShape.Circle or NodeShape.DoubleCircle:
			{
				var diameter = Math.Ceiling(Math.Sqrt(width * width + height * height)) + 8;
				width = shape == NodeShape.DoubleCircle ? diameter + 12 : diameter;
				height = width;
				break;
			}
			case NodeShape.Hexagon or NodeShape.Trapezoid or NodeShape.TrapezoidAlt:
				width += RenderConstants.NodePadding.Horizontal;
				break;
			case NodeShape.Asymmetric:
				width += 12;
				break;
			case NodeShape.Cylinder:
				height += 14;
				break;
			case NodeShape.StateStart or NodeShape.StateEnd:
				return (28, 28);
		}

		width = Math.Max(width, 60);
		height = Math.Max(height, 36);

		return (width, height);
	}
}
