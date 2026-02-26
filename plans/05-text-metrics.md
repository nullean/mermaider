# 05 — Text Metrics

**See also:** [10-allocation-strategy.md](10-allocation-strategy.md) for allocation patterns.

## Overview

Text measurement is needed to size nodes and edge labels **before** layout. Since we have no DOM or font renderer, beautiful-mermaid uses character-class width estimation — categorizing characters into width buckets and summing up ratios.

This is the simplest component to port (~250 lines of TS) and has no dependencies. It is also a hot path — called for every node and edge label during layout — so it must be **zero-allocation**. All methods operate on `ReadOnlySpan<char>`.

## Character Width Classification

Each character maps to a relative width ratio (1.0 = average lowercase letter):

| Category | Characters | Ratio | Notes |
|---|---|---|---|
| Zero-width | Combining diacritical marks (U+0300–U+036F, etc.) | 0.0 | Overlay on previous char |
| Narrow | `i l t f j I 1 ! \| . , : ; '` | 0.4 | Thin glyphs |
| Semi-narrow punct | `( ) [ ] { } / \ - " \`` | 0.5 | Brackets, slashes |
| Semi-narrow letter | `r` | 0.8 | |
| Space | ` ` | 0.3 | |
| Average lowercase | a-z (excluding above) | 1.0 | Default |
| Digits | 0-9 (excluding `1`) | 1.0 | Uniform in most fonts |
| Uppercase | A-Z (excluding below) | 1.2 | Slightly wider |
| Wide | `W M w m @ %` | 1.2 | |
| Very wide | `W M` | 1.5 | Widest Latin |
| Fullwidth | CJK, emoji | 2.0 | Double-width |

## Implementation — Zero-Allocation Width Lookup

Use `SearchValues<char>` (.NET 8+) for SIMD-optimized set membership, and a direct `switch` pattern for the hot path:

```csharp
namespace Mermaid.Text;

internal static class CharWidths
{
	// SearchValues compiles to SIMD-optimized bitset operations
	private static readonly SearchValues<char> s_narrow =
		SearchValues.Create("iltfjI1!|.,:;'");

	private static readonly SearchValues<char> s_semiNarrowPunct =
		SearchValues.Create("(){}[]/\\-\"`");

	// Inlined hot path — no virtual calls, no dictionary lookups
	internal static double GetCharWidth(char c) => c switch
	{
		' ' => 0.3,
		'r' => 0.8,
		'W' or 'M' => 1.5,
		'w' or 'm' or '@' or '%' => 1.2,
		_ when s_narrow.Contains(c) => 0.4,
		_ when s_semiNarrowPunct.Contains(c) => 0.5,
		>= 'A' and <= 'Z' => 1.2,
		>= '0' and <= '9' => 1.0,
		_ when IsFullwidth(c) => 2.0,
		_ when IsCombiningMark(c) => 0.0,
		_ => 1.0
	};

	private static bool IsCombiningMark(char c) =>
		char.GetUnicodeCategory(c) is
			System.Globalization.UnicodeCategory.NonSpacingMark or
			System.Globalization.UnicodeCategory.EnclosingMark;
}
```

The `switch` expression compiles to an efficient jump table. `SearchValues<char>` uses SIMD intrinsics for the set membership checks — this is substantially faster than `FrozenSet<char>.Contains()` for small character sets.
```

### Fullwidth Detection

Port the Unicode range checks for CJK, Hangul, etc.:

```csharp
private static bool IsFullwidth(char c) =>
    c switch
    {
        >= '\u1100' and <= '\u115F' => true, // Hangul Jamo
        >= '\u2E80' and <= '\u2EFF' => true, // CJK Radicals
        >= '\u3000' and <= '\u303F' => true, // CJK Symbols
        >= '\u3040' and <= '\u309F' => true, // Hiragana
        >= '\u30A0' and <= '\u30FF' => true, // Katakana
        >= '\u3100' and <= '\u312F' => true, // Bopomofo
        >= '\u3130' and <= '\u318F' => true, // Hangul Compat
        >= '\u3190' and <= '\u31FF' => true, // Kanbun + ext
        >= '\u3200' and <= '\u33FF' => true, // Enclosed CJK
        >= '\u3400' and <= '\u4DBF' => true, // CJK Ext A
        >= '\u4E00' and <= '\u9FFF' => true, // CJK Unified
        >= '\uAC00' and <= '\uD7AF' => true, // Hangul Syllables
        >= '\uF900' and <= '\uFAFF' => true, // CJK Compat
        >= '\uFF00' and <= '\uFF60' => true, // Fullwidth ASCII
        >= '\uFFE0' and <= '\uFFE6' => true, // Fullwidth symbols
        _ => false
    };
```

Note: The TS version also checks `code >= 0x20000` for CJK Extension B+, which are supplementary plane characters (surrogate pairs in UTF-16). For C#, handle via `Rune`:

```csharp
internal static double GetRuneWidth(Rune rune)
{
    if (rune.Value >= 0x20000) return 2.0; // CJK Extension B+
    if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.OtherSymbol && IsEmoji(rune))
        return 2.0;
    return GetCharWidth((char)rune.Value); // BMP characters
}
```

### Emoji Detection

The TS version uses `\p{Emoji_Presentation}|\p{Extended_Pictographic}`. C# `Regex` supports Unicode categories too:

```csharp
[GeneratedRegex(@"\p{So}")]  // OtherSymbol covers most emoji
private static partial Regex EmojiPattern();
```

Or use `Rune.GetUnicodeCategory()` and check for `UnicodeCategory.OtherSymbol` combined with range checks.

## Text Width Measurement — Span-Based, Zero-Alloc

```csharp
internal static class TextMetrics
{
	private const double LineHeightRatio = 1.3;

	// Zero-allocation: operates entirely on ReadOnlySpan<char>
	internal static double MeasureTextWidth(ReadOnlySpan<char> text, double fontSize, int fontWeight)
	{
		var baseRatio = fontWeight >= 600 ? 0.60 : fontWeight >= 500 ? 0.57 : 0.54;
		var totalWidth = 0.0;

		// Direct indexing on span — no enumerator allocation
		for (var i = 0; i < text.Length; i++)
			totalWidth += CharWidths.GetCharWidth(text[i]);

		return totalWidth * fontSize * baseRatio + fontSize * 0.15;
	}

	// Zero-allocation width measurement that skips inline formatting tags
	// without allocating a stripped string
	internal static double MeasurePlainWidth(ReadOnlySpan<char> text, double fontSize, int fontWeight)
	{
		var baseRatio = fontWeight >= 600 ? 0.60 : fontWeight >= 500 ? 0.57 : 0.54;
		var totalWidth = 0.0;
		var inTag = false;

		for (var i = 0; i < text.Length; i++)
		{
			var c = text[i];
			if (c == '<') { inTag = true; continue; }
			if (c == '>' && inTag) { inTag = false; continue; }
			if (!inTag)
				totalWidth += CharWidths.GetCharWidth(c);
		}

		return totalWidth * fontSize * baseRatio + fontSize * 0.15;
	}

	// Zero-allocation multiline measurement using EnumerateLines on span
	// Only counts lines and measures widths — does NOT allocate string[]
	internal static MultilineMetrics MeasureMultiline(ReadOnlySpan<char> text, double fontSize, int fontWeight)
	{
		var lineHeight = fontSize * LineHeightRatio;
		var maxWidth = 0.0;
		var lineCount = 0;

		foreach (var line in text.EnumerateLines())
		{
			lineCount++;
			var w = MeasurePlainWidth(line, fontSize, fontWeight);
			if (w > maxWidth)
				maxWidth = w;
		}

		if (lineCount == 0)
			lineCount = 1;

		return new MultilineMetrics(maxWidth, lineCount * lineHeight, lineCount, lineHeight);
	}
}

// No string[] — just dimensions. The renderer splits lines separately when needed.
internal readonly record struct MultilineMetrics(
	double Width,
	double Height,
	int LineCount,
	double LineHeight
);
```

## Font Constants

```csharp
internal static class FontSizes
{
    internal const double NodeLabel = 13;
    internal const double EdgeLabel = 11;
    internal const double GroupHeader = 12;
}

internal static class FontWeights
{
    internal const int NodeLabel = 500;
    internal const int EdgeLabel = 400;
    internal const int GroupHeader = 600;
}
```

## Files to Create

| File | Lines (est.) | Complexity |
|---|---|---|
| `Text/CharWidths.cs` | ~80 | Low — lookup tables |
| `Text/TextMetrics.cs` | ~80 | Low — arithmetic |
| `Text/MultilineUtils.cs` | ~60 | Low — regex replace |
| `Rendering/FontConstants.cs` | ~30 | Low — constants |

## Testing

Text metrics are easy to unit test — compare measured widths against known values:

```csharp
[Theory]
[InlineData("Hello", 13, 500, /* expected ~= 31.7 */)]
[InlineData("WIDE", 13, 500, /* expected ~= 28.5 */)]
[InlineData("iii", 13, 500, /* expected ~= 10.4 */)]
public void MeasureTextWidth_ReturnsExpectedWidth(string text, double fontSize, int fontWeight, double expected)
{
    var actual = TextMetrics.MeasureTextWidth(text, fontSize, fontWeight);
    Assert.InRange(actual, expected * 0.9, expected * 1.1); // 10% tolerance
}
```

Calibrate expected values by running the TS implementation on the same inputs.
