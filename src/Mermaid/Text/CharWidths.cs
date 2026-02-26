using System.Buffers;

namespace Mermaid.Text;

/// <summary>
/// Variable-width character measurement for SVG layout.
/// Width ratios are normalized where 1.0 = average lowercase letter.
/// </summary>
internal static class CharWidths
{
	private static readonly SearchValues<char> s_narrow =
		SearchValues.Create("iltfjI1!|.,:;'");

	private static readonly SearchValues<char> s_wide =
		SearchValues.Create("Wwm@%");

	private static readonly SearchValues<char> s_semiNarrow =
		SearchValues.Create("()[]{}\\/-\"`");

	internal static double GetCharWidth(char c)
	{
		var code = (int)c;

		if (IsCombiningMark(code))
			return 0;

		if (IsFullwidth(code))
			return 2.0;

		if (c == ' ')
			return 0.3;

		if (c is 'W' or 'M')
			return 1.5;

		if (s_wide.Contains(c))
			return 1.2;

		if (s_narrow.Contains(c))
			return 0.4;

		if (s_semiNarrow.Contains(c))
			return 0.5;

		if (c == 'r')
			return 0.8;

		if (code >= 65 && code <= 90)
			return 1.2;

		if (code >= 48 && code <= 57)
			return 1.0;

		return 1.0;
	}

	private static bool IsCombiningMark(int code) =>
		(code >= 0x0300 && code <= 0x036F) ||
		(code >= 0x1AB0 && code <= 0x1AFF) ||
		(code >= 0x1DC0 && code <= 0x1DFF) ||
		(code >= 0x20D0 && code <= 0x20FF) ||
		(code >= 0xFE20 && code <= 0xFE2F);

	private static bool IsFullwidth(int code) =>
		(code >= 0x1100 && code <= 0x115F) ||
		(code >= 0x2E80 && code <= 0x2EFF) ||
		(code >= 0x2F00 && code <= 0x2FDF) ||
		(code >= 0x3000 && code <= 0x303F) ||
		(code >= 0x3040 && code <= 0x309F) ||
		(code >= 0x30A0 && code <= 0x30FF) ||
		(code >= 0x3100 && code <= 0x312F) ||
		(code >= 0x3130 && code <= 0x318F) ||
		(code >= 0x3190 && code <= 0x31FF) ||
		(code >= 0x3200 && code <= 0x33FF) ||
		(code >= 0x3400 && code <= 0x4DBF) ||
		(code >= 0x4E00 && code <= 0x9FFF) ||
		(code >= 0xAC00 && code <= 0xD7AF) ||
		(code >= 0xF900 && code <= 0xFAFF) ||
		(code >= 0xFF00 && code <= 0xFF60) ||
		(code >= 0xFFE0 && code <= 0xFFE6) ||
		code >= 0x20000;
}
