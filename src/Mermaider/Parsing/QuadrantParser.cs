using System.Globalization;
using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

internal static partial class QuadrantParser
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^title\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex TitlePattern();

	[GeneratedRegex(@"^x-axis\s+(.+?)(?:\s*-->\s*(.+))?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex XAxisPattern();

	[GeneratedRegex(@"^y-axis\s+(.+?)(?:\s*-->\s*(.+))?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex YAxisPattern();

	[GeneratedRegex(@"^quadrant-([1-4])\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex QuadrantLabelPattern();

	[GeneratedRegex(@"^(.+?):\s*\[\s*(-?[0-9]*\.?[0-9]+)\s*,\s*(-?[0-9]*\.?[0-9]+)\s*\]$", RegexOptions.None, TimeoutMs)]
	private static partial Regex PointPattern();

	internal static QuadrantChart Parse(string[] lines)
	{
		try
		{
			return ParseCore(lines);
		}
		catch (RegexMatchTimeoutException ex)
		{
			throw new MermaidParseException(
				$"Parsing timed out after {ex.MatchTimeout.TotalSeconds}s — input may contain pathological patterns.",
				ex);
		}
	}

	private static QuadrantChart ParseCore(string[] lines)
	{
		string? title = null;
		string? xAxisLeft = null, xAxisRight = null;
		string? yAxisBottom = null, yAxisTop = null;
		string? q1 = null, q2 = null, q3 = null, q4 = null;
		var points = new List<QuadrantPoint>();

		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];

			var titleMatch = TitlePattern().Match(line);
			if (titleMatch.Success)
			{
				title = titleMatch.Groups[1].Value.Trim();
				continue;
			}

			var xMatch = XAxisPattern().Match(line);
			if (xMatch.Success)
			{
				xAxisLeft = xMatch.Groups[1].Value.Trim();
				xAxisRight = xMatch.Groups[2].Success ? xMatch.Groups[2].Value.Trim() : null;
				continue;
			}

			var yMatch = YAxisPattern().Match(line);
			if (yMatch.Success)
			{
				yAxisBottom = yMatch.Groups[1].Value.Trim();
				yAxisTop = yMatch.Groups[2].Success ? yMatch.Groups[2].Value.Trim() : null;
				continue;
			}

			var qMatch = QuadrantLabelPattern().Match(line);
			if (qMatch.Success)
			{
				var label = qMatch.Groups[2].Value.Trim();
				switch (qMatch.Groups[1].Value)
				{
					case "1":
						q1 = label;
						break;
					case "2":
						q2 = label;
						break;
					case "3":
						q3 = label;
						break;
					case "4":
						q4 = label;
						break;
				}
				continue;
			}

			var pMatch = PointPattern().Match(line);
			if (pMatch.Success)
			{
				var label = pMatch.Groups[1].Value.Trim();
				var x = double.Parse(pMatch.Groups[2].Value, CultureInfo.InvariantCulture);
				var y = double.Parse(pMatch.Groups[3].Value, CultureInfo.InvariantCulture);
				x = Math.Clamp(x, 0, 1);
				y = Math.Clamp(y, 0, 1);
				points.Add(new QuadrantPoint(label, x, y));
			}
		}

		return new QuadrantChart
		{
			Title = title,
			XAxisLeft = xAxisLeft,
			XAxisRight = xAxisRight,
			YAxisBottom = yAxisBottom,
			YAxisTop = yAxisTop,
			Quadrant1 = q1,
			Quadrant2 = q2,
			Quadrant3 = q3,
			Quadrant4 = q4,
			Points = points,
		};
	}
}
