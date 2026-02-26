using AwesomeAssertions;
using Mermaid.Models;
using Mermaid.Parsing;

namespace Mermaid.Tests.Parsing;

public class ErParserTests
{
	[Test]
	public void Parses_entity_with_attributes()
	{
		var lines = new[]
		{
			"erDiagram",
			"CUSTOMER {",
			"string name PK",
			"int age",
			"string email UK \"user email\"",
			"}",
		};

		var diagram = ErParser.Parse(lines);

		diagram.Entities.Should().HaveCount(1);
		diagram.Entities[0].Id.Should().Be("CUSTOMER");
		diagram.Entities[0].Attributes.Should().HaveCount(3);
		diagram.Entities[0].Attributes[0].Type.Should().Be("string");
		diagram.Entities[0].Attributes[0].Name.Should().Be("name");
		diagram.Entities[0].Attributes[0].Keys.Should().HaveCount(1);
		diagram.Entities[0].Attributes[0].Keys[0].Should().Be(ErKeyType.PK);
		diagram.Entities[0].Attributes[2].Keys[0].Should().Be(ErKeyType.UK);
		diagram.Entities[0].Attributes[2].Comment.Should().Be("user email");
	}

	[Test]
	public void Parses_identifying_relationship()
	{
		var lines = new[]
		{
			"erDiagram",
			"CUSTOMER ||--o{ ORDER : places",
		};

		var diagram = ErParser.Parse(lines);

		diagram.Relationships.Should().HaveCount(1);
		diagram.Relationships[0].Entity1.Should().Be("CUSTOMER");
		diagram.Relationships[0].Entity2.Should().Be("ORDER");
		diagram.Relationships[0].Cardinality1.Should().Be(ErCardinality.One);
		diagram.Relationships[0].Cardinality2.Should().Be(ErCardinality.ZeroMany);
		diagram.Relationships[0].Label.Should().Be("places");
		diagram.Relationships[0].Identifying.Should().BeTrue();
	}

	[Test]
	public void Parses_non_identifying_relationship()
	{
		var lines = new[]
		{
			"erDiagram",
			"CUSTOMER ||..o{ ORDER : places",
		};

		var diagram = ErParser.Parse(lines);

		diagram.Relationships[0].Identifying.Should().BeFalse();
	}

	[Test]
	public void Parses_zero_one_cardinality()
	{
		var lines = new[]
		{
			"erDiagram",
			"PERSON |o--|| ADDRESS : lives-at",
		};

		var diagram = ErParser.Parse(lines);

		diagram.Relationships[0].Cardinality1.Should().Be(ErCardinality.ZeroOne);
		diagram.Relationships[0].Cardinality2.Should().Be(ErCardinality.One);
	}

	[Test]
	public void Parses_many_cardinality()
	{
		var lines = new[]
		{
			"erDiagram",
			"ORDER }|--|{ PRODUCT : contains",
		};

		var diagram = ErParser.Parse(lines);

		diagram.Relationships[0].Cardinality1.Should().Be(ErCardinality.Many);
		diagram.Relationships[0].Cardinality2.Should().Be(ErCardinality.Many);
	}

	[Test]
	public void Auto_creates_entities_from_relationships()
	{
		var lines = new[]
		{
			"erDiagram",
			"A ||--o{ B : has",
		};

		var diagram = ErParser.Parse(lines);

		diagram.Entities.Should().HaveCount(2);
	}

	[Test]
	public void Parses_entity_without_attributes()
	{
		var lines = new[]
		{
			"erDiagram",
			"SIMPLE {",
			"}",
		};

		var diagram = ErParser.Parse(lines);

		diagram.Entities.Should().HaveCount(1);
		diagram.Entities[0].Attributes.Should().HaveCount(0);
	}
}
