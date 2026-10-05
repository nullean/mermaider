namespace Mermaider;

/// <summary>
/// Thrown when an unexpected internal error occurs while laying out or drawing a diagram that parsed successfully.
/// The original exception is preserved as <see cref="Exception.InnerException"/>.
/// </summary>
/// <param name="diagramType">The type of diagram that was being rendered.</param>
/// <param name="innerException">The underlying failure.</param>
public sealed class MermaidRenderException(Models.DiagramType diagramType, Exception innerException)
	: Exception($"Failed to render {diagramType} diagram: {innerException.Message}", innerException)
{
	/// <summary>The type of diagram that was being rendered.</summary>
	public Models.DiagramType DiagramType { get; } = diagramType;
}
