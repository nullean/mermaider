namespace Mermaider;

/// <summary>
/// Thrown when a diagram exceeds a configured resource limit (input size, element count,
/// recursion depth, output size, or render deadline). Inherits from
/// <see cref="MermaidParseException"/> so existing <c>catch (MermaidParseException)</c>
/// handlers continue to work without modification.
/// </summary>
public sealed class MermaidResourceLimitException : MermaidParseException
{
	/// <summary>The name of the limit that was exceeded (e.g. "MaxInputLength").</summary>
	public string LimitName { get; }

	/// <summary>The observed value that exceeded the limit.</summary>
	public long ObservedValue { get; }

	/// <summary>The configured limit that was exceeded.</summary>
	public long LimitValue { get; }

	public MermaidResourceLimitException(string limitName, long observed, long limit)
		: base(
			$"Resource limit exceeded — {limitName}: {observed:N0} > {limit:N0}. " +
			$"Raise the limit via RenderOptions.Limits or use ResourceLimits.Unlimited for trusted input.")
	{
		LimitName = limitName;
		ObservedValue = observed;
		LimitValue = limit;
	}

	internal MermaidResourceLimitException(string limitName, long observed, long limit, Exception inner)
		: base(
			$"Resource limit exceeded — {limitName}: {observed:N0} > {limit:N0}. " +
			$"Raise the limit via RenderOptions.Limits or use ResourceLimits.Unlimited for trusted input.",
			inner)
	{
		LimitName = limitName;
		ObservedValue = observed;
		LimitValue = limit;
	}
}
