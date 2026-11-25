using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Mermaider.Animate.Scene;

namespace Mermaider.Animate.Timeline;

/// <summary>
/// Writes channels as one stylesheet on a single master clock: every animation lasts the whole timeline and starts at
/// zero, its keyframes placed as percentages of it, so no two elements can drift apart. Classes and keyframe names are
/// generated (<c>{prefix}-a{n}</c>); no text from the diagram or the score ever reaches the stylesheet.
/// </summary>
internal static class CssEmitter
{
	internal static string Emit(IReadOnlyList<Channel> channels, double duration, PlaybackMode mode, string prefix, IDictionary<XElement, string> classes)
	{
		var sb = new StringBuilder();
		var iterations = mode == PlaybackMode.Loop ? "infinite" : "1";
		var total = Ms(duration);
		var byElement = channels.Where(c => c.IsAnimated).GroupBy(c => c.Element).ToList();
		var keyframes = new StringBuilder();
		var reduced = new StringBuilder();
		var n = 0;

		foreach (var group in byElement)
		{
			var cls = ClassFor(group.Key, prefix, classes);
			var names = new List<string>();
			var reducedNames = new List<string>();
			var origin = group.FirstOrDefault(c => c.Property == Property.Transform);
			foreach (var channel in group)
			{
				var name = $"{prefix}-k{n++}";
				names.Add(name);
				WriteKeyframes(keyframes, name, channel, duration, cut: false);
				if (channel.Reduced == ReducedMotion.Cut)
				{
					reducedNames.Add(name + "r");
					WriteKeyframes(keyframes, name + "r", channel, duration, cut: true);
				}
			}

			_ = sb
				.Append('.')
				.Append(cls)
				.Append(" { animation: ")
				.Append(string.Join(", ", names.Select(k => $"{k} {total} linear 0s {iterations} both")))
				.Append(';');
			if (origin is not null)
				_ = sb
					.Append(" transform-box: view-box; transform-origin: ")
					.Append(Num.F(origin.Origin.X))
					.Append("px ")
					.Append(Num.F(origin.Origin.Y))
					.Append("px;");
			_ = sb.Append(" }\n");

			_ = reduced
				.Append("  .")
				.Append(cls)
				.Append(" { animation: ")
				.Append(reducedNames.Count == 0 ? "none" : string.Join(", ", reducedNames.Select(k => $"{k} {total} linear 0s {iterations} both")))
				.Append("; }\n");
		}

		_ = sb.Append(keyframes);
		if (reduced.Length > 0)
			_ = sb.Append("@media (prefers-reduced-motion: reduce) {\n").Append(reduced).Append("}\n");

		if (mode == PlaybackMode.Scrub && byElement.Count > 0)
		{
			var selectors = string.Join(", ", byElement.Select(g => "." + classes[g.Key]));
			_ = sb
				.Append("@supports (animation-timeline: view()) {\n")
				.Append("  .")
				.Append(prefix)
				.Append("-root { view-timeline-name: --")
				.Append(prefix)
				.Append("; }\n")
				.Append("  ")
				.Append(selectors)
				.Append(" { animation-duration: auto; animation-timeline: --")
				.Append(prefix)
				.Append("; animation-range: entry 90% exit 10%; }\n")
				.Append("}\n");
		}
		return sb.ToString();
	}

	internal static string ClassFor(XElement element, string prefix, IDictionary<XElement, string> classes)
	{
		if (!classes.TryGetValue(element, out var cls))
		{
			cls = $"{prefix}-a{classes.Count}";
			classes[element] = cls;
			var existing = (string?)element.Attribute("class");
			element.SetAttributeValue("class", existing is null ? cls : existing + " " + cls);
		}
		return cls;
	}

	private static void WriteKeyframes(StringBuilder sb, string name, Channel channel, double duration, bool cut)
	{
		_ = sb.Append("@keyframes ").Append(name).Append(" {");
		var frames = new List<(double Time, double[] Value, string Ease)> { (0, channel.Initial, "linear") };
		foreach (var segment in channel.Segments)
		{
			var ease = cut ? "steps(1, jump-start)" : segment.Ease.Css;
			var last = frames[^1];
			if (segment.Start > last.Time + 0.5)
				frames.Add((segment.Start, segment.From, ease));
			else
				frames[^1] = (last.Time, segment.From, ease);
			frames.Add((Math.Min(duration, segment.End), segment.To, "linear"));
		}
		if (frames[^1].Time < duration)
			frames.Add((duration, frames[^1].Value, "linear"));

		foreach (var (time, value, ease) in frames)
		{
			_ = sb.Append(' ').Append(Pct(time, duration)).Append(" { ").Append(channel.Css(value));
			if (ease != "linear")
				_ = sb.Append("; animation-timing-function: ").Append(ease);
			_ = sb.Append(" }");
		}
		_ = sb.Append(" }\n");
	}

	private static string Pct(double time, double duration) =>
		Math.Round(Math.Max(0, Math.Min(1, time / duration)) * 100, 3).ToString("0.###", CultureInfo.InvariantCulture) + "%";

	private static string Ms(double ms) => Math.Round(ms).ToString(CultureInfo.InvariantCulture) + "ms";
}
