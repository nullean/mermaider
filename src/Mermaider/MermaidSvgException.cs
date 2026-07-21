namespace Mermaider;

/// <summary>
/// Thrown when <see cref="Models.SanitizeMode.Block"/> rejects SVG that violates
/// the SVG allowlist or is not well-formed XML.
/// </summary>
public sealed class MermaidSvgException : Exception
{
	/// <summary>Immutable snapshot of every violation reported by the sanitizer.</summary>
	public IReadOnlyList<SvgViolation> Violations { get; }

	/// <summary>Creates an exception for a rejected SVG sanitization result.</summary>
	public MermaidSvgException(IReadOnlyList<SvgViolation> violations)
		: base(CreateMessage(violations))
	{
		ArgumentNullException.ThrowIfNull(violations);
		if (violations.Count == 0)
			throw new ArgumentException("At least one SVG violation is required.", nameof(violations));

		Violations = Array.AsReadOnly(violations.ToArray());
	}

	private static string CreateMessage(IReadOnlyList<SvgViolation> violations)
	{
		ArgumentNullException.ThrowIfNull(violations);
		if (violations.Count == 0)
			return "SVG sanitization was rejected.";

		var first = violations[0];
		return first.Kind == "document" && first.Name == "malformed-xml"
			? "SVG sanitization failed: input is not well-formed SVG/XML."
			: $"SVG sanitization failed: disallowed {first}.";
	}
}
