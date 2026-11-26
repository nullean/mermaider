using System.Text.RegularExpressions;
using System.Xml.Linq;
using AwesomeAssertions;
using CsCheck;

namespace Mermaider.Animate.Tests;

/// <summary>
/// Whatever an author (or an agent) writes in an animate block, over any diagram type, the result is an animation or an
/// <see cref="AnimationException"/> listing what is wrong: never another exception, never a hang, never broken SVG.
/// </summary>
public class FuzzTests
{
	private static readonly string Scores = Path.Combine(AppContext.BaseDirectory, "scores");

	/// <summary>Every preview diagram without its score: one of each diagram type.</summary>
	private static readonly string[] Diagrams =
	[
		.. Directory
			.GetFiles(Scores, "*.mmd")
			.Order(StringComparer.Ordinal)
			.Select(f => File.ReadAllText(f).Replace("\r\n", "\n"))
			.Select(s => s[..s.IndexOf("\nanimate", StringComparison.Ordinal)] + "\n"),
	];

	private static readonly string[] Verbs =
	[
		"build",
		"show",
		"hide",
		"focus",
		"pulse",
		"mark",
		"badge",
		"fill",
		"hang",
		"draw",
		"erase",
		"cut",
		"restore",
		"send",
		"stream",
		"tour",
		"ping",
		"run",
		"try",
		"play",
		"sweep",
		"move",
		"expand",
		"set",
		"trace",
		"note",
		"caption",
		"highlight",
		"scenario",
		"count",
	];

	private static readonly string[] Words =
	[
		"build",
		"show",
		"hide",
		"focus",
		"pulse",
		"mark",
		"badge",
		"fill",
		"hang",
		"draw",
		"erase",
		"cut",
		"restore",
		"send",
		"stream",
		"tour",
		"ping",
		"run",
		"try",
		"play",
		"sweep",
		"move",
		"expand",
		"set",
		"trace",
		"note",
		"caption",
		"highlight",
		"count",
		"step",
		"with",
		"token",
		"close",
		"each",
		"always",
		"ok",
		"fail",
		"warn",
		"info",
		"clear",
		"as",
		"to",
		"becomes",
		"slow",
		"fast",
		"lost",
		"stall",
		"rejected",
		"group",
		"all",
		"loop",
		"once",
		"scrub",
		"calm",
		"crisp",
		"still",
		"trickle",
		"busy",
		"->",
		"<->",
		",",
		"=",
		"#1",
		"#2",
		"#3",
		"#1..#3",
		"#9",
		"3/8",
		"8",
		"75",
		"-2",
		"[*]",
		"\"x\"",
		"\"",
		"\"2024-03-01\"",
		"a",
		"b",
		"c",
		"client",
		"api",
		"db",
		"lb",
		"Idle",
		"Done",
		"Todo",
		"o",
		"x",
		"A",
		"B",
	];

	[Test]
	public void Any_animate_block_compiles_or_reports_its_problems()
	{
		Diagrams.Should().HaveCountGreaterThanOrEqualTo(23);
		var junk = Gen.String[Gen.Char["<>{}\"';:/\\@&*()[]%$#!? abc-.,#é\t"], 0, 6];
		// names come from the diagram itself as well, so many scores get past the parser and into the compiler
		var sources = Gen.OneOf(Diagrams).SelectMany(diagram =>
		{
			var names = Regex.Matches(diagram, @"[A-Za-z_][\w]*").Select(m => m.Value).Distinct().ToArray();
			var token = Gen.Frequency((6, Gen.OneOf(Words)), (4, Gen.OneOf(names)), (1, junk));
			// most lines open the way real ones do: a verb, "with" and a verb, or a step
			var verb = Gen.OneOf(Verbs);
			var opening = Gen.Frequency((6, verb), (2, verb.Select(v => "with " + v)), (1, Gen.Const("step \"x\"")), (1, token));
			var line = Gen.Select(
				Gen.Int[0, 3],
				opening,
				token.Array[0, 6],
				(indent, first, tokens) => new string(' ', indent * 2) + first + " " + string.Join(" ", tokens)
			);
			return Gen.Select(
				Gen.OneOf("", " loop", " once", " scrub", " crisp", " still"),
				line.Array[0, 12],
				(header, lines) => diagram + "\nanimate" + header + "\n" + string.Join("\n", lines) + "\n"
			);
		});
		sources.Sample(
			source =>
			{
				// on a task, so a score that hangs the compiler fails here instead of stalling the run
				var compiling = Task.Run(() =>
				{
					try
					{
						var animation = MermaidAnimator.Compile(source);
						_ = XDocument.Parse(animation.Svg);
						_ = XDocument.Parse(animation.RenderFrame(animation.ScoreDuration));
						_ = animation.RenderPlayer();
						return null;
					}
					catch (AnimationException e)
					{
						return e;
					}
				});
				compiling.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue("no score makes the compiler loop");
				if (compiling.Result is { } problems)
					problems.Diagnostics.Should().NotBeEmpty().And.OnlyContain(d => d.Line > 0, "every problem points at its line");
			},
			iter: 1000
		);
	}
}
