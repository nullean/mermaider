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
	public sealed record Frame(string Score, int Step, string Source)
	{
		public override string ToString() => $"{Score} step {Step + 1}";
	}

	private static readonly string Scores = Path.Combine(AppContext.BaseDirectory, "scores");

	public static IEnumerable<Frame> Frames()
	{
		foreach (var file in Directory.GetFiles(Scores, "*.mmd").Order(StringComparer.Ordinal))
		{
			var source = File.ReadAllText(file).Replace("\r\n", "\n");
			var steps = MermaidAnimator.Compile(source).Steps.Count;
			for (var k = 0; k < steps; k++)
				yield return new Frame(Path.GetFileNameWithoutExtension(file), k, source);
		}
	}

	[Test]
	public void Every_preview_score_has_frames() => Frames().Select(f => f.Score).Distinct().Should().HaveCountGreaterThanOrEqualTo(23);

	[Test]
	[MethodDataSource(nameof(Frames))]
	public void End_of_step(Frame frame)
	{
		var animation = MermaidAnimator.Compile(frame.Source);
		var step = animation.Steps[frame.Step];
		animation.RenderFrame(step.End).Should().MatchSnapshot(SvgSnapshotFormat.Instance, $"{frame.Score}.{frame.Step + 1}");
	}
}
