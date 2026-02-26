namespace Mermaid.Rendering;

internal static class RenderConstants
{
	internal static class FontSizes
	{
		internal const int NodeLabel = 13;
		internal const int EdgeLabel = 11;
		internal const int GroupHeader = 12;
	}

	internal static class FontWeights
	{
		internal const int NodeLabel = 500;
		internal const int EdgeLabel = 400;
		internal const int GroupHeader = 600;
	}

	internal static class StrokeWidths
	{
		internal const double OuterBox = 1;
		internal const double InnerBox = 0.75;
		internal const double Connector = 1;
	}

	internal static class ArrowHead
	{
		internal const int Width = 8;
		internal const int Height = 5;
	}

	internal static class NodePadding
	{
		internal const int Horizontal = 20;
		internal const int Vertical = 10;
		internal const int DiamondExtra = 24;
	}

	internal const string TextBaselineShift = "0.35em";
	internal const int GroupHeaderContentPad = 12;
	internal const string MonoFont = "'JetBrains Mono'";
	internal const string MonoFontStack = "'JetBrains Mono', 'SF Mono', 'Fira Code', ui-monospace, monospace";
}
