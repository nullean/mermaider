namespace Mermaider;

/// <summary>
/// Thrown when Mermaid diagram text cannot be parsed.
/// This includes syntax errors and regex timeout (ReDoS protection).
/// </summary>
public sealed class MermaidParseException : Exception
{
	public MermaidParseException(string message) : base(message) { }

	public MermaidParseException(string message, Exception innerException) : base(message, innerException) { }
}
