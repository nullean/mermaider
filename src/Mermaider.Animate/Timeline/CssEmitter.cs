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
	/// <summary>One animation on an element: a channel on one clock, whose window starts at <see cref="Start"/>.</summary>
	internal readonly record struct Entry(Channel Channel, string Suffix, double Start, double End, string Timing);

	/// <summary>
	/// The animations each element runs, in the order its <c>animation</c> list names them. A looping score with an intro
	/// has two clocks; a channel that does not change during one of them leaves it out: the intro's last frame holds
	/// (<c>forwards</c>) through a story that changes nothing, and a story that the intro does not touch fills the intro
	/// with its first frame (<c>backwards</c>). Content changes come first: under reduced motion only they run, so their list
	/// is the start of the full one, and the player's delays for the full list fit it too. An element has one channel per
	/// property, so the order changes nothing else.
	/// </summary>
	internal static List<(XElement Element, List<Entry> Entries)> Plan(IReadOnlyList<Channel> channels, double duration, double storyStart, bool loops)
	{
		var plan = new List<(XElement, List<Entry>)>();
		foreach (var group in channels.Where(c => c.IsAnimated).GroupBy(c => c.Element))
		{
			var entries = new List<Entry>();
			foreach (var channel in group.OrderBy(c => c.Reduced == ReducedMotion.Cut ? 0 : 1))
			{
				if (!loops || storyStart <= 0)
				{
					entries.Add(new(channel, "", 0, duration, $"{Ms(duration)} linear 0s {(loops ? "infinite" : "1")} both"));
					continue;
				}
				var introMoves = !IsConstant(Frames(channel, 0, storyStart, cut: false));
				var storyMoves = !IsConstant(Frames(channel, storyStart, duration, cut: false));
				if (introMoves)
					entries.Add(new(channel, "i", 0, storyStart, $"{Ms(storyStart)} linear 0s 1 forwards"));
				if (storyMoves || !introMoves)
					entries.Add(
						new(
							channel,
							"",
							storyStart,
							duration,
							$"{Ms(duration - storyStart)} linear {Ms(storyStart)} infinite{(introMoves ? "" : " backwards")}"
						)
					);
			}
			plan.Add((group.Key, entries));
		}
		return plan;
	}

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
		var plan = Plan(channels, duration, storyStart, loops);
		var keyframes = new StringBuilder();
		var reduced = new StringBuilder();
		// identical keyframes are written once and shared: many elements appear, dim or light the same way at the same time
		var names = new Dictionary<string, string>(StringComparer.Ordinal);

		string Keyframes(Entry entry, bool cut)
		{
			var body = Body(Frames(entry.Channel, entry.Start, entry.End, cut), entry.Channel, entry.Start, entry.End);
			if (!names.TryGetValue(body, out var name))
			{
				name = $"{prefix}-k{names.Count}";
				names[body] = name;
				_ = keyframes.Append("@keyframes ").Append(name).Append(" {").Append(body).Append(" }\n");
			}
			return name;
		}

		foreach (var (element, entries) in plan)
		{
			var cls = ClassFor(element, prefix, classes);
			var animations = new List<string>();
			var reducedAnimations = new List<string>();
			foreach (var entry in entries)
			{
				animations.Add($"{Keyframes(entry, cut: false)} {entry.Timing}");
				if (entry.Channel.Reduced == ReducedMotion.Cut)
					reducedAnimations.Add($"{Keyframes(entry, cut: true)} {entry.Timing}");
			}
			var origin = entries.Select(e => e.Channel).FirstOrDefault(c => c.Property == Property.Transform);

			_ = sb.Append('.').Append(cls).Append(" { animation: ").Append(string.Join(", ", animations)).Append(';');
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
				.Append(reducedAnimations.Count == 0 ? "none" : string.Join(", ", reducedAnimations))
				.Append("; }\n");
		}

		_ = sb.Append(keyframes);
		if (reduced.Length > 0)
			_ = sb.Append("@media (prefers-reduced-motion: reduce) {\n").Append(reduced).Append("}\n");

		if (mode == PlaybackMode.Scrub && plan.Count > 0)
		{
			var selectors = string.Join(", ", plan.Select(g => "." + classes[g.Element]));
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
		else if (plan.Count > 0)
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
			foreach (var (element, entries) in plan)
				_ = sb
					.Append("  .")
					.Append(classes[element])
					.Append(" { ")
					.Append(string.Join("; ", entries.Select(e => e.Channel).Distinct().Select(c => c.Css(c.ValueAt(0)))))
					.Append("; }\n");
			var trigger = $"--{prefix} play-forwards";
			foreach (var group in plan.GroupBy(g => g.Entries.Count))
				_ = sb
					.Append("  ")
					.Append(string.Join(", ", group.Select(g => "." + classes[g.Element])))
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
	/// at the end. A keyframe between two that hold the same value changes nothing and is left out.
	/// </summary>
	private static List<(double Time, double[] Value, string Ease)> Frames(Channel channel, double from, double to, bool cut)
	{
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

		var kept = new List<(double Time, double[] Value, string Ease)>(frames.Count);
		for (var i = 0; i < frames.Count; i++)
		{
			var held = i > 0 && i < frames.Count - 1 && Same(frames[i - 1].Value, frames[i].Value) && Same(frames[i].Value, frames[i + 1].Value);
			if (!held)
				kept.Add(frames[i]);
		}
		return kept;
	}

	/// <summary>Values that write the same CSS are the same frame: what the browser sees is what counts.</summary>
	private static bool Same(double[] a, double[] b)
	{
		for (var i = 0; i < a.Length; i++)
			if (Num.F3(a[i]) != Num.F3(b[i]))
				return false;
		return true;
	}

	private static bool IsConstant(List<(double Time, double[] Value, string Ease)> frames) => frames.All(f => Same(f.Value, frames[0].Value));

	private static string Body(List<(double Time, double[] Value, string Ease)> frames, Channel channel, double from, double to)
	{
		var sb = new StringBuilder();
		var length = to - from;
		foreach (var (time, value, ease) in frames)
		{
			_ = sb.Append(' ').Append(Pct(time - from, length)).Append(" { ").Append(channel.Css(value));
			if (ease != "linear")
				_ = sb.Append("; animation-timing-function: ").Append(ease);
			_ = sb.Append(" }");
		}
		return sb.ToString();
	}

	private static string Pct(double time, double duration) =>
		Math.Round(Math.Max(0, Math.Min(1, time / duration)) * 100, 3).ToString("0.###", CultureInfo.InvariantCulture) + "%";

	private static string Ms(double ms) => Math.Round(ms).ToString(CultureInfo.InvariantCulture) + "ms";
}
