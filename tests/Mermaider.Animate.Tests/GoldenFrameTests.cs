using AwesomeAssertions;
using Nullean.Snapshots;

namespace Mermaider.Animate.Tests;

/// <summary>
/// What a reader sees at the end of every step of every preview score, as a static frame. A frame does not care how the
/// stylesheet is written, so a refactor of it passes untouched, and any change to what is shown fails here. Review a
/// change with <c>dotnet snap</c> (it lists the frames that moved and diffs them as text) and accept what was meant.
/// </summary>
public class GoldenFrameTests
{
	/// <summary>
	/// A step's last frame; <see cref="Ending"/> picks one of a score's scenarios, when it has them. A score of highlights
	/// alone has no steps, and has one frame, a moment into its loop (step -1).
	/// </summary>
	public sealed record Frame(string Score, int Step, string Source, int? Ending = null)
	{
		public override string ToString() => Ending is { } e ? $"{Score} ending {e + 1} step {Step + 1}" : $"{Score} step {Step + 1}";
	}

	private static readonly string Scores = Path.Combine(AppContext.BaseDirectory, "scores");

	public static IEnumerable<Frame> Frames()
	{
		foreach (var file in Directory.GetFiles(Scores, "*.mmd").Order(StringComparer.Ordinal))
		{
			var source = File.ReadAllText(file).Replace("\r\n", "\n");
			var animation = MermaidAnimator.Compile(source);
			var name = Path.GetFileNameWithoutExtension(file);
			if (animation.Scenarios.Count == 0 && animation.Steps.Count == 0)
				yield return new Frame(name, -1, source);
			if (animation.Scenarios.Count == 0)
				for (var k = 0; k < animation.Steps.Count; k++)
					yield return new Frame(name, k, source);
			for (var e = 0; e < animation.Scenarios.Count; e++)
				for (var k = 0; k < animation.Scenarios[e].Animation.Steps.Count; k++)
					yield return new Frame(name, k, source, e);
		}
	}

	[Test]
	public void Every_preview_score_has_frames() => Frames().Select(f => f.Score).Distinct().Should().HaveCountGreaterThanOrEqualTo(23);

	[Test]
	[MethodDataSource(nameof(Frames))]
	public void End_of_step(Frame frame)
	{
		var animation = MermaidAnimator.Compile(frame.Source);
		if (frame.Ending is { } e)
			animation = animation.Scenarios[e].Animation;
		var at = frame.Step < 0 ? animation.Intro + TimeSpan.FromMilliseconds(600) : animation.Steps[frame.Step].End;
		var key = frame.Ending is { } ending ? $"{frame.Score}.e{ending + 1}.{frame.Step + 1}" : $"{frame.Score}.{frame.Step + 1}";
		animation.RenderFrame(at).Should().MatchSnapshot(SvgSnapshotFormat.Instance, key);
	}
}
