using System.Text.RegularExpressions;
using System.Xml.Linq;
using AwesomeAssertions;
using CsCheck;
using Mermaider.Animate.Timeline;

namespace Mermaider.Animate.Tests;

public partial class PropertyTests
{
	/// <summary>
	/// Whatever text a score carries, the stylesheet is only generated selectors, numbers and keywords: no caption, label
	/// or id can reach it, and the SVG stays well-formed.
	/// </summary>
	[Test]
	public void Score_text_never_reaches_the_stylesheet()
	{
		var hostile = Gen.String[Gen.Char["<>{}\"';:/\\@&*()[]%$#!? abcXYZé✓"], 1, 24];
		hostile.Sample(
			text =>
			{
				var escaped = text.Replace("\\", "\\\\").Replace("\"", "\\\"");
				var source =
					$"""
				flowchart LR
				  a --> b
				  b --> c
				animate
				  token t = "{escaped}"
				  step "{escaped}"
				    send t a -> b becomes "{escaped}"
				    try b -> a rejected "{escaped}"
				  caption "{escaped}"
				""";
				var svg = MermaidAnimator.RenderSvg(source);
				var root = XDocument.Parse(svg, LoadOptions.PreserveWhitespace).Root!;
				var style = root.Elements().First(e => e.Name.LocalName == "style").Value;
				foreach (var line in style.Split('\n', StringSplitOptions.RemoveEmptyEntries))
					StylesheetLine().IsMatch(line).Should().BeTrue($"'{line}' is not a generated rule");
				root.Descendants().Where(e => e.Name.LocalName == "text").Should().Contain(e => e.Value == text);
			},
			iter: 60
		);
	}

	[GeneratedRegex("""
		^(\.ma[0-9a-f]{8}-a\d+ \{ animation: [a-z0-9 ,.-]+;( transform-box: view-box; transform-origin: -?[\d.]+px -?[\d.]+px;)? \}|@keyframes ma[0-9a-f]{8}-k\d+i?r? \{( -?[\d.]+% \{ [a-z-]+:[a-z0-9(),. -]+(; animation-timing-function: [a-z0-9(),. -]+)? \})+ \}|@media \(prefers-reduced-motion: reduce\) \{|@media print \{|  \.ma[0-9a-f]{8}-a\d+ \{ animation: [a-z0-9 ,-]+; \}|\}|@supports \(animation-timeline: view\(\)\) \{|  \.ma[0-9a-f]{8}-root \{ view-timeline-name: --ma[0-9a-f]{8}; \}|  [.a-z0-9, -]+ \{ animation-duration: auto; animation-timeline: --ma[0-9a-f]{8}; animation-range: entry 90% exit 10%; \}|@supports \(timeline-trigger: --t view\(\)\) \{|  \.ma[0-9a-f]{8}-a\d+ \{ ([a-z-]+:[a-z0-9(),. -]+(; )?)+; \}|  \.ma[0-9a-f]{8}-root \{ timeline-trigger: --ma[0-9a-f]{8} view\(\) entry 30% exit 0%; \}|  [.a-z0-9, -]+ \{ animation-trigger: --ma[0-9a-f]{8} play-forwards(, --ma[0-9a-f]{8} play-forwards)*; \})$
		""")]
	private static partial Regex StylesheetLine();

	/// <summary>Any sequence of changes leaves a value that never jumps: each change starts where the last one stood.</summary>
	[Test]
	public void A_channel_value_is_continuous_across_overlapping_changes()
	{
		var change = Gen.Select(Gen.Double[0, 5000], Gen.Double[1, 1500], Gen.Double[0, 1]);
		change.Array[1, 12].Sample(
			changes =>
			{
				var channel = new Channel(new XElement("g"), Property.Opacity, [0.5], ReducedMotion.Cut);
				foreach (var (start, duration, to) in changes.OrderBy(c => c.Item1))
					channel.Animate(start, duration, [to], Easing.Move);
				foreach (var segment in channel.Segments)
				{
					var before = channel.ValueAt(segment.Start - 1e-6)[0];
					Math.Abs(before - segment.From[0]).Should().BeLessThan(1e-3);
				}
				channel.Segments.Zip(channel.Segments.Skip(1)).Should().OnlyContain(p => p.First.End <= p.Second.Start + 1e-9);
			},
			iter: 300
		);
	}

	[Test]
	public void Easing_matches_its_inverse()
	{
		Gen.Double[0.001, 0.999].Sample(
			p =>
			{
				Math.Abs(Easing.Move.Evaluate(Easing.Move.Inverse(p)) - p).Should().BeLessThan(1e-4);
			},
			iter: 200
		);
	}
}
