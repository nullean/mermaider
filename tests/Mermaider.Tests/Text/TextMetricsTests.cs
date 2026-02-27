using AwesomeAssertions;

namespace Mermaider.Tests.Text;

public class TextMetricsTests
{
	[Test]
	public void NarrowCharsAreSmallerThanAverage()
	{
		CharWidths.GetCharWidth('i').Should().BeLessThan(1.0);
		CharWidths.GetCharWidth('l').Should().BeLessThan(1.0);
	}

	[Test]
	public void WideCharsAreLargerThanAverage()
	{
		CharWidths.GetCharWidth('W').Should().BeGreaterThan(1.0);
		CharWidths.GetCharWidth('M').Should().BeGreaterThan(1.0);
	}

	[Test]
	public void MeasureTextWidthReturnsPositive()
	{
		var width = TextMetrics.MeasureTextWidth("Hello", 13, 500);
		width.Should().BeGreaterThan(0);
	}

	[Test]
	public void MultilineReturnsCorrectLineCount()
	{
		var metrics = TextMetrics.MeasureMultiline("Hello\nWorld\nFoo".AsSpan(), 13, 500);
		metrics.LineCount.Should().Be(3);
	}

	[Test]
	public void MultilineHeightScalesWithLines()
	{
		var single = TextMetrics.MeasureMultiline("Hello".AsSpan(), 13, 500);
		var triple = TextMetrics.MeasureMultiline("Hello\nWorld\nFoo".AsSpan(), 13, 500);
		triple.Height.Should().BeGreaterThan(single.Height);
	}
}
