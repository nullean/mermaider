namespace Mermaider.Animate;

/// <summary>One problem in a score, with the source line it was found on (1-based, counted over the whole input).</summary>
public sealed record AnimationDiagnostic(int Line, string Message)
{
	public override string ToString() => $"line {Line}: {Message}";
}

/// <summary>The score could not be compiled. <see cref="Diagnostics"/> lists every problem found.</summary>
public sealed class AnimationException(IReadOnlyList<AnimationDiagnostic> diagnostics) : Exception(
	diagnostics.Count == 1 ? diagnostics[0].ToString() : $"{diagnostics.Count} problems in the animate block:\n" + string.Join("\n", diagnostics)
)
{
	public IReadOnlyList<AnimationDiagnostic> Diagnostics { get; } = diagnostics;
}
