using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Mermaider.Animate.Scene;

namespace Mermaider.Animate.Timeline;

/// <summary>
/// Writes channels as one stylesheet on shared clocks: every animation on a clock lasts its whole length and starts with
/// it, keyframes placed as percentages, so no two elements can drift apart. A looping score with an intro has two
/// clocks: the intro plays once and holds its last frame, and the story starts as the intro ends and repeats. Classes
/// and keyframe names are generated (<c>{prefix}-a{n}</c>); no text from the diagram or the score reaches the stylesheet.
/// </summary>
internal static class CssEmitter
{
	internal static string Emit(
		IReadOnlyList<Channel> channels,
		double duration,
		double storyStart,
		bool loops,
		PlaybackMode mode,
		string prefix,
		IDictionary<XElement, string> classes
	)
	{
		var sb = new StringBuilder();
		// (suffix, window start, window end, timing): the intro holds with forwards, the story waits out the intro
		var clocks = loops && storyStart > 0
			? new[]
			{
				("i", 0.0, storyStart, $"{Ms(storyStart)} linear 0s 1 forwards"),
				("", storyStart, duration, $"{Ms(duration - storyStart)} linear {Ms(storyStart)} infinite")
			}
			: [("", 0.0, duration, $"{Ms(duration)} linear 0s {(loops ? "infinite" : "1")} both")];
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
				foreach (var (suffix, from, to, timing) in clocks)
				{
					names.Add($"{name}{suffix} {timing}");
					WriteKeyframes(keyframes, name + suffix, channel, from, to, cut: false);
					if (channel.Reduced == ReducedMotion.Cut)
					{
						reducedNames.Add($"{name}{suffix}r {timing}");
						WriteKeyframes(keyframes, name + suffix + "r", channel, from, to, cut: true);
					}
				}
			}

			_ = sb.Append('.').Append(cls).Append(" { animation: ").Append(string.Join(", ", names)).Append(';');
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
				.Append(reducedNames.Count == 0 ? "none" : string.Join(", ", reducedNames))
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
		else if (byElement.Count > 0)
		{
			// where supported, the animation waits until the diagram is on screen, so a reader below the fold sees it begin.
			// animation-trigger is a list matched to the animations one by one, so every animation gets its own entry.
			_ = sb
				.Append("@supports (timeline-trigger: --t view()) {\n")
				.Append("  .")
				.Append(prefix)
				.Append("-root { timeline-trigger: --")
				.Append(prefix)
				.Append(" view() entry 30% exit 0%; }\n");
			// until it fires the animation has no effect, so the waiting diagram takes its first frame as plain styles
			foreach (var group in byElement)
				_ = sb.Append("  .").Append(classes[group.Key]).Append(" { ").Append(string.Join("; ", group.Select(c => c.Css(c.ValueAt(0))))).Append("; }\n");
			var trigger = $"--{prefix} play-forwards";
			foreach (var group in byElement.GroupBy(g => g.Count() * clocks.Length))
				_ = sb
					.Append("  ")
					.Append(string.Join(", ", group.Select(g => "." + classes[g.Key])))
					.Append(" { animation-trigger: ")
					.Append(string.Join(", ", Enumerable.Repeat(trigger, group.Key)))
					.Append("; }\n");
			_ = sb.Append("}\n");
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

	/// <summary>
	/// Keyframes for one channel over the window [<paramref name="from"/>, <paramref name="to"/>]: its value at the window's
	/// start, every change inside it with that change's easing (a change the window cuts through goes linear), and its value
	/// at the end.
	/// </summary>
	private static void WriteKeyframes(StringBuilder sb, string name, Channel channel, double from, double to, bool cut)
	{
		_ = sb.Append("@keyframes ").Append(name).Append(" {");
		var length = to - from;
		var frames = new List<(double Time, double[] Value, string Ease)> { (from, channel.ValueAt(from), "linear") };
		foreach (var segment in channel.Segments)
		{
			if (segment.End <= from || segment.Start >= to)
				continue;
			var start = Math.Max(from, segment.Start);
			var end = Math.Min(to, segment.End);
			var whole = start == segment.Start && end == segment.End;
			var ease = cut ? "steps(1, jump-start)" : whole ? segment.Ease.Css : "linear";
			var last = frames[^1];
			if (start > last.Time + 0.5)
				frames.Add((start, channel.ValueAt(start), ease));
			else
				frames[^1] = (last.Time, last.Value, ease);
			frames.Add((end, whole ? segment.To : channel.ValueAt(end), "linear"));
		}
		if (frames[^1].Time < to)
			frames.Add((to, frames[^1].Value, "linear"));

		foreach (var (time, value, ease) in frames)
		{
			_ = sb.Append(' ').Append(Pct(time - from, length)).Append(" { ").Append(channel.Css(value));
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
