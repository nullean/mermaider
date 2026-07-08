using AwesomeAssertions;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

public class PacketParserTests
{
	[Test]
	public void Parses_range_fields()
	{
		var lines = new[]
		{
			"packet-beta",
			"0-15: \"Header\"",
			"16-31: \"Source\"",
			"32-47: \"Destination\"",
		};

		var diagram = PacketParser.Parse(lines);

		diagram.Fields.Should().HaveCount(3);
		diagram.Fields[0].Start.Should().Be(0);
		diagram.Fields[0].End.Should().Be(15);
		diagram.Fields[0].Label.Should().Be("Header");
		diagram.Fields[1].Start.Should().Be(16);
		diagram.Fields[1].End.Should().Be(31);
		diagram.Fields[1].Label.Should().Be("Source");
		diagram.Fields[2].Start.Should().Be(32);
		diagram.Fields[2].End.Should().Be(47);
		diagram.Fields[2].Label.Should().Be("Destination");
	}

	[Test]
	public void Parses_single_bit_field()
	{
		var lines = new[]
		{
			"packet",
			"106: \"URG\"",
		};

		var diagram = PacketParser.Parse(lines);

		diagram.Fields.Should().HaveCount(1);
		diagram.Fields[0].Start.Should().Be(106);
		diagram.Fields[0].End.Should().Be(106);
		diagram.Fields[0].Label.Should().Be("URG");
	}

	[Test]
	public void Parses_bit_count_form()
	{
		var lines = new[]
		{
			"packet",
			"+16: \"Source Port\"",
			"+16: \"Dest Port\"",
			"+32: \"Sequence\"",
		};

		var diagram = PacketParser.Parse(lines);

		diagram.Fields.Should().HaveCount(3);
		diagram.Fields[0].Start.Should().Be(0);
		diagram.Fields[0].End.Should().Be(15);
		diagram.Fields[0].Label.Should().Be("Source Port");
		diagram.Fields[1].Start.Should().Be(16);
		diagram.Fields[1].End.Should().Be(31);
		diagram.Fields[1].Label.Should().Be("Dest Port");
		diagram.Fields[2].Start.Should().Be(32);
		diagram.Fields[2].End.Should().Be(63);
		diagram.Fields[2].Label.Should().Be("Sequence");
	}

	[Test]
	public void Parses_title()
	{
		var lines = new[]
		{
			"packet",
			"title UDP Header",
			"0-15: \"Source Port\"",
			"16-31: \"Destination Port\"",
		};

		var diagram = PacketParser.Parse(lines);

		diagram.Title.Should().Be("UDP Header");
		diagram.Fields.Should().HaveCount(2);
	}

	[Test]
	public void Parses_compact_header_title()
	{
		var lines = new[]
		{
			"packet-beta title TCP Segment",
			"0-15: \"Source Port\"",
		};

		var diagram = PacketParser.Parse(lines);

		diagram.Title.Should().Be("TCP Segment");
		diagram.Fields.Should().HaveCount(1);
	}

	[Test]
	public void Parses_mixed_forms()
	{
		var lines = new[]
		{
			"packet",
			"0-15: \"Source Port\"",
			"+16: \"Dest Port\"",
			"32: \"Flag\"",
		};

		var diagram = PacketParser.Parse(lines);

		diagram.Fields.Should().HaveCount(3);
		diagram.Fields[0].Should().BeEquivalentTo(new Mermaider.Models.PacketField(0, 15, "Source Port"));
		diagram.Fields[1].Start.Should().Be(16);
		diagram.Fields[1].End.Should().Be(31);
		diagram.Fields[2].Start.Should().Be(32);
		diagram.Fields[2].End.Should().Be(32);
	}

	[Test]
	public void Handles_empty_packet()
	{
		var lines = new[]
		{
			"packet-beta",
		};

		var diagram = PacketParser.Parse(lines);

		diagram.Fields.Should().BeEmpty();
		diagram.Title.Should().BeNull();
	}

	[Test]
	public void Ignores_zero_bit_count()
	{
		var lines = new[]
		{
			"packet",
			"+0: \"Bad\"",
			"+8: \"OK\"",
		};

		var diagram = PacketParser.Parse(lines);

		diagram.Fields.Should().HaveCount(1);
		diagram.Fields[0].Label.Should().Be("OK");
		diagram.Fields[0].Start.Should().Be(0);
		diagram.Fields[0].End.Should().Be(7);
	}
}
