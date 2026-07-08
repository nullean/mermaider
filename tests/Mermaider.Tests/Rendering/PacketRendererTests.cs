using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

public class PacketRendererTests
{
	private const string BasicPacket = """
		packet-beta
		0-15: "Header"
		16-31: "Source"
		32-47: "Destination"
		""";

	[Test]
	public void Renders_valid_svg()
	{
		var svg = MermaidRenderer.RenderSvg(BasicPacket);

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Contains_field_labels()
	{
		var svg = MermaidRenderer.RenderSvg(BasicPacket);

		svg.Should().Contain("Header");
		svg.Should().Contain("Source");
		svg.Should().Contain("Destination");
	}

	[Test]
	public void Contains_bit_numbers()
	{
		var svg = MermaidRenderer.RenderSvg(BasicPacket);

		svg.Should().Contain(">0<");
		svg.Should().Contain(">15<");
		svg.Should().Contain(">16<");
		svg.Should().Contain(">31<");
		svg.Should().Contain(">32<");
		svg.Should().Contain(">47<");
	}

	[Test]
	public void Contains_block_rects()
	{
		var svg = MermaidRenderer.RenderSvg(BasicPacket);

		svg.Should().Contain("<rect");
		svg.Should().Contain("stroke=\"var(--_line)\"");
	}

	[Test]
	public void Contains_title()
	{
		var svg = MermaidRenderer.RenderSvg("""
			packet
			title UDP Header
			0-15: "Source Port"
			16-31: "Dest Port"
			""");

		svg.Should().Contain("UDP Header");
		svg.Should().Contain("Source Port");
		svg.Should().Contain("Dest Port");
	}

	[Test]
	public void Renders_single_bit_fields()
	{
		var svg = MermaidRenderer.RenderSvg("""
			packet
			0: "A"
			1: "B"
			2: "C"
			""");

		svg.Should().Contain("A");
		svg.Should().Contain("B");
		svg.Should().Contain("C");
	}

	[Test]
	public void Renders_bit_count_form()
	{
		var svg = MermaidRenderer.RenderSvg("""
			packet
			+16: "Source Port"
			+16: "Dest Port"
			""");

		svg.Should().Contain("Source Port");
		svg.Should().Contain("Dest Port");
		svg.Should().Contain(">0<");
		svg.Should().Contain(">15<");
		svg.Should().Contain(">31<");
	}

	[Test]
	public void Renders_multi_row_when_exceeding_32_bits()
	{
		var svg = MermaidRenderer.RenderSvg("""
			packet
			0-15: "A"
			16-31: "B"
			32-47: "C"
			48-63: "D"
			""");

		// 32 bits/row → two rows → at least two distinct y positions for rects
		svg.Should().Contain("A");
		svg.Should().Contain("D");
		var rectCount = 0;
		var idx = 0;
		while ((idx = svg.IndexOf("<rect", idx, StringComparison.Ordinal)) >= 0)
		{
			rectCount++;
			idx += 5;
		}
		rectCount.Should().BeGreaterThanOrEqualTo(4);
	}

	[Test]
	public void Escapes_xml_in_labels()
	{
		var svg = MermaidRenderer.RenderSvg("""
			packet
			0-7: "A & B <C>"
			""");

		svg.Should().Contain("A &amp; B &lt;C&gt;");
	}

	[Test]
	public void Renders_empty_packet()
	{
		var svg = MermaidRenderer.RenderSvg("packet-beta");

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Detects_packet_and_packet_beta()
	{
		var a = MermaidRenderer.RenderSvg("""
			packet
			0-7: "X"
			""");
		var b = MermaidRenderer.RenderSvg("""
			packet-beta
			0-7: "X"
			""");

		a.Should().Contain("X");
		b.Should().Contain("X");
	}

	[Test]
	public void Splits_field_spanning_32_bit_row_boundary()
	{
		// Single field 24-40 crosses the row boundary at bit 31/32 → two rects, repeated label.
		var svg = MermaidRenderer.RenderSvg("""
			packet
			24-40: "Spans rows"
			""");

		svg.Should().Contain("Spans rows");
		// Bit labels for each segment: first row 24/31, second row 32/40
		svg.Should().Contain(">24<");
		svg.Should().Contain(">31<");
		svg.Should().Contain(">32<");
		svg.Should().Contain(">40<");

		var labelCount = 0;
		var idx = 0;
		while ((idx = svg.IndexOf("Spans rows", idx, StringComparison.Ordinal)) >= 0)
		{
			labelCount++;
			idx += 10;
		}
		labelCount.Should().Be(2);

		var rectCount = 0;
		idx = 0;
		while ((idx = svg.IndexOf("<rect", idx, StringComparison.Ordinal)) >= 0)
		{
			rectCount++;
			idx += 5;
		}
		rectCount.Should().Be(2);
	}

	[Test]
	public void Pathological_high_bit_does_not_crash_or_allocate_huge_svg()
	{
		// Parser rejects out-of-cap fields; render must stay bounded and succeed.
		var svg = MermaidRenderer.RenderSvg("""
			packet
			2147483646: "x"
			0-7: "Safe"
			""");

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
		svg.Should().Contain("Safe");
		svg.Should().NotContain(">2147483646<");
		// Empty intermediate rows for rejected high bits should not exist
		svg.Length.Should().BeLessThan(50_000);
	}

	[Test]
	public void Pathological_huge_range_is_rejected()
	{
		var svg = MermaidRenderer.RenderSvg("""
			packet
			0-2147483646: "Huge"
			0-7: "Safe"
			""");

		svg.Should().StartWith("<svg");
		svg.Should().Contain("Safe");
		svg.Should().NotContain("Huge");
		svg.Length.Should().BeLessThan(50_000);
	}
}
