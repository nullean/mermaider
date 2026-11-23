namespace Mermaider.Compat;

/// <summary>
/// <c>Split(..., TrimEntries)</c> without the .NET 5 enum member: the same call on every target, so the shared source
/// has one spelling.
/// </summary>
internal static class SplitExtensions
{
	public static string[] SplitTrimmed(this string value, char[]? separators, bool removeEmpty) =>
#if NET5_0_OR_GREATER
		value.Split(separators, (removeEmpty ? StringSplitOptions.RemoveEmptyEntries : StringSplitOptions.None) | StringSplitOptions.TrimEntries);
#else
		SplitTrimmedCore(value, separators, removeEmpty);

	private static string[] SplitTrimmedCore(string value, char[]? separators, bool removeEmpty)
	{
		var parts = value.Split(separators, StringSplitOptions.None);
		var result = new List<string>(parts.Length);
		foreach (var part in parts)
		{
			var trimmed = part.Trim();
			if (!removeEmpty || trimmed.Length > 0)
				result.Add(trimmed);
		}
		return [.. result];
	}
#endif
}
