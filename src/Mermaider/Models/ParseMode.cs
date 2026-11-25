namespace Mermaider.Models;

/// <summary>How the parser treats input that Mermaid itself would refuse.</summary>
public enum ParseMode
{
	/// <summary>
	/// Default. Malformed statements (an unclosed node bracket, an arrow with no target, a block with no
	/// <c>end</c>) throw a <see cref="MermaidParseException"/> naming the line and what was expected,
	/// so a caller can use Mermaider to check that a diagram is valid.
	/// </summary>
	Strict = 0,

	/// <summary>
	/// Best effort: statements the parser cannot make sense of are skipped and the rest is rendered.
	/// Use it for hand-written or LLM-generated text where a partial diagram beats none.
	/// </summary>
	Lenient = 1,
}
