using Mermaider.Models;
using Mermaider.Text;

namespace Mermaider.Rendering;

internal static class NodeSizing
{
	private const double MaxContentWidth = 220;

	internal static string WrapLabel(string label)
	{
		if (!label.Contains('\n') &&
			TextMetrics.MeasureTextWidth(label, RenderConstants.FontSizes.NodeLabel, RenderConstants.FontWeights.NodeLabel) <= MaxContentWidth)
			return label;

		var lines = label.Split('\n');
		var result = new List<string>();
		foreach (var line in lines)
		{
			var lineW = TextMetrics.MeasureTextWidth(line, RenderConstants.FontSizes.NodeLabel, RenderConstants.FontWeights.NodeLabel);
			if (lineW <= MaxContentWidth)
			{
				result.Add(line);
				continue;
			}

			var words = line.Split(' ');
			var current = "";
			foreach (var word in words)
			{
				var candidate = current.Length == 0 ? word : current + " " + word;
				var candidateW = TextMetrics.MeasureTextWidth(candidate, RenderConstants.FontSizes.NodeLabel, RenderConstants.FontWeights.NodeLabel);
				if (candidateW > MaxContentWidth && current.Length > 0)
				{
					result.Add(current);
					current = word;
				}
				else
				{
					current = candidate;
				}
			}
			if (current.Length > 0)
				result.Add(current);
		}
		return string.Join('\n', result);
	}

	internal static (double Width, double Height) Estimate(string label, NodeShape shape)
	{
		label = WrapLabel(label);
		var metrics = TextMetrics.MeasureMultiline(label.AsSpan(), RenderConstants.FontSizes.NodeLabel, RenderConstants.FontWeights.NodeLabel);

		var width = metrics.Width + (RenderConstants.NodePadding.Horizontal * 2);
		var height = metrics.Height + (RenderConstants.NodePadding.Vertical * 2);

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
					var diameter = Math.Ceiling(Math.Sqrt((width * width) + (height * height))) + 8;
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
				return (24, 24);
			case NodeShape.ForkJoin:
				return (120, 8);
		}

		width = Math.Max(width, 64);
		height = Math.Max(height, 40);

		return (width, height);
	}
}
