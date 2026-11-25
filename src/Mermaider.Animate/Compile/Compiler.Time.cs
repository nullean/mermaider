using System.Xml.Linq;
using Mermaider.Animate.Scene;
using Mermaider.Animate.Score;
using Mermaider.Animate.Timeline;

namespace Mermaider.Animate.Compile;

/// <summary>
/// Diagrams about time: a gantt chart's playhead sweeps and tasks grow behind it, a timeline's periods arrive along its
/// axis, a git graph's commits land in order. Time in the diagram maps onto time in the animation at one rate per chart.
/// </summary>
internal sealed partial class Compiler
{
	private DateTime? _playheadAt;
	private XElement? _playhead;
	private readonly HashSet<SceneTarget> _ganttReady = [];

	/// <summary>
	/// Moves the playhead from where it stands to <see cref="SweepStatement.To"/> (or the chart's end). A task's bar grows
	/// from its start date to its end date as the playhead crosses them, and its name appears when the playhead reaches
	/// its start; a milestone appears on its date. The whole chart takes the motion spec's sweep time, so a partial sweep
	/// takes its share of it.
	/// </summary>
	private double Sweep(SweepStatement sweep, double at)
	{
		if (_scene.Kind != SceneKind.Gantt || _scene.TimeAxis is not { } axis)
		{
			_diagnostics.Add(new(sweep.Line, "sweep moves a gantt chart's playhead. For a timeline use show or build; for a git graph use play."));
			return at;
		}
		var tasks = _scene.Ordered;
		var min = tasks.Min(t => t.Start);
		var max = tasks.Max(t => t.End);
		var from = _playheadAt ?? min;
		var to = sweep.To is { } target ? (target < min ? min : target > max ? max : target) : max;
		if (to <= from)
			return at;

		var span = Math.Max(1, (max - min).TotalDays);
		var duration = Math.Max(_spec.Enter, _spec.Sweep * ((to - from).TotalDays / span));

		double Time(DateTime date) => at + (duration * (date - from).TotalDays / (to - from).TotalDays);

		// the playhead: a line across the plot that travels the dates
		var x0 = axis.X(min);
		if (_playhead is null)
		{
			_playhead = new XElement(
				SvgScene.Ns + "line",
				new XAttribute("x1", Num.F(x0)),
				new XAttribute("y1", Num.F(axis.Top)),
				new XAttribute("x2", Num.F(x0)),
				new XAttribute("y2", Num.F(axis.Bottom)),
				new XAttribute("stroke", "var(--accent, #3b82f6)"),
				new XAttribute("stroke-width", "2"),
				new XAttribute("opacity", "0")
			);
			_overlay.Add(_playhead);
			Opacity(_playhead, ReducedMotion.Cut, 0).Animate(at, _spec.Shift, Channel.Value(0.9), Easing.Enter);
		}
		var head = ChannelFor(_playhead, Property.Transform, Channel.Transform(0, 0, 1), ReducedMotion.Off);
		head.Animate(at, duration, Channel.Transform(axis.X(to) - x0, 0, 1), Easing.Linear);

		foreach (var task in tasks)
		{
			Prepare(task);
			// descendants: preparing a task wraps its labels, so they are no longer its direct children
			var text = task.Elements[0].Descendants(SvgScene.Ns + "text").ToList();
			if (task.Start >= from && task.Start <= to)
				foreach (var label in text)
					Visibility(label).Animate(Time(task.Start), _spec.Enter, Channel.Value(1), Easing.Enter);

			if (task.End > task.Start && _edgeVisualsByTarget.TryGetValue(task, out var bar))
			{
				var a = task.Start > from ? task.Start : from;
				var b = task.End < to ? task.End : to;
				if (b > a)
				{
					var reached = (b - task.Start).TotalDays / (task.End - task.Start).TotalDays;
					Ink(bar).Animate(Time(a), Time(b) - Time(a), Channel.Value(1 - reached), Easing.Linear);
				}
			}
			else if (task.End <= task.Start && task.Start >= from && task.Start <= to && task.Mark is { } milestone)
			{
				Opacity(milestone, ReducedMotion.Cut, 0).Animate(Time(task.Start), _spec.Enter, Channel.Value(1), Easing.Enter);
				_ = Pulse(task, Time(task.Start) + (_spec.Enter * 0.5), _spec.PulseScale);
			}
		}
		_playheadAt = to;
		return at + duration;
	}

	private readonly Dictionary<SceneTarget, EdgeVisual> _edgeVisualsByTarget = [];

	/// <summary>Hides a task until the playhead reaches it: its name, and its bar behind a mask that grows from the left.</summary>
	private void Prepare(SceneTarget task)
	{
		if (!_ganttReady.Add(task))
			return;
		foreach (var label in task.Elements[0].Elements(SvgScene.Ns + "text").ToList())
		{
			var visibility = Visibility(label);
			if (!visibility.IsAnimated)
				visibility.Initial[0] = 0;
		}
		if (task.Mark is not { } mark)
			return;
		if (task.End > task.Start && mark.Name.LocalName == "rect")
		{
			var box = task.Box;
			var line = new Polyline([new Vec(box.X - 1, box.Center.Y), new Vec(box.Right + 1, box.Center.Y)]);
			var edge = new SceneEdge(task.Id, task.Id, mark, line) { Stroke = mark };
			var visual = Drawable(edge, box.Height + 8);
			Ink(visual).Initial[0] = 1;
			_edgeVisualsByTarget[task] = visual;
		}
		else
			Opacity(mark, ReducedMotion.Cut, 0);
	}

	/// <summary>Commits land in order; the links into each commit draw just before it, and a branch's badge comes with its first commit.</summary>
	private double PlayCommits(PlayStatement play, double at)
	{
		var commits = _scene.Ordered;
		var first = play.First ?? 1;
		var last = play.Last ?? commits.Count;
		if (first > commits.Count || last > commits.Count || first > last)
		{
			_diagnostics.Add(new(play.Line, $"The diagram has {commits.Count} commits, so #{first}..#{last} is out of range."));
			return at;
		}
		var t = at;
		for (var i = first - 1; i < last; i++)
		{
			var commit = commits[i];
			var incoming = _scene.Edges.Where(e => e.To == commit.Id).ToList();
			var drawn = t;
			foreach (var link in incoming)
				drawn = Math.Max(drawn, Draw(link, t));
			var land = incoming.Count > 0 ? drawn - (_spec.Enter * 0.4) : t;
			if (commit.Branch is { } branch && _scene.Group(branch) is { } badge && !(HasVisibility(badge) && IsVisible(badge, land)))
				_ = Show(badge, land);
			var shown = Show(commit, land);
			_ = Pulse(commit, land + (_spec.Enter * 0.5), _spec.PulseScale);
			t = shown + _spec.Beat;
		}
		return t;
	}
}
