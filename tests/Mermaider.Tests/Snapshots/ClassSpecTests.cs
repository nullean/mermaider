using AwesomeAssertions;
using Nullean.Snapshots;

namespace Mermaider.Tests.Snapshots;

public class ClassSpecTests
{
	[Test]
	public void All_relationship_types() =>
		MermaidRenderer
			.RenderSvg(
				"""
			classDiagram
			A <|-- B : Inheritance
			C *-- D : Composition
			E o-- F : Aggregation
			G --> H : Association
			I ..> J : Dependency
			K ..|> L : Realization
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Visibility_modifiers() =>
		MermaidRenderer
			.RenderSvg(
				"""
			classDiagram
			class Account {
			+String publicField
			-int privateField
			#double protectedField
			~bool packageField
			+getBalance() double
			-validate() bool
			#reset() void
			~internal() void
			}
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Static_and_abstract_members() =>
		MermaidRenderer
			.RenderSvg(
				"""
			classDiagram
			class MathUtils {
			+PI$ double
			+add(a, b)$ int
			+compute()* void
			}
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Generics() =>
		MermaidRenderer
			.RenderSvg(
				"""
			classDiagram
			class Repository~T~ {
			+getById(id) T
			+save(entity) void
			+findAll() List~T~
			}
			class UserRepo {
			+getById(id) User
			}
			Repository~T~ <|-- UserRepo
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Interface_and_implementations() =>
		MermaidRenderer
			.RenderSvg(
				"""
			classDiagram
			class ILogger {
			<<interface>>
			+Log(msg) void
			+LogError(ex) void
			}
			class ConsoleLogger {
			-bool verbose
			+Log(msg) void
			+LogError(ex) void
			}
			class FileLogger {
			-String path
			+Log(msg) void
			+LogError(ex) void
			+Flush() void
			}
			ILogger <|.. ConsoleLogger
			ILogger <|.. FileLogger
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Cardinality_labels() =>
		MermaidRenderer
			.RenderSvg(
				"""
			classDiagram
			Customer "1" --> "*" Order : places
			Order "1" *-- "1..*" LineItem : contains
			Student "0..*" --> "1..*" Course : enrolls
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	// #88: a cardinality sits beside its line next to its own class, clear of the relation label at the midpoint
	[Test]
	public void Cardinality_beside_relation_label() =>
		MermaidRenderer
			.RenderSvg("""
			classDiagram
			Pond o-- "many" Duck : holds
			Duck *-- "2" Wing : has
			""")
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Cardinality_beside_relation_label_lr() =>
		MermaidRenderer
			.RenderSvg(
				"""
			classDiagram
			direction LR
			Customer "1" --> "0..*" Order : places
			Order "1" *-- "1..*" LineItem : contains
			Customer "1" --> "1" Address
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Namespace_grouping() =>
		MermaidRenderer
			.RenderSvg(
				"""
			classDiagram
			namespace Domain {
			class User {
			+String name
			+login() void
			}
			class Order {
			+int id
			+submit() void
			}
			}
			namespace Infrastructure {
			class Database {
			+connect() void
			+query(sql) void
			}
			}
			User --> Order
			Order --> Database
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Annotation_types() =>
		MermaidRenderer
			.RenderSvg(
				"""
			classDiagram
			class Shape {
			<<abstract>>
			+area() double
			}
			class Drawable {
			<<interface>>
			+draw() void
			}
			class Color {
			<<enumeration>>
			RED
			GREEN
			BLUE
			}
			class EventBus {
			<<service>>
			+publish(event) void
			}
			Shape <|-- Circle
			Drawable <|.. Circle
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Inline_attributes() =>
		MermaidRenderer
			.RenderSvg(
				"""
			classDiagram
			Animal : +String name
			Animal : +int age
			Animal : +isMammal() bool
			Dog : +String breed
			Dog : +bark() void
			Animal <|-- Dog
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Complex_class_hierarchy() =>
		MermaidRenderer
			.RenderSvg(
				"""
			classDiagram
			class Vehicle {
			<<abstract>>
			+String make
			+int year
			+start() void
			+stop() void
			}
			class Car {
			+int doors
			+drive() void
			}
			class Truck {
			+double payload
			+haul() void
			}
			class Electric {
			<<interface>>
			+charge() void
			+batteryLevel() int
			}
			Vehicle <|-- Car
			Vehicle <|-- Truck
			Electric <|.. Car : optional
			Car *-- Engine
			class Engine {
			+int horsepower
			+String type
			}
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Direction_left_right() =>
		MermaidRenderer
			.RenderSvg("""
			classDiagram
			direction LR
			class A
			class B
			A --> B
			""")
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Lollipop_interface() =>
		MermaidRenderer
			.RenderSvg("""
			classDiagram
			class Shape
			class Drawable
			Shape --() Drawable
			""")
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Class_note() =>
		MermaidRenderer
			.RenderSvg("""
			classDiagram
			class Animal
			note for Animal "Can be wild or domestic"
			""")
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Method_classifiers() =>
		MermaidRenderer
			.RenderSvg(
				"""
			classDiagram
			class Animal {
				+int count$
				+sleep()$
				+wake() bool$
				+run()*
				+walk() bool*
			}
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Generic_class_as_relationship_endpoint() =>
		MermaidRenderer
			.RenderSvg(
				"""
			classDiagram
			class List~T~ {
				+add(T item)
			}
			Duck ..> List~T~
			List~T~ <|-- ArrayList
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Solid_and_dashed_links() =>
		MermaidRenderer
			.RenderSvg("""
			classDiagram
			A -- B
			A --> C
			C .. D : uses
			""")
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);
}
