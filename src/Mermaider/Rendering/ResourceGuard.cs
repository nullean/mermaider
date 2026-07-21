using System.Runtime.CompilerServices;
using Mermaider.Models;

namespace Mermaider.Rendering;

/// <summary>
/// Static helpers that enforce <see cref="ResourceLimits"/> at pipeline boundaries.
/// All methods throw <see cref="MermaidResourceLimitException"/> when a limit is exceeded.
/// </summary>
internal static class ResourceGuard
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void CheckInputLength(string text, ResourceLimits limits)
	{
		if (text.Length > limits.MaxInputLength)
			throw new MermaidResourceLimitException(nameof(limits.MaxInputLength), text.Length, limits.MaxInputLength);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void CheckLines(string[] lines, ResourceLimits limits)
	{
		if (lines.Length > limits.MaxLines)
			throw new MermaidResourceLimitException(nameof(limits.MaxLines), lines.Length, limits.MaxLines);
	}

	internal static void CheckLineLength(string[] lines, ResourceLimits limits)
	{
		if (limits.MaxLineLength == int.MaxValue)
			return;

		foreach (var line in lines)
		{
			if (line.Length > limits.MaxLineLength)
				throw new MermaidResourceLimitException(nameof(limits.MaxLineLength), line.Length, limits.MaxLineLength);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void CheckElements(long count, ResourceLimits limits)
	{
		if (count > limits.MaxElements)
			throw new MermaidResourceLimitException(nameof(limits.MaxElements), count, limits.MaxElements);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void CheckOutputLength(System.Text.StringBuilder sb, ResourceLimits limits)
	{
		if (sb.Length > limits.MaxOutputLength)
			throw new MermaidResourceLimitException(nameof(limits.MaxOutputLength), sb.Length, limits.MaxOutputLength);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void CheckRecursionDepth(int depth, ResourceLimits limits)
	{
		if (depth > limits.MaxRecursionDepth)
			throw new MermaidResourceLimitException(nameof(limits.MaxRecursionDepth), depth, limits.MaxRecursionDepth);
	}
}
