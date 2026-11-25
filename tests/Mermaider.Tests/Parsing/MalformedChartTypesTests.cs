using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

/// <summary>
/// Malformed pie, quadrant, xy, radar, venn, sankey and packet charts are refused with a located message by default,
/// and skipped under lenient parsing. Every refused input is one Mermaid's own parser (12.3) refuses; every accepted one it accepts.
/// </summary>
public class MalformedChartTypesTests
{
	[Test]
	[Arguments("pie\n\"Dogs\" : 386\n\"Cats\" :\n", 3, "slice 'Cats' has no value", "a number after ':'")]
	[Arguments("pie\n\"Dogs\" : 386\n\"Cats\"\n", 3, "slice 'Cats' has no value", "': <number>'")]
	[Arguments("pie\n\"Dogs\" : 386\n\"Ca\n", 3, "slice label opened with \" is never closed", "a closing \"")]
	[Arguments("pie\n\"Dogs\" : abc\n", 2, "value 'abc' of slice 'Dogs' is not a number", "a non-negative number")]
	[Arguments("pie\n\"Dogs\" : -1\n", 2, "value '-1' of slice 'Dogs' is not a number", "a non-negative number")]
	[Arguments("pie\nDogs : 3\n", 2, "unexpected text 'Dogs : 3'", "a slice like")]
	[Arguments("pie\nti\n\"Dogs\" : 3\n", 2, "unexpected text 'ti'", "title <text>")]
	[Arguments("pie showdata\n\"Dogs\" : 3\n", 1, "unexpected text after 'pie'", "'showData'")]
	[Arguments("quadrantChart\nCampaign: [0.5,\n", 2, "coordinates of point 'Campaign' are never closed", "']'")]
	[Arguments("quadrantChart\nCampaign: [0.5]\n", 2, "point 'Campaign' needs exactly two coordinates", "[x, y]")]
	[Arguments("quadrantChart\nCampaign: [1.5, 0.5]\n", 2, "coordinate '1.5' of point 'Campaign' is not a number between 0 and 1", "between 0 and 1")]
	[Arguments("quadrantChart\nCampaign: 0.5, 0.5\n", 2, "point 'Campaign' has no coordinates", "[x, y]")]
	[Arguments("quadrantChart\nCamp\n", 2, "unexpected text 'Camp'", "'title <text>'")]
	[Arguments("quadrantChart\nquadrant-5 Nope\n", 2, "there is no quadrant-5", "'quadrant-4'")]
	[Arguments("quadrantChart\nquadrant-2 \n", 2, "quadrant-2 has no label", "a label after")]
	[Arguments("quadrantChart\nx-axis\n", 2, "x-axis has no label", "a label after 'x-axis'")]
	[Arguments("quadrantChart\nx-axis A --> B --> C\n", 2, "x-axis has more than one '-->'", "a single arrow")]
	[Arguments("quadrantChart\ny-axis \"Low --> High\n", 2, "quote opened in the y-axis label is never closed", "a closing")]
	[Arguments("quadrantChart\nA: [0.5, 0.5] radius: x\n", 2, "radius 'x' is not a number", "radius: 12")]
	[Arguments("xychart-beta\nbar [1, 2\n", 2, "values of the bar series are never closed", "']'")]
	[Arguments("xychart-beta\nbar [1, x]\n", 2, "value 'x' of the bar series is not a number", "numbers such as")]
	[Arguments("xychart-beta\nbar [1,, 2]\n", 2, "the bar series has an empty value", "a number between the commas")]
	[Arguments("xychart-beta\nline\n", 2, "line series has no values", "values in square brackets")]
	[Arguments("xychart-beta\nbar [1, 2] extra\n", 2, "unexpected text after the values of the bar series", "after ']'")]
	[Arguments("xychart-beta\nx-axis [a, b\nbar [1, 2]\n", 2, "category list is never closed", "square brackets")]
	[Arguments("xychart-beta\nx-axis [a,, b]\nbar [1, 2]\n", 2, "category list has an empty entry", "a category between the commas")]
	[Arguments("xychart-beta\nx-axis 0 -->\nbar [1, 2]\n", 2, "range is not 'min --> max' with two numbers", "min --> max")]
	[Arguments("xychart-beta\ny-axis [a, b]\nbar [1, 2]\n", 2, "the y-axis cannot list categories", "numeric range")]
	[Arguments("xychart-beta\ntitle \"Sales\nbar [1]\n", 2, "title opened with '\"' is never closed", "a closing")]
	[Arguments("xychart-beta\ntitle\nbar [1]\n", 2, "title has no text", "a title after 'title'")]
	[Arguments("xychart-beta\nbars [1]\n", 2, "unexpected text 'bars [1]'", "'x-axis'")]
	[Arguments("xychart-beta sideways\nbar [1]\n", 1, "unexpected text after 'xychart-beta'", "'horizontal' or 'vertical'")]
	[Arguments("radar-beta\naxis a, b\ncurve c{1, 2\n", 3, "values of curve 'c' are never closed", "'}'")]
	[Arguments("radar-beta\naxis a, b\ncurve c\n", 3, "curve 'c' has no values", "braces")]
	[Arguments("radar-beta\naxis a, b\ncurve c{1, x}\n", 3, "value 'x' of curve 'c' is not a non-negative number", "numbers such as")]
	[Arguments("radar-beta\naxis a, b\ncurve c{a: 1}\n", 3, "curve 'c' has no value for axis 'b'", "a value for every axis")]
	[Arguments("radar-beta\naxis a, b\ncurve c{a: 1, a: 2}\n", 3, "curve 'c' gives axis 'a' twice", "one value per axis")]
	[Arguments("radar-beta\naxis a, b\ncurve c{a: 1, 2}\n", 3, "mixes plain values", "all 'axis: value'")]
	[Arguments("radar-beta\naxis a, b\ncurve c{1, 2}, d{1, 2}d{1}\n", 3, "unexpected text after the values of curve", "a comma")]
	[Arguments("radar-beta\naxis a b\n", 2, "'a b' is not a valid axis id", "letters, digits")]
	[Arguments("radar-beta\naxis a[\"A\n", 2, "label of axis 'a' is never closed", "']'")]
	[Arguments("radar-beta\naxis a, b,\n", 2, "axis list has an empty entry", "an axis id")]
	[Arguments("radar-beta\naxis a, b\nmax x\n", 3, "max value 'x' is not a non-negative number", "max 100")]
	[Arguments("radar-beta\naxis a, b\ngraticule square\n", 3, "graticule 'square' is not a shape", "'circle' or 'polygon'")]
	[Arguments("radar-beta\naxis a, b\nshowLegend maybe\n", 3, "showLegend 'maybe' is not true or false", "'true' or 'false'")]
	[Arguments("radar-beta\naxis a, b\nfoo\n", 3, "unexpected text 'foo'", "'curve'")]
	[Arguments("venn-beta\nset A\nunion A\n", 3, "union of 'A' has only one set", "at least two sets")]
	[Arguments("venn-beta\nset A\nunion A, B\n", 3, "union refers to set 'B', which is not declared", "'set B'")]
	[Arguments("venn-beta\nset A\nset B\nunion A,\n", 4, "union has no set id", "a set id after the comma")]
	[Arguments("venn-beta\nset A B\n", 2, "unexpected text 'B' after set 'A'", "an optional [\"label\"]")]
	[Arguments("venn-beta\nset 1\n", 2, "'1' is not a valid set id", "put anything else in double quotes")]
	[Arguments("venn-beta\nset A[\"Front\n", 2, "label of set 'A' is never closed", "']'")]
	[Arguments("venn-beta\nset A:\n", 2, "size of set 'A' has no value", "a number after ':'")]
	[Arguments("venn-beta\nset A: big\n", 2, "size 'big' of set 'A' is not a number", "a number")]
	[Arguments("venn-beta\nset\n", 2, "set has no set id", "'set'")]
	[Arguments("venn-beta\nsets A\n", 2, "unexpected text 'sets A'", "'union <id>, <id>'")]
	[Arguments("sankey-beta\nA,B\n", 2, "row has 2 fields", "'source,target,value'")]
	[Arguments("sankey-beta\nA,B,1\nA\n", 3, "row has 1 field", "'source,target,value'")]
	[Arguments("sankey-beta\nA,B,\n", 2, "field 3 is empty", "'source,target,value'")]
	[Arguments("sankey-beta\nA,,1\n", 2, "field 2 is empty", "'source,target,value'")]
	[Arguments("sankey-beta\nA,B,1,2\n", 2, "row has more than three fields", "'source,target,value'")]
	[Arguments("sankey-beta\n\"A,B,1\n", 2, "quoted field 1 is never closed", "a closing")]
	[Arguments("sankey-beta\n\"A\" x,B,1\n", 2, "unexpected text after the quoted field 1", "',' after the closing quote")]
	[Arguments("sankey-beta\nA\"x,B,1\n", 2, "quote inside the unquoted field 1", "the whole field in quotes")]
	[Arguments("sankey-beta\n", 1, "the sankey diagram has no links", "rows of 'source,target,value'")]
	[Arguments("sankey-beta extra\nA,B,1\n", 1, "unexpected text after 'sankey-beta'", "nothing else on the opening line")]
	[Arguments("packet-beta\n0-7: \"Source\n", 2, "label of field '0-7' is not closed properly", "closed with \"")]
	[Arguments("packet-beta\n0-7: Source\n", 2, "label of field '0-7' is not quoted", "double quotes")]
	[Arguments("packet-beta\n0-7:\n", 2, "field '0-7' has no label", "a quoted label")]
	[Arguments("packet-beta\n0-7 \"Source\"\n", 2, "unexpected text", "a field like")]
	[Arguments("packet-beta\n0-x: \"Source\"\n", 2, "'0-x' is not a bit or bit range", "'+16'")]
	[Arguments("packet-beta\n0-7: \"Source\"\n9-15: \"Port\"\n", 3, "field starts at bit 9, but the previous field ended at bit 7", "starting at bit 8")]
	[Arguments("packet-beta\n0-7: \"Source\"\n4-10: \"Port\"\n", 3, "field starts at bit 4, but the previous field ended at bit 7", "starting at bit 8")]
	[Arguments("packet-beta\n1-7: \"Source\"\n", 2, "the first field starts at bit 1", "starting at bit 0")]
	[Arguments("packet-beta\n7-0: \"Source\"\n", 2, "bit range 7-0 ends before it starts", "an end bit of at least 7")]
	[Arguments("packet-beta\n+0: \"Source\"\n", 2, "field '+0' has no bits", "a bit count")]
	[Arguments("packet-beta\n+8: \"Source\"\n+8: \"Port\"\n17: \"Flag\"\n", 4, "field starts at bit 17, but the previous field ended at bit 15", "starting at bit 16")]
	[Arguments("packet-beta\nTITLE Header\n0-7: \"Source\"\n", 2, "unexpected text 'TITLE Header'", "a field like")]
	[Arguments("packet-beta\nheader\n", 2, "unexpected text 'header'", "a field like")]
	public void Strict_by_default_throws_with_line_and_expectation(string source, int line, string problem, string expected)
	{
		var act = () => MermaidRenderer.RenderSvg(source);

		var ex = act.Should().Throw<MermaidParseException>().Which;
		ex.Line.Should().Be(line);
		ex.Message.Should().Contain($"on line {line}").And.Contain(problem).And.Contain("Expecting").And.Contain(expected);
		ex.SourceLine.Should().NotBeNullOrEmpty();
	}

	[Test]
	public void Message_points_a_caret_at_the_column()
	{
		var act = () => MermaidRenderer.RenderSvg("pie\n\"Dogs\" : abc\n");

		act.Should().Throw<MermaidParseException>().Which.Message.Should().Be(
			"Parse error on line 2: value 'abc' of slice 'Dogs' is not a number\n  \"Dogs\" : abc\n           ^\nExpecting a non-negative number such as 386 or 3.5."
		);
	}

	// What Mermaid's parser accepts is rendered, however unusual (checked against mermaid.parse 12.3).
	[Test]
	[Arguments("pie\n'Dogs' : 386\n\"Cats\" : 85.5\n\"\" : 1\n")]
	[Arguments("pie showData title Pets\ntitle\n\"A\" : 0\n")]
	[Arguments("quadrantChart\nA: [0.5,0.5]\nB: [ 1 , 0 ]\nC:::c1: [0.2, 0.3] radius: 12, color: #ff0000\nclassDef c1 radius: 5\nx-axis Low -->\nquadrant-12 Odd\nTITLE x\ntitle: y\n")]
	[Arguments("xychart-beta\nx-axis \"Months\" [jan, \"feb mar\", 3]\ny-axis \"Revenue\" 4000 --> 11000\nbar \"Sales\" [1, -2.5, .5, +3]\nline [1 \"p\", 2]\n")]
	[Arguments("xychart-beta horizontal title \"x\"\nx-axis 5 --> 1\nbar [1]\n")]
	[Arguments("radar-beta\naxis a[\"A\"], b-2, _c\ncurve x{_c: 1, a: 2, b-2: 3}, y[\"Y\"]{1, 2, 3, 4}\nmax 10.5\nmin 0\nticks 3.5\ngraticule polygon\nshowLegend false\n")]
	[Arguments("venn-beta\nset A[\"Front\"]: 30\nset \"B C\"\nset D[x]:-1\nunion A, \"B C\"[\"Both\"]: 5\ntext A \"x\"\nstyle A fill:#ff0000\nSET E\n")]
	[Arguments("sankey-beta\nA,B,x\n\"A,1\",\"B\"\"2\",3\nA, ,-1\n")]
	[Arguments("packet-beta title T\n0 - 7: \"A\"\n+ 8: \"B\"\n16: 'C'\n17-31: \"D\\\"E\"\n+1: \"F\"\n")]
	public void Accepts_what_mermaid_accepts(string source)
	{
		var act = () => MermaidRenderer.RenderSvg(source);

		act.Should().NotThrow();
	}

	[Test]
	public void Radar_places_a_keyed_curve_by_axis_and_reads_every_curve_on_a_line()
	{
		var chart = RadarParser.Parse(["radar-beta", "axis a, b, c", "curve x{c: 3, a: 1, b: 2}, y{4, 5, 6}",]);

		chart.Curves.Should().HaveCount(2);
		chart.Curves[0].Values.Should().Equal(1, 2, 3);
		chart.Curves[1].Values.Should().Equal(4, 5, 6);
	}

	[Test]
	public void Radar_axis_label_may_hold_a_comma()
	{
		var chart = RadarParser.Parse(["radar-beta", "axis a[\"One, two\"], b",]);

		chart.Axes.Select(a => a.Label).Should().Equal("One, two", "b");
	}

	[Test]
	public void Packet_reads_single_quoted_and_escaped_labels()
	{
		var diagram = PacketParser.Parse(["packet-beta", "0 - 7: 'Its'", "8-15: \"A\\\"B\"",], strict: false);

		diagram.Fields.Select(f => f.Label).Should().Equal("Its", "A\"B");
	}

	[Test]
	public void Pie_reads_single_quoted_slices()
	{
		var chart = PieParser.Parse(["pie", "'Dogs' : 3",]);

		chart.Slices.Should().ContainSingle().Which.Label.Should().Be("Dogs");
	}

	[Test]
	public void Quadrant_reads_a_point_with_a_class_and_style()
	{
		var chart = QuadrantParser.Parse(["quadrantChart", "A:::c1: [0.2, 0.3] radius: 12",]);

		chart.Points.Should().ContainSingle().Which.Label.Should().Be("A");
	}

	[Test]
	public void Venn_reads_an_unquoted_label_and_ignores_a_non_positive_size()
	{
		var diagram = VennParser.Parse(["venn-beta", "set A[Front]:-1",]);

		var set = diagram.Sets.Should().ContainSingle().Which;
		set.Label.Should().Be("Front");
		set.Size.Should().BeNull();
	}

	// Lenient parsing keeps the old skip-and-render behaviour.
	[Test]
	public void Lenient_pie_skips_a_slice_without_a_value()
	{
		var chart = PieParser.Parse(["pie", "\"Dogs\" : 3", "\"Cats\" :", "junk",], strict: false);

		chart.Slices.Should().ContainSingle().Which.Label.Should().Be("Dogs");
	}

	[Test]
	public void Lenient_quadrant_skips_a_bad_point_and_clamps_the_rest()
	{
		var chart = QuadrantParser.Parse(["quadrantChart", "A: [0.5,", "B: [1.5, -1]", "junk",], strict: false);

		chart.Points.Should().ContainSingle().Which.X.Should().Be(1.0);
	}

	[Test]
	public void Lenient_xychart_keeps_a_slot_for_a_bad_number_and_skips_unknown_lines()
	{
		var chart = XyChartParser.Parse(["xychart-beta", "bar [1, x, 3]", "junk", "line [1, 2",], strict: false);

		chart.Series.Should().ContainSingle().Which.Values.Should().Equal(1, 0, 3);
	}

	[Test]
	public void Lenient_radar_skips_an_unclosed_curve_and_unknown_lines()
	{
		var chart = RadarParser.Parse(["radar-beta", "axis a, b", "curve c{1, 2", "junk", "curve d{3, 4}",], strict: false);

		chart.Curves.Should().ContainSingle().Which.Id.Should().Be("d");
	}

	[Test]
	public void Lenient_venn_skips_a_union_of_one_set()
	{
		var diagram = VennParser.Parse(["venn-beta", "set A", "union A", "set B C", "junk",], strict: false);

		diagram.Sets.Should().ContainSingle().Which.Id.Should().Be("A");
		diagram.Unions.Should().BeEmpty();
	}

	[Test]
	public void Lenient_sankey_skips_rows_that_are_not_links_and_may_be_empty()
	{
		var diagram = SankeyParser.Parse(["sankey-beta", "A,B", "A,B,3",], strict: false);

		diagram.Links.Should().ContainSingle().Which.Value.Should().Be(3);
		SankeyParser.Parse(["sankey-beta",], strict: false).Links.Should().BeEmpty();
	}

	[Test]
	public void Lenient_packet_skips_malformed_fields_and_ignores_the_sequence()
	{
		var diagram = PacketParser.Parse(["packet-beta", "0-7: \"A\"", "0-7: Bad", "20-30: \"Gap\"", "junk",], strict: false);

		diagram.Fields.Should().HaveCount(2);
	}

	[Test]
	public void Strict_parser_calls_throw_where_lenient_ones_do_not()
	{
		// The same malformed input through the strict default throws, through strict: false it does not.
		var lines = new[] { "pie", "\"Dogs\" :", };

		var strict = () => PieParser.Parse(lines);
		var lenient = () => PieParser.Parse(lines, strict: false);

		strict.Should().Throw<MermaidParseException>();
		lenient.Should().NotThrow();
	}
}
