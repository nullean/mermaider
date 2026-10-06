using Mermaider.Models;
using Mermaider.Rendering;

namespace Mermaider.Layout;

/// <summary>
/// Box sizes for class and ER nodes on the shared entity geometry (36px header, +16 for a «stereotype», 28px rows,
/// the <see cref="EntityGrid"/> columns). Every layout provider sizes boxes here so the renderers, which read
/// <c>HeaderHeight</c> / <c>AttrHeight</c> / <c>MethodHeight</c> / <c>RowHeight</c> from the positioned model,
/// always draw into a box that fits.
/// </summary>
internal static class EntityBoxSizing
{
	internal const double ClassHeaderBaseHeight = 36;
	internal const double ClassAnnotationHeight = 16;
	internal const double ClassMemberRowHeight = DesignSystem.RowHeight;
	internal const double ClassSectionPadY = 0;
	internal const double ClassEmptySectionHeight = 8;
	internal const double ClassMinWidth = 100;

	internal const double ErHeaderHeight = 36;
	internal const double ErRowHeight = DesignSystem.RowHeight;
	internal const double ErMinWidth = 120;

	/// <summary>Size of a class box and its header / attribute / method sections.</summary>
	internal static (double Width, double Height, double HeaderHeight, double AttrHeight, double MethodHeight) Class(ClassNode cls)
	{
		var headerHeight = cls.Annotation != null
			? ClassHeaderBaseHeight + ClassAnnotationHeight
			: ClassHeaderBaseHeight;

		// a class without members is just its header; otherwise an empty compartment keeps a sliver so both read
		var hasMembers = cls.Attributes.Count > 0 || cls.Methods.Count > 0;
		var attrHeight = cls.Attributes.Count > 0
			? (cls.Attributes.Count * ClassMemberRowHeight) + ClassSectionPadY
			: hasMembers ? ClassEmptySectionHeight : 0;

		var methodHeight = cls.Methods.Count > 0
			? (cls.Methods.Count * ClassMemberRowHeight) + ClassSectionPadY
			: hasMembers ? ClassEmptySectionHeight : 0;

		var headerW = EntityGrid.HeadingWidth(cls.Label);
		if (cls.Annotation is { Length: > 0 } annotation)
			headerW = Math.Max(headerW, EntityGrid.StereotypeWidth("«" + annotation + "»"));
		var membersW = ClassMemberColumns.BoxWidth(cls.Attributes.Concat(cls.Methods));
		var width = Math.Max(ClassMinWidth, Math.Max(headerW, membersW));
		var height = headerHeight + attrHeight + methodHeight;

		return (width, height, headerHeight, attrHeight, methodHeight);
	}

	/// <summary>Size of an ER entity box; <paramref name="degree"/> is its number of non-self relationships.</summary>
	internal static (double Width, double Height) Entity(ErEntity entity, int degree)
	{
		var (typeW, nameW, badgeW) = ErSvgRenderer.MeasureColumns(entity.Attributes);
		var gridW = entity.Attributes.Count > 0 ? EntityGrid.BoxWidth(typeW, nameW, signs: false, badgeW) : 0;
		// Edge anchors are spread evenly along a node side; keep them at least ~18px apart so crow's-foot markers don't overlap.
		var anchorWidth = degree > 4 ? (degree * 18) + 24 : 0;
		var width = Math.Max(Math.Max(ErMinWidth, anchorWidth), Math.Max(EntityGrid.HeadingWidth(entity.Label), gridW));
		var height = entity.Attributes.Count == 0
			? ErHeaderHeight + 8
			: ErHeaderHeight + (entity.Attributes.Count * ErRowHeight);
		return (width, height);
	}

	/// <summary>Number of relationships touching <paramref name="entityId"/>, self-loops excluded.</summary>
	internal static int EntityDegree(ErDiagram diagram, string entityId) =>
		diagram.Relationships.Count(r => r.Entity1 != r.Entity2 && (r.Entity1 == entityId || r.Entity2 == entityId));
}
