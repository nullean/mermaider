namespace Mermaider.Rendering;

internal static class RenderConstants
{
	internal static class FontSizes
	{
		internal const int NodeLabel = 16;
		internal const int EdgeLabel = 14;
		internal const int SeqMessageLabel = 14;
		internal const int GroupHeader = 14;
		internal const int Member = 14;
		internal const int Annotation = 12;
		internal const int KeyBadge = 12;
	}

	internal static class FontWeights
	{
		internal const int NodeLabel = 500;
		internal const int EdgeLabel = 400;
		internal const int GroupHeader = 600;
		internal const int Member = 400;
		internal const int Annotation = 500;
		internal const int KeyBadge = 600;
	}

	internal static class StrokeWidths
	{
		internal const double OuterBox = 1.75;
		internal const double InnerBox = 2;
		internal const double Connector = 2.25;
	}

	internal static class ArrowHead
	{
		internal const int Size = 12;
	}

	internal static class NodePadding
	{
		internal const int Horizontal = 28;
		internal const int Vertical = 16;
		internal const int DiamondExtra = 28;
	}

	internal static class Radii
	{
		internal const int Rectangle = 6;
		internal const int Rounded = 10;
		internal const int Group = 8;
		internal const int EdgeLabel = 10;
	}

	internal const string TextBaselineShift = "0.35em";
	internal const int GroupHeaderContentPad = 12;

	internal const string SansStack = "system-ui, -apple-system, 'Segoe UI', sans-serif";
	internal const string MonoStack = "ui-monospace, 'SF Mono', 'Cascadia Code', monospace";

	internal static class FsVar
	{
		internal const string Xs = "var(--fs-xs)";
		internal const string S = "var(--fs-s)";
		internal const string M = "var(--fs-m)";
		internal const string L = "var(--fs-l)";
	}

	internal static class TextAttrs
	{
		internal static readonly string NodeLabelCenterFill =
			$"text-anchor=\"middle\" font-size=\"{FsVar.M}\" font-weight=\"{FontWeights.NodeLabel}\" fill=\"";

		internal static readonly string NodeLabelBoldCenterFill =
			$"text-anchor=\"middle\" font-size=\"{FsVar.M}\" font-weight=\"700\" fill=\"";

		internal static readonly string EdgeLabelCenterFill =
			$"text-anchor=\"middle\" font-size=\"{FsVar.S}\" font-weight=\"{FontWeights.EdgeLabel}\" fill=\"";

		internal static readonly string GroupHeaderFill =
			$"font-size=\"{FsVar.S}\" font-weight=\"{FontWeights.GroupHeader}\" fill=\"";

		internal static readonly string SeqNodeLabelFill =
			$"font-size=\"{FsVar.M}\" text-anchor=\"middle\" font-weight=\"{FontWeights.NodeLabel}\" fill=\"";

		internal static readonly string SeqEdgeLabelCenterFill =
			$"font-size=\"{FsVar.S}\" text-anchor=\"middle\" font-weight=\"{FontWeights.EdgeLabel}\" fill=\"";

		internal static readonly string SeqEdgeLabelStartFill =
			$"font-size=\"{FsVar.S}\" text-anchor=\"start\" font-weight=\"{FontWeights.EdgeLabel}\" fill=\"";

		internal static readonly string SeqMessageLabelCenterFill =
			$"font-size=\"{FsVar.S}\" text-anchor=\"middle\" font-weight=\"{FontWeights.EdgeLabel}\" fill=\"";

		internal static readonly string SeqMessageLabelStartFill =
			$"font-size=\"{FsVar.S}\" text-anchor=\"start\" font-weight=\"{FontWeights.EdgeLabel}\" fill=\"";

		internal static readonly string SeqNoteCenterFill =
			$"font-size=\"{FsVar.S}\" text-anchor=\"middle\" font-weight=\"{FontWeights.EdgeLabel}\" fill=\"";

		internal static readonly string SeqBlockTabFill =
			$"font-size=\"{FsVar.S}\" font-weight=\"{FontWeights.GroupHeader}\" fill=\"";

		internal static readonly string ClassRelLabelFill =
			$"font-size=\"{FsVar.S}\" text-anchor=\"middle\" font-weight=\"{FontWeights.EdgeLabel}\" fill=\"";
	}
}
