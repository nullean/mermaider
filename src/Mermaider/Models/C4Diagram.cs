namespace Mermaider.Models;

/// <summary>C4 architecture diagram (context, container, component, dynamic, deployment).</summary>
public sealed record C4Diagram
{
	public C4DiagramKind Kind { get; init; }
	public string? Title { get; init; }
	public required IReadOnlyList<C4Node> RootNodes { get; init; }
	public required IReadOnlyList<C4Relation> Relations { get; init; }
	public int ShapeInRow { get; init; } = 4;
	public int BoundaryInRow { get; init; } = 2;
}

public enum C4DiagramKind
{
	Context,
	Container,
	Component,
	Dynamic,
	Deployment,
}

/// <summary>Either a leaf element or a nested boundary.</summary>
public abstract record C4Node(string Alias);

public sealed record C4Element(
	string Alias,
	C4ElementType Type,
	string Label,
	string? Technology,
	string? Description,
	bool External) : C4Node(Alias);

public sealed record C4Boundary(
	string Alias,
	C4BoundaryType Type,
	string Label,
	string? TypeLabel,
	IReadOnlyList<C4Node> Children,
	bool IsDeploymentNode = false,
	string? Technology = null) : C4Node(Alias);

public enum C4ElementType
{
	Person,
	System,
	SystemDb,
	SystemQueue,
	Container,
	ContainerDb,
	ContainerQueue,
	Component,
	ComponentDb,
	ComponentQueue,
	DeploymentNode,
}

public enum C4BoundaryType
{
	Boundary,
	Enterprise,
	System,
	Container,
}

public sealed record C4Relation(
	string From,
	string To,
	string? Label,
	string? Technology,
	bool Bidirectional);
