using System.Globalization;
using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

/// <summary>
/// Regression for #86: a class self-relation was Sugiyama's fixed 28px loop, which the class layout reserved no room for,
/// with both cardinality pills and the label stacked on top of it. The loop must reach visibly past its class, start and
/// end on the class, and its three pills must not overlap each other, the loop's class or any other class.
/// </summary>
public partial class ClassSelfLoopGeometryTests
{
	private const int TimeoutMs = 2000;

	/// <summary>The issue's diagram: a memberless (36px) class, the shortest box a loop can sit on.</summary>
	private const string Issue86 =
		"""
		classDiagram
		  class Animal
		  class Duck
		  Animal "1" --> "0..*" Animal : parent
		  Animal <|-- Duck
		""";

	/// <summary>The looping class sits between two siblings 20px away: the layout has to open room for the loop.</summary>
	private const string Cramped =
		"""
		classDiagram
		  class Root
		  Root <|-- A
		  Root <|-- B
		  Root <|-- C
		  B "1" --> "0..*" B : children
		""";

	/// <summary>A class with members, tall enough for the label to sit on the loop's outer vertical.</summary>
	private const string Tall =
		"""
		classDiagram
		  class Node {
		    +String name
		    +int depth
		    +children() List
		  }
		  Node "1" --> "0..*" Node : parent
		  Node <|-- Leaf
		""";

	/// <summary>
	/// Left to right, edges use the left and right sides, so the loops go below and above; a second loop on one class takes
	/// the other side, and the namespace grows around both.
	/// </summary>
	private const string LeftToRight =
		"""
		classDiagram
		  direction LR
		  namespace Zoo {
		    class Animal
		    class Keeper
		  }
		  Animal "1" --> "0..*" Animal : parent
		  Animal "1" o-- "1" Animal : mate
		  Keeper --> Animal
		""";

	[Test]
	[Arguments(Issue86, "Animal", 1)]
	[Arguments(Cramped, "B", 1)]
	[Arguments(Tall, "Node", 1)]
	[Arguments(LeftToRight, "Animal", 2)]
	public void Self_loop_reaches_past_its_class_and_returns_to_it(string diagram, string id, int count)
	{
		var svg = MermaidRenderer.RenderSvg(diagram);
		var box = ClassBoxes(svg)[id];
		var loops = Loops(svg, id);

		loops.Should().HaveCount(count);
		foreach (var loop in loops)
		{
			foreach (var end in new[] { loop[0], loop[^1] })
			{
				box.OnBorder(end).Should().BeTrue($"both ends of the loop {Describe(loop)} touch the class {box}");
			}
			loop.Max(box.Distance).Should().BeGreaterThanOrEqualTo(36, $"the loop {Describe(loop)} must reach visibly past {box}");
			Math
				.Sqrt(Math.Pow(loop[^1].X - loop[0].X, 2) + Math.Pow(loop[^1].Y - loop[0].Y, 2))
				.Should()
				.BeGreaterThanOrEqualTo(20, "the two ends of the loop are apart");
		}
		svg.Should().Contain("marker-end=", "the association keeps its arrowhead");
	}

	[Test]
	public void Left_to_right_loops_go_below_and_above_the_class()
	{
		var svg = MermaidRenderer.RenderSvg(LeftToRight);
		var box = ClassBoxes(svg)["Animal"];
		var loops = Loops(svg, "Animal");

		loops.Should().ContainSingle(l => l.Min(p => p.Y) < box.Y - 30, "one loop goes above");
		loops.Should().ContainSingle(l => l.Max(p => p.Y) > box.Y + box.H + 30, "the other goes below");
		var ns = NamespaceBox(svg);
		foreach (var p in Pills(svg, "Animal"))
			(p.X >= ns.X && p.Y >= ns.Y && p.X + p.W <= ns.X + ns.W && p.Y + p.H <= ns.Y + ns.H).Should().BeTrue($"pill {p} stays inside the namespace {ns}");
		ns.Y.Should().BeGreaterThan(0, "the namespace grown around the upper loop stays on the canvas");
	}

	[Test]
	[Arguments(Issue86, "Animal", 3)]
	[Arguments(Cramped, "B", 3)]
	[Arguments(Tall, "Node", 3)]
	[Arguments(LeftToRight, "Animal", 6)]
	public void Self_loop_pills_overlap_nothing(string diagram, string id, int count)
	{
		var svg = MermaidRenderer.RenderSvg(diagram);
		var boxes = ClassBoxes(svg);
		var pills = Pills(svg, id);

		pills.Should().HaveCount(count, "every label and cardinality is drawn");
		for (var i = 0; i < pills.Count; i++)
		{
			for (var j = i + 1; j < pills.Count; j++)
				pills[i].Intersects(pills[j]).Should().BeFalse($"pill {pills[i]} overlaps pill {pills[j]}");
			foreach (var (name, box) in boxes)
				pills[i].Intersects(box).Should().BeFalse($"pill {pills[i]} overlaps class {name} {box}");
		}
	}

	[Test]
	[Arguments(Issue86, "Animal")]
	[Arguments(Cramped, "B")]
	public void Self_loop_crosses_no_other_class(string diagram, string id)
	{
		var svg = MermaidRenderer.RenderSvg(diagram);
		var loop = Loops(svg, id).Single();
		var reach = new Rect(loop.Min(p => p.X), loop.Min(p => p.Y), loop.Max(p => p.X) - loop.Min(p => p.X), loop[^1].Y - loop[0].Y);
		foreach (var (name, box) in ClassBoxes(svg).Where(b => b.Key != id))
			reach.Intersects(box).Should().BeFalse($"the loop {reach} runs into class {name} {box}");
		reach.X.Should().BeGreaterThanOrEqualTo(0);
		(reach.X + reach.W).Should().BeLessThanOrEqualTo(ViewBoxWidth(svg), "the loop fits on the canvas");
	}

	private readonly record struct Rect(double X, double Y, double W, double H)
	{
		public bool Intersects(Rect o) => X < o.X + o.W && o.X < X + W && Y < o.Y + o.H && o.Y < Y + H;

		public bool OnBorder((double X, double Y) p) =>
			p.X >= X - 0.5 && p.X <= X + W + 0.5 && p.Y >= Y - 0.5 && p.Y <= Y + H + 0.5 && Distance(p) <= 0.5 && Inset(p) <= 0.5;

		/// <summary>How far <paramref name="p"/> lies outside the box (0 inside).</summary>
		public double Distance((double X, double Y) p) => Math.Max(Math.Max(X - p.X, p.X - (X + W)), Math.Max(Math.Max(Y - p.Y, p.Y - (Y + H)), 0));

		private double Inset((double X, double Y) p) =>
			Math.Min(Math.Min(Math.Abs(p.X - X), Math.Abs(p.X - (X + W))), Math.Min(Math.Abs(p.Y - Y), Math.Abs(p.Y - (Y + H))));

		public override string ToString() => FormattableString.Invariant($"[{X:F1},{Y:F1} {W:F1}x{H:F1}]");
	}

	private static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);

	private static Dictionary<string, Rect> ClassBoxes(string svg) =>
		ClassNodePattern()
			.Matches(svg)
			.ToDictionary(m => m.Groups[1].Value, m => new Rect(D(m.Groups[2].Value), D(m.Groups[3].Value), D(m.Groups[4].Value), D(m.Groups[5].Value)));

	private static List<List<(double X, double Y)>> Loops(string svg, string id) =>
		SelfRelationshipPattern()
			.Matches(svg)
			.Where(m => m.Groups[1].Value == id && m.Groups[2].Value == id)
			.Select(path => CoordinatePattern().Matches(path.Groups[3].Value).Select(m => (D(m.Groups[1].Value), D(m.Groups[2].Value))).ToList())
			.ToList();

	private static string Describe(List<(double X, double Y)> loop) => string.Join(" ", loop.Select(p => FormattableString.Invariant($"{p.X:F1},{p.Y:F1}")));

	/// <summary>Every label and cardinality pill on the self-relations of <paramref name="id"/>.</summary>
	private static List<Rect> Pills(string svg, string id) =>
		EdgeLabelGroupPattern()
			.Matches(svg)
			.Where(m => m.Groups[1].Value == id && m.Groups[2].Value == id)
			.SelectMany(group => RectPattern().Matches(group.Groups[3].Value))
			.Select(m => new Rect(D(m.Groups[1].Value), D(m.Groups[2].Value), D(m.Groups[3].Value), D(m.Groups[4].Value)))
			.ToList();

	private static Rect NamespaceBox(string svg)
	{
		var m = NamespacePattern().Match(svg);
		return new Rect(D(m.Groups[1].Value), D(m.Groups[2].Value), D(m.Groups[3].Value), D(m.Groups[4].Value));
	}

	private static double ViewBoxWidth(string svg) => D(ViewBoxPattern().Match(svg).Groups[1].Value);

	[GeneratedRegex(@"<g class=""class-node"" data-id=""([^""]+)""[^>]*>\s*<rect x=""([^""]+)"" y=""([^""]+)"" width=""([^""]+)"" height=""([^""]+)""", RegexOptions.None, TimeoutMs)]
	private static partial Regex ClassNodePattern();

	[GeneratedRegex(@"<path class=""class-relationship"" data-from=""([^""]+)"" data-to=""([^""]+)""[^>]* d=""([^""]+)""", RegexOptions.None, TimeoutMs)]
	private static partial Regex SelfRelationshipPattern();

	[GeneratedRegex(@"(-?[\d.]+),(-?[\d.]+)", RegexOptions.None, TimeoutMs)]
	private static partial Regex CoordinatePattern();

	[GeneratedRegex(@"<g class=""edge-label"" data-from=""([^""]+)"" data-to=""([^""]+)"">([\s\S]*?)</g>", RegexOptions.None, TimeoutMs)]
	private static partial Regex EdgeLabelGroupPattern();

	[GeneratedRegex(@"<rect x=""([^""]+)"" y=""([^""]+)"" width=""([^""]+)"" height=""([^""]+)""", RegexOptions.None, TimeoutMs)]
	private static partial Regex RectPattern();

	[GeneratedRegex(@"class=""subgraph ns-box""[^>]*>\s*<rect x=""([^""]+)"" y=""([^""]+)"" width=""([^""]+)"" height=""([^""]+)""", RegexOptions.None, TimeoutMs)]
	private static partial Regex NamespacePattern();

	[GeneratedRegex(@"viewBox=""[^""]*?\s([\d.]+)\s[\d.]+""", RegexOptions.None, TimeoutMs)]
	private static partial Regex ViewBoxPattern();
}
