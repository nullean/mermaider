using System.Globalization;
using System.Text;
using Mermaider.Models;

namespace Mermaider.Rendering.Ascii;

/// <summary>
/// An xy chart drawn in characters. Bars and lines are plotted on a grid; a <c>box</c> series is drawn as a
/// box-and-whisker on one row per category, which is the shape a distribution wants in a terminal — a
/// percentile summary read sideways, where the width of the box is the middle half and the whiskers are the
/// ends.
/// </summary>
internal static class AsciiXyChartRenderer
{
	internal static string Render(XyChart chart, AsciiOptions options)
	{
		var boxes = chart.Series.Where(s => s.Type == XySeriesType.Box && s.Values.Count >= 5).ToList();
		return boxes.Count > 0 && boxes.Count == chart.Series.Count
			? Boxes(chart, boxes, options)
			: Plot(chart, options);
	}

	/// <summary>
	/// One row per category: the axis, then the box. A distribution per line reads better than a grid when
	/// what is being compared is a handful of services, and it is what fits beside a table of numbers.
	/// </summary>
	private static string Boxes(XyChart chart, List<XySeries> boxes, AsciiOptions options)
	{
		var (low, high) = Range(boxes.SelectMany(b => b.Values));
		var names = new string[boxes.Count];
		for (var i = 0; i < boxes.Count; i++)
			names[i] = boxes[i].Name ?? Category(chart, i);

		var gutter = Math.Max(4, names.Max(n => n.Length) + 1);
		var width = Math.Max(24, options.Width - gutter - 2);
		var canvas = new AsciiCanvas(gutter + width + 2, (boxes.Count * 2) + 4, options.Ascii);
		var top = chart.Title is { Length: > 0 } ? 2 : 0;
		if (chart.Title is { Length: > 0 } title)
			canvas.Text(0, 0, title);

		for (var i = 0; i < boxes.Count; i++)
		{
			var row = top + (i * 2);
			var five = boxes[i].Values.Take(5).OrderBy(v => v).ToArray();
			canvas.Text(0, row, names[i]);

			var at = five.Select(v => gutter + Scale(v, low, high, width)).ToArray();
			canvas.Horizontal(at[0], at[1], row);
			canvas.Horizontal(at[3], at[4], row);
			canvas.Glyph(at[0], row, options.Ascii ? '|' : '├');
			canvas.Glyph(at[4], row, options.Ascii ? '|' : '┤');

			// the middle half, drawn solid, with the median marked inside it
			for (var x = at[1]; x <= at[3]; x++)
				canvas.Glyph(x, row, options.Ascii ? '=' : '━');
			canvas.Glyph(at[1], row, options.Ascii ? '[' : '▐');
			canvas.Glyph(at[3], row, options.Ascii ? ']' : '▌');
			canvas.Glyph(at[2], row, options.Ascii ? '#' : '┃');
		}

		var axis = top + (boxes.Count * 2);
		canvas.Horizontal(gutter, gutter + width, axis);
		canvas.Text(gutter, axis + 1, Number(low));
		var highest = Number(high);
		canvas.Text(gutter + width - highest.Length, axis + 1, highest);
		return canvas.ToString();
	}

	private static string Plot(XyChart chart, AsciiOptions options)
	{
		var values = chart.Series.Where(s => s.Type != XySeriesType.Box).SelectMany(s => s.Values).ToList();
		var (low, high) = Range(values);
		if (chart.Series.Any(s => s.Type == XySeriesType.Bar) && low > 0)
			low = 0;

		var categories = Math.Max(1, chart.XCategories?.Count ?? chart.Series.Max(s => s.Values.Count));
		var height = Math.Max(4, options.Height);
		var labels = Enumerable.Range(0, categories).Select(i => Category(chart, i)).ToArray();
		var step = Math.Max(labels.Max(l => l.Length) + 2, 6);
		var gutter = Math.Max(Number(low).Length, Number(high).Length) + 1;
		var canvas = new AsciiCanvas(gutter + (categories * step) + 2, height + 4, options.Ascii);

		var top = chart.Title is { Length: > 0 } ? 2 : 0;
		if (chart.Title is { Length: > 0 } title)
			canvas.Text(0, 0, title);

		var axis = top + height;
		canvas.Vertical(gutter, top, axis);
		canvas.Horizontal(gutter, gutter + (categories * step), axis);
		canvas.Text(0, top, AsciiCanvas.Fit(Number(high), gutter));
		canvas.Text(0, axis, AsciiCanvas.Fit(Number(low), gutter));

		var marks = new[] { '#', '*', '+', 'x', 'o' };
		var mark = 0;
		foreach (var series in chart.Series)
		{
			if (series.Type == XySeriesType.Box)
				continue;
			var glyph = marks[mark++ % marks.Length];
			for (var i = 0; i < series.Values.Count && i < categories; i++)
			{
				var x = gutter + (i * step) + (step / 2);
				var y = axis - Scale(series.Values[i], low, high, height);
				if (series.Type == XySeriesType.Bar)
				{
					for (var row = y; row < axis; row++)
						canvas.Glyph(x, row, glyph);
				}
				else
				{
					canvas.Glyph(x, y, glyph);
				}
			}
		}

		for (var i = 0; i < categories; i++)
			canvas.Centred(gutter + (i * step), step, axis + 1, labels[i]);
		return canvas.ToString();
	}

	private static string Category(XyChart chart, int index) =>
		chart.XCategories is { Count: > 0 } categories && index < categories.Count
			? categories[index]
			: (index + 1).ToString(CultureInfo.InvariantCulture);

	private static (double Low, double High) Range(IEnumerable<double> values)
	{
		var low = double.PositiveInfinity;
		var high = double.NegativeInfinity;
		foreach (var value in values)
		{
			low = Math.Min(low, value);
			high = Math.Max(high, value);
		}

		if (double.IsInfinity(low))
			return (0, 1);
		return high - low < 1e-9 ? (low, low + 1) : (low, high);
	}

	private static int Scale(double value, double low, double high, int size) =>
		(int)Math.Round((value - low) / (high - low) * (size - 1));

	/// <summary>A number an axis can wear: no more digits than the value is worth.</summary>
	private static string Number(double value)
	{
		var magnitude = Math.Abs(value);
		return magnitude switch
		{
			>= 10_000 => (value / 1000).ToString("0.#", CultureInfo.InvariantCulture) + "k",
			>= 100 => value.ToString("0", CultureInfo.InvariantCulture),
			>= 1 => value.ToString("0.##", CultureInfo.InvariantCulture),
			0 => "0",
			_ => value.ToString("0.###", CultureInfo.InvariantCulture),
		};
	}
}
