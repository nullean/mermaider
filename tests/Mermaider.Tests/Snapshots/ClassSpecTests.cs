using VerifyTUnit;

namespace Mermaider.Tests.Snapshots;

public class ClassSpecTests
{
	[Test]
	public Task All_relationship_types() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			classDiagram
			A <|-- B : Inheritance
			C *-- D : Composition
			E o-- F : Aggregation
			G --> H : Association
			I ..> J : Dependency
			K ..|> L : Realization
			"""), "svg");

	[Test]
	public Task Visibility_modifiers() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
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
			"""), "svg");

	[Test]
	public Task Static_and_abstract_members() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			classDiagram
			class MathUtils {
			+PI$ double
			+add(a, b)$ int
			+compute()* void
			}
			"""), "svg");

	[Test]
	public Task Generics() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
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
			"""), "svg");

	[Test]
	public Task Interface_and_implementations() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
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
			"""), "svg");

	[Test]
	public Task Cardinality_labels() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			classDiagram
			Customer "1" --> "*" Order : places
			Order "1" *-- "1..*" LineItem : contains
			Student "0..*" --> "1..*" Course : enrolls
			"""), "svg");

	[Test]
	public Task Namespace_grouping() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
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
			"""), "svg");

	[Test]
	public Task Annotation_types() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
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
			"""), "svg");

	[Test]
	public Task Inline_attributes() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			classDiagram
			Animal : +String name
			Animal : +int age
			Animal : +isMammal() bool
			Dog : +String breed
			Dog : +bark() void
			Animal <|-- Dog
			"""), "svg");

	[Test]
	public Task Complex_class_hierarchy() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
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
			"""), "svg");

	[Test]
	public Task Direction_left_right() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			classDiagram
			direction LR
			class A
			class B
			A --> B
			"""), "svg");

	[Test]
	public Task Lollipop_interface() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			classDiagram
			class Shape
			class Drawable
			Shape --() Drawable
			"""), "svg");

	[Test]
	public Task Class_note() =>
		Verifier.Verify(MermaidRenderer.RenderSvg("""
			classDiagram
			class Animal
			note for Animal "Can be wild or domestic"
			"""), "svg");
}
