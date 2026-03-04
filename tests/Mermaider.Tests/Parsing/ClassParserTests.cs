using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

public class ClassParserTests
{
	[Test]
	public void Parses_class_with_attributes_and_methods()
	{
		var lines = new[]
		{
			"classDiagram",
			"class Animal {",
			"+String name",
			"+eat() void",
			"}",
		};

		var diagram = ClassParser.Parse(lines);

		diagram.Classes.Should().HaveCount(1);
		diagram.Classes[0].Id.Should().Be("Animal");
		diagram.Classes[0].Attributes.Should().HaveCount(1);
		diagram.Classes[0].Attributes[0].Name.Should().Be("name");
		diagram.Classes[0].Attributes[0].Visibility.Should().Be(ClassVisibility.Public);
		diagram.Classes[0].Attributes[0].Type.Should().Be("String");
		diagram.Classes[0].Methods.Should().HaveCount(1);
		diagram.Classes[0].Methods[0].Name.Should().Be("eat");
		diagram.Classes[0].Methods[0].IsMethod.Should().BeTrue();
		diagram.Classes[0].Methods[0].Type.Should().Be("void");
	}

	[Test]
	public void Parses_annotation()
	{
		var lines = new[]
		{
			"classDiagram",
			"class Shape {",
			"<<abstract>>",
			"}",
		};

		var diagram = ClassParser.Parse(lines);

		diagram.Classes[0].Annotation.Should().Be("abstract");
	}

	[Test]
	public void Parses_inline_annotation()
	{
		var lines = new[]
		{
			"classDiagram",
			"class IFlyable { <<interface>> }",
		};

		var diagram = ClassParser.Parse(lines);

		diagram.Classes[0].Annotation.Should().Be("interface");
	}

	[Test]
	public void Parses_inheritance_relationship()
	{
		var lines = new[]
		{
			"classDiagram",
			"Animal <|-- Dog",
		};

		var diagram = ClassParser.Parse(lines);

		diagram.Relationships.Should().HaveCount(1);
		diagram.Relationships[0].From.Should().Be("Animal");
		diagram.Relationships[0].To.Should().Be("Dog");
		diagram.Relationships[0].Type.Should().Be(ClassRelationType.Inheritance);
		diagram.Relationships[0].MarkerAt.Should().Be(ClassMarkerAt.From);
	}

	[Test]
	public void Parses_composition_relationship()
	{
		var lines = new[]
		{
			"classDiagram",
			"Car *-- Engine",
		};

		var diagram = ClassParser.Parse(lines);

		diagram.Relationships[0].Type.Should().Be(ClassRelationType.Composition);
	}

	[Test]
	public void Parses_aggregation_relationship()
	{
		var lines = new[]
		{
			"classDiagram",
			"Car o-- Wheel",
		};

		var diagram = ClassParser.Parse(lines);

		diagram.Relationships[0].Type.Should().Be(ClassRelationType.Aggregation);
	}

	[Test]
	public void Parses_dependency_relationship()
	{
		var lines = new[]
		{
			"classDiagram",
			"A ..> B",
		};

		var diagram = ClassParser.Parse(lines);

		diagram.Relationships[0].Type.Should().Be(ClassRelationType.Dependency);
	}

	[Test]
	public void Parses_realization_relationship()
	{
		var lines = new[]
		{
			"classDiagram",
			"A ..|> B",
		};

		var diagram = ClassParser.Parse(lines);

		diagram.Relationships[0].Type.Should().Be(ClassRelationType.Realization);
	}

	[Test]
	public void Parses_relationship_with_label_and_cardinality()
	{
		var lines = new[]
		{
			"classDiagram",
			"Customer \"1\" --> \"*\" Order : places",
		};

		var diagram = ClassParser.Parse(lines);

		diagram.Relationships[0].Label.Should().Be("places");
		diagram.Relationships[0].FromCardinality.Should().Be("1");
		diagram.Relationships[0].ToCardinality.Should().Be("*");
	}

	[Test]
	public void Parses_inline_attribute()
	{
		var lines = new[]
		{
			"classDiagram",
			"Animal : +String name",
		};

		var diagram = ClassParser.Parse(lines);

		diagram.Classes[0].Attributes.Should().HaveCount(1);
		diagram.Classes[0].Attributes[0].Name.Should().Be("name");
	}

	[Test]
	public void Parses_generic_class()
	{
		var lines = new[]
		{
			"classDiagram",
			"class List~T~ {",
			"+add(item) void",
			"}",
		};

		var diagram = ClassParser.Parse(lines);

		diagram.Classes[0].Label.Should().Be("List<T>");
	}

	[Test]
	public void Parses_namespace()
	{
		var lines = new[]
		{
			"classDiagram",
			"namespace MyApp {",
			"class Service",
			"}",
		};

		var diagram = ClassParser.Parse(lines);

		diagram.Namespaces.Should().HaveCount(1);
		diagram.Namespaces[0].Name.Should().Be("MyApp");
		diagram.Namespaces[0].ClassIds.Should().HaveCount(1);
	}

	[Test]
	public void Parses_visibility_modifiers()
	{
		var lines = new[]
		{
			"classDiagram",
			"class Foo {",
			"+public() void",
			"-private() void",
			"#protected() void",
			"~package() void",
			"}",
		};

		var diagram = ClassParser.Parse(lines);

		diagram.Classes[0].Methods[0].Visibility.Should().Be(ClassVisibility.Public);
		diagram.Classes[0].Methods[1].Visibility.Should().Be(ClassVisibility.Private);
		diagram.Classes[0].Methods[2].Visibility.Should().Be(ClassVisibility.Protected);
		diagram.Classes[0].Methods[3].Visibility.Should().Be(ClassVisibility.Package);
	}

	[Test]
	public void Auto_creates_classes_from_relationships()
	{
		var lines = new[]
		{
			"classDiagram",
			"A --> B",
		};

		var diagram = ClassParser.Parse(lines);

		diagram.Classes.Should().HaveCount(2);
	}

	[Test]
	public void Parses_classDef()
	{
		var lines = new[]
		{
			"classDiagram",
			"classDef highlight fill:#f9f,stroke:#333,stroke-width:4px",
			"class Animal {",
			"+int age",
			"}",
		};

		var diagram = ClassParser.Parse(lines);

		diagram.ClassDefs.Should().ContainKey("highlight");
		diagram.ClassDefs["highlight"]["fill"].Should().Be("#f9f");
		diagram.ClassDefs["highlight"]["stroke"].Should().Be("#333");
		diagram.ClassDefs["highlight"]["stroke-width"].Should().Be("4px");
	}

	[Test]
	public void Parses_cssClass_assignment()
	{
		var lines = new[]
		{
			"classDiagram",
			"classDef highlight fill:#f9f,stroke:#333",
			"class Duck {",
			"+swim() void",
			"}",
			"cssClass \"Duck\" highlight",
		};

		var diagram = ClassParser.Parse(lines);

		diagram.ClassAssignments.Should().ContainKey("Duck");
		diagram.ClassAssignments["Duck"].Should().Be("highlight");
	}

	[Test]
	public void Parses_triple_colon_style_shorthand_with_block()
	{
		var lines = new[]
		{
			"classDiagram",
			"classDef highlight fill:#f9f,stroke:#333",
			"class Fish:::highlight {",
			"+int spikeCount",
			"}",
		};

		var diagram = ClassParser.Parse(lines);

		diagram.Classes.Should().HaveCount(1);
		diagram.Classes[0].Id.Should().Be("Fish");
		diagram.Classes[0].Attributes.Should().HaveCount(1);
		diagram.ClassAssignments.Should().ContainKey("Fish");
		diagram.ClassAssignments["Fish"].Should().Be("highlight");
	}

	[Test]
	public void Parses_triple_colon_style_shorthand_without_block()
	{
		var lines = new[]
		{
			"classDiagram",
			"classDef highlight fill:#f9f",
			"class Fish:::highlight",
		};

		var diagram = ClassParser.Parse(lines);

		diagram.Classes.Should().HaveCount(1);
		diagram.Classes[0].Id.Should().Be("Fish");
		diagram.ClassAssignments.Should().ContainKey("Fish");
		diagram.ClassAssignments["Fish"].Should().Be("highlight");
	}

	[Test]
	public void Parses_style_directive()
	{
		var lines = new[]
		{
			"classDiagram",
			"class Animal {",
			"+int age",
			"}",
			"style Animal fill:#bbf,stroke:#00f",
		};

		var diagram = ClassParser.Parse(lines);

		diagram.NodeStyles.Should().ContainKey("Animal");
		diagram.NodeStyles["Animal"]["fill"].Should().Be("#bbf");
		diagram.NodeStyles["Animal"]["stroke"].Should().Be("#00f");
	}

	[Test]
	public void Parses_cssClass_with_multiple_classes()
	{
		var lines = new[]
		{
			"classDiagram",
			"classDef highlight fill:#f9f",
			"class Duck",
			"class Fish",
			"cssClass \"Duck,Fish\" highlight",
		};

		var diagram = ClassParser.Parse(lines);

		diagram.ClassAssignments.Should().ContainKey("Duck");
		diagram.ClassAssignments.Should().ContainKey("Fish");
		diagram.ClassAssignments["Duck"].Should().Be("highlight");
		diagram.ClassAssignments["Fish"].Should().Be("highlight");
	}

	[Test]
	public void Parses_namespace_with_class_blocks()
	{
		var lines = new[]
		{
			"classDiagram",
			"namespace Animals {",
			"class Duck {",
			"+swim() void",
			"}",
			"class Fish {",
			"+int spikeCount",
			"}",
			"}",
		};

		var diagram = ClassParser.Parse(lines);

		diagram.Namespaces.Should().HaveCount(1);
		diagram.Namespaces[0].Name.Should().Be("Animals");
		diagram.Namespaces[0].ClassIds.Should().HaveCount(2);
		diagram.Namespaces[0].ClassIds.Should().Contain("Duck");
		diagram.Namespaces[0].ClassIds.Should().Contain("Fish");
		diagram.Classes.Should().HaveCount(2);
	}

	[Test]
	public void Parses_full_classDef_and_style_example()
	{
		var lines = new[]
		{
			"classDiagram",
			"classDef highlight fill:#f9f,stroke:#333,stroke-width:4px",
			"class Animal {",
			"+int age",
			"}",
			"class Duck {",
			"+swim() void",
			"}",
			"Animal <|-- Duck",
			"cssClass \"Duck\" highlight",
			"class Fish:::highlight {",
			"+int spikeCount",
			"}",
		};

		var diagram = ClassParser.Parse(lines);

		diagram.Classes.Should().HaveCount(3);
		diagram.ClassDefs.Should().ContainKey("highlight");
		diagram.ClassAssignments["Duck"].Should().Be("highlight");
		diagram.ClassAssignments["Fish"].Should().Be("highlight");
		diagram.Relationships.Should().HaveCount(1);
	}
}
