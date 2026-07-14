using System.Globalization;
using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

public partial class SankeyRendererTests
{
	private const string Basic = """
		sankey-beta
		Electricity grid,Over generation / exports,104.453
		Electricity grid,Heating and cooling - homes,113.726
		Electricity grid,Industry,342.165
		""";

	[Test]
	public void Renders_valid_svg()
	{
		var svg = MermaidRenderer.RenderSvg(Basic);

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Contains_node_labels()
	{
		var svg = MermaidRenderer.RenderSvg(Basic);

		svg.Should().Contain("Electricity grid");
		svg.Should().Contain("Industry");
		svg.Should().Contain("Heating and cooling - homes");
	}

	[Test]
	public void Draws_links_and_nodes()
	{
		var svg = MermaidRenderer.RenderSvg(Basic);

		svg.Should().Contain("<path");
		svg.Should().Contain("<rect");
		// Ribbons now use source→target linearGradients instead of flat fill-opacity
		svg.Should().Contain("linearGradient");
		svg.Should().Contain("sankey-grad-");
	}

	[Test]
	public void Uses_theme_text_for_labels()
	{
		var svg = MermaidRenderer.RenderSvg(Basic);

		svg.Should().Contain("fill=\"var(--_text)\"");
	}

	[Test]
	public void Renders_empty_diagram()
	{
		var svg = MermaidRenderer.RenderSvg("sankey-beta");

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Accessibility_role()
	{
		var svg = MermaidRenderer.RenderSvg("""
			sankey-beta
			accTitle: Energy flow
			A,B,1
			""");

		svg.Should().Contain("aria-roledescription=\"sankey diagram\"");
		svg.Should().Contain("Energy flow");
	}

	[Test]
	public void Renders_cycle_with_distinct_node_positions()
	{
		var svg = MermaidRenderer.RenderSvg("""
			sankey-beta
			A,B,1
			B,C,1
			C,A,1
			""");

		svg.Should().StartWith("<svg");
		var xs = NodeXs(svg);
		xs.Should().ContainKeys("A", "B", "C");
		// Cycle residual edges may be omitted, but columns must not collapse onto one x.
		xs.Values.Distinct().Count().Should().BeGreaterThan(1);
		xs["A"].Should().NotBe(xs["B"]);
		xs["B"].Should().NotBe(xs["C"]);
		xs["C"].Should().NotBe(xs["A"]);

		// Every drawn ribbon must flow left → right (no reverse/zero-span geometry).
		foreach (var (x0, x1) in RibbonXs(svg))
			x1.Should().BeGreaterThan(x0);
	}

	[Test]
	public void Multi_hop_places_source_left_of_target()
	{
		var svg = MermaidRenderer.RenderSvg("""
			sankey-beta
			A,B,10
			B,C,10
			""");

		var xs = NodeXs(svg);
		xs.Should().ContainKeys("A", "B", "C");
		xs["A"].Should().BeLessThan(xs["B"]);
		xs["B"].Should().BeLessThan(xs["C"]);

		foreach (var (x0, x1) in RibbonXs(svg))
			x1.Should().BeGreaterThan(x0);

		// Two forward links A→B and B→C
		svg.Split("<path", StringSplitOptions.None).Length.Should().Be(3);
	}

	[Test]
	public void Skips_self_loop_link()
	{
		var svg = MermaidRenderer.RenderSvg("""
			sankey-beta
			A,A,5
			A,B,3
			""");

		svg.Should().Contain("B");
		// One ribbon for A→B (self-loop omitted)
		svg.Split("<path", StringSplitOptions.None).Length.Should().Be(2);
	}

	/// <summary>Map node label → rect x from adjacent rect/text pairs in the SVG.
	/// Labels include a numeric value suffix (e.g. "A 1") — key is the name portion only.</summary>
	private static Dictionary<string, double> NodeXs(string svg)
	{
		var result = new Dictionary<string, double>(StringComparer.Ordinal);
		foreach (Match m in NodeRectText().Matches(svg))
		{
			var x = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
			// Label format is "Name value" — extract just the name (everything before last space+number)
			var fullLabel = m.Groups[2].Value;
			var spaceIdx = fullLabel.LastIndexOf(' ');
			var name = spaceIdx > 0 ? fullLabel[..spaceIdx] : fullLabel;
			result[name] = x;
		}

		return result;
	}

	/// <summary>Source and target X of each ribbon path (M x0 … first L x1).</summary>
	private static List<(double X0, double X1)> RibbonXs(string svg)
	{
		var result = new List<(double, double)>();
		foreach (Match m in RibbonPath().Matches(svg))
		{
			var x0 = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
			var x1 = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
			result.Add((x0, x1));
		}

		return result;
	}

	[GeneratedRegex("""<rect x="([^"]+)"[^/]*/>\s*<text[^>]*>([^<]*)</text>""", RegexOptions.CultureInvariant, 2000)]
	private static partial Regex NodeRectText();

	[GeneratedRegex("""<path d="M ([0-9.]+) [^"]*? L ([0-9.]+) """, RegexOptions.CultureInvariant, 2000)]
	private static partial Regex RibbonPath();
}
