using System.Buffers;

namespace Mermaider.Text;

/// <summary>
/// Variable-width character measurement for SVG layout.
/// Width ratios are normalized where 1.0 = average lowercase letter.
/// </summary>
internal static class CharWidths
{
	private static readonly SearchValues<char> NarrowCharacters =
		SearchValues.Create("iltfjI1!|.,:;'");

	private static readonly SearchValues<char> WideCharacters =
		SearchValues.Create("Wwm@%");

	private static readonly SearchValues<char> SemiNarrowCharacters =
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

		if (WideCharacters.Contains(c))
			return 1.2;

		if (NarrowCharacters.Contains(c))
			return 0.4;

		if (SemiNarrowCharacters.Contains(c))
			return 0.5;

		if (c == 'r')
			return 0.8;

		if (code is >= 65 and <= 90)
			return 1.2;

		if (code is >= 48 and <= 57)
			return 1.0;

		return 1.0;
	}

	private static bool IsCombiningMark(int code) =>
		code is (>= 0x0300 and <= 0x036F) or
				(>= 0x1AB0 and <= 0x1AFF) or
				(>= 0x1DC0 and <= 0x1DFF) or
				(>= 0x20D0 and <= 0x20FF) or
				(>= 0xFE20 and <= 0xFE2F);

	private static bool IsFullwidth(int code) =>
		code is (>= 0x1100 and <= 0x115F) or
				(>= 0x2E80 and <= 0x2EFF) or
				(>= 0x2F00 and <= 0x2FDF) or
				(>= 0x3000 and <= 0x303F) or
				(>= 0x3040 and <= 0x309F) or
				(>= 0x30A0 and <= 0x30FF) or
				(>= 0x3100 and <= 0x312F) or
				(>= 0x3130 and <= 0x318F) or
				(>= 0x3190 and <= 0x31FF) or
				(>= 0x3200 and <= 0x33FF) or
				(>= 0x3400 and <= 0x4DBF) or
				(>= 0x4E00 and <= 0x9FFF) or
				(>= 0xAC00 and <= 0xD7AF) or
				(>= 0xF900 and <= 0xFAFF) or
				(>= 0xFF00 and <= 0xFF60) or
				(>= 0xFFE0 and <= 0xFFE6) or
				>= 0x20000;
}
