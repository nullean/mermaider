# 08 — Testing Strategy

Follows the [Elastic .NET Testing](https://improved-broccoli-v92811n.pages.github.io) standards:
**TUnit** as the test framework, **AwesomeAssertions** for fluent assertions.

## Test Framework Stack

| Package | Purpose | Notes |
|---|---|---|
| `TUnit` | Test framework | Elastic standard (replaces xUnit) |
| `AwesomeAssertions` | Fluent assertions | Elastic standard (fork of FluentAssertions) |
| `Verify.TUnit` | Snapshot/golden file testing | TUnit-compatible Verify integration |
| `Microsoft.NET.Test.Sdk` | Test SDK | Required by TUnit |

## Test Project Setup

`Elastic.DotNet.Build` auto-configures projects under `tests/`:

```xml
<TreatWarningsAsErrors>false</TreatWarningsAsErrors>
<IsTestProject>true</IsTestProject>
<IsPackable>false</IsPackable>
<OutputType>Exe</OutputType>
```

The `Mermaid.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="TUnit" />
    <PackageReference Include="AwesomeAssertions" />
    <PackageReference Include="Verify.TUnit" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/Mermaid/Mermaid.csproj" />
  </ItemGroup>
</Project>
```

No version numbers — Central Package Management handles versions in `Directory.Packages.props`.

## 1. Unit Tests

### Parser Tests (TUnit)

```csharp
// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Mermaid.Tests.Parsing;

public class FlowchartParserTests
{
	[Test]
	public async Task ParsesSimpleGraph()
	{
		var graph = FlowchartParser.Parse("graph TD\n  A --> B");

		graph.Direction.Should().Be(Direction.TD);
		graph.Nodes.Should().HaveCount(2);
		graph.Nodes["A"].Shape.Should().Be(NodeShape.Rectangle);
		graph.Edges.Should().ContainSingle()
			.Which.Should().BeEquivalentTo(new
			{
				Source = "A",
				Target = "B",
				Style = EdgeStyle.Solid,
				HasArrowEnd = true
			});
	}

	[Test]
	[Arguments("A[Text]", NodeShape.Rectangle)]
	[Arguments("A(Text)", NodeShape.Rounded)]
	[Arguments("A{Text}", NodeShape.Diamond)]
	[Arguments("A([Text])", NodeShape.Stadium)]
	[Arguments("A((Text))", NodeShape.Circle)]
	[Arguments("A[/Text\\]", NodeShape.Trapezoid)]
	public async Task ParsesNodeShape(string nodeDef, NodeShape expected)
	{
		var graph = FlowchartParser.Parse($"graph TD\n  {nodeDef}");
		graph.Nodes.Values.Single().Shape.Should().Be(expected);
	}

	[Test]
	[Arguments("-->", EdgeStyle.Solid, false, true)]
	[Arguments("---", EdgeStyle.Solid, false, false)]
	[Arguments("-.->", EdgeStyle.Dotted, false, true)]
	[Arguments("==>", EdgeStyle.Thick, false, true)]
	[Arguments("<-->", EdgeStyle.Solid, true, true)]
	public async Task ParsesEdgeStyles(string arrow, EdgeStyle style, bool arrowStart, bool arrowEnd)
	{
		var graph = FlowchartParser.Parse($"graph TD\n  A {arrow} B");
		var edge = graph.Edges.Single();
		edge.Style.Should().Be(style);
		edge.HasArrowStart.Should().Be(arrowStart);
		edge.HasArrowEnd.Should().Be(arrowEnd);
	}

	[Test]
	public async Task ParsesChainedEdges()
	{
		var graph = FlowchartParser.Parse("graph TD\n  A --> B --> C");
		graph.Edges.Should().HaveCount(2);
	}

	[Test]
	public async Task ParsesParallelLinks()
	{
		var graph = FlowchartParser.Parse("graph TD\n  A & B --> C & D");
		graph.Edges.Should().HaveCount(4); // Cartesian product
	}

	[Test]
	public async Task ParsesSubgraphNesting()
	{
		var graph = FlowchartParser.Parse("""
			graph TD
			  subgraph Outer
			    subgraph Inner
			      A --> B
			    end
			  end
			""");
		graph.Subgraphs.Should().ContainSingle()
			.Which.Children.Should().ContainSingle();
	}
}
```

### Text Metrics Tests

```csharp
namespace Mermaid.Tests.Text;

public class TextMetricsTests
{
	[Test]
	[Arguments("Hello", 13, 500)]
	[Arguments("WIDE", 13, 500)]
	[Arguments("iii", 13, 500)]
	public async Task MeasureTextWidth_ReturnsPositiveValue(string text, double fontSize, int fontWeight)
	{
		var actual = TextMetrics.MeasureTextWidth(text, fontSize, fontWeight);
		actual.Should().BeGreaterThan(0);
	}

	[Test]
	public async Task NarrowText_IsNarrowerThanWideText()
	{
		var narrow = TextMetrics.MeasureTextWidth("ill", 13, 500);
		var wide = TextMetrics.MeasureTextWidth("WWW", 13, 500);
		narrow.Should().BeLessThan(wide);
	}

	[Test]
	public async Task MultilineText_HeightScalesWithLineCount()
	{
		var single = TextMetrics.MeasureMultiline("Hello", 13, 500);
		var double_ = TextMetrics.MeasureMultiline("Hello\nWorld", 13, 500);
		double_.Height.Should().BeGreaterThan(single.Height * 1.5);
	}
}
```

### Character Width Tests

```csharp
namespace Mermaid.Tests.Text;

public class CharWidthTests
{
	[Test]
	[Arguments('i', 0.4)]
	[Arguments('W', 1.5)]
	[Arguments('a', 1.0)]
	[Arguments(' ', 0.3)]
	[Arguments('(', 0.5)]
	[Arguments('A', 1.2)]
	public async Task GetCharWidth_ReturnsExpectedRatio(char c, double expected)
	{
		CharWidths.GetCharWidth(c).Should().Be(expected);
	}
}
```

### Theming Tests

```csharp
namespace Mermaid.Tests.Theming;

public class ThemingTests
{
	[Test]
	public async Task AllBuiltInThemes_HaveRequiredColors()
	{
		foreach (var (name, colors) in Themes.BuiltIn)
		{
			colors.Bg.Should().NotBeNullOrWhiteSpace($"theme '{name}' missing bg");
			colors.Fg.Should().NotBeNullOrWhiteSpace($"theme '{name}' missing fg");
		}
	}

	[Test]
	public async Task StyleBlock_ContainsDerivedVariables()
	{
		var block = StyleBlock.Build("Inter", hasMonoFont: false);
		block.Should().Contain("--_text");
		block.Should().Contain("--_line");
		block.Should().Contain("--_node-fill");
		block.Should().Contain("color-mix");
	}
}
```

## 2. Golden File / Snapshot Tests (Verify)

Use **Verify.TUnit** to snapshot-test full SVG output:

```csharp
namespace Mermaid.Tests.Rendering;

public class GoldenFileTests
{
	[Test]
	[Arguments("simple_flow", "graph TD\n  A --> B")]
	[Arguments("diamond_decision", "graph TD\n  A{Decision} -->|Yes| B\n  A -->|No| C")]
	[Arguments("subgraph", "graph TD\n  subgraph Group\n    A --> B\n  end\n  C --> A")]
	public async Task RenderSvg_MatchesGolden(string name, string mermaidText)
	{
		var svg = MermaidRenderer.RenderSvg(mermaidText);
		await Verify(svg).UseFileName(name);
	}
}
```

### Golden File Workflow

1. First run: Verify creates `.verified.svg` files
2. Review SVGs visually (open in browser)
3. Accept if correct — file becomes the golden reference
4. Subsequent runs: any diff fails the test
5. Use Verify's diff tool integration for visual comparison

### Test Diagram Corpus

```
tests/Mermaid.Tests/TestData/inputs/
├── flowchart/
│   ├── simple.mmd
│   ├── all_shapes.mmd
│   ├── all_edges.mmd
│   ├── subgraph_nested.mmd
│   ├── direction_override.mmd
│   ├── parallel_links.mmd
│   ├── chained_edges.mmd
│   └── classDef_styling.mmd
├── state/
│   ├── simple.mmd
│   ├── composite.mmd
│   └── pseudostates.mmd
├── sequence/
│   └── ...
├── class/
│   └── ...
└── er/
    └── ...
```

## 3. Cross-Reference Tests

Compare parsing results structurally with beautiful-mermaid output:

```csharp
namespace Mermaid.Tests.Rendering;

public class CrossReferenceTests
{
	[Test]
	[Arguments("graph TD\n  A --> B", 2, 1)]
	[Arguments("graph TD\n  A --> B --> C", 3, 2)]
	[Arguments("graph TD\n  A & B --> C", 3, 2)]
	public async Task ParsedGraph_HasExpectedTopology(string mermaidText, int nodeCount, int edgeCount)
	{
		var graph = MermaidRenderer.Parse(mermaidText);
		graph.Nodes.Should().HaveCount(nodeCount);
		graph.Edges.Should().HaveCount(edgeCount);
	}
}
```

## 4. Benchmarks

```csharp
// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Mermaid.Benchmarks;

[MemoryDiagnoser]
public class RenderBenchmarks
{
	private readonly string _simpleFlow = "graph TD\n  A --> B --> C";

	[Benchmark(Baseline = true)]
	public string SimpleFlowchart() => MermaidRenderer.RenderSvg(_simpleFlow);

	[Benchmark]
	public MermaidGraph ParseOnly() => MermaidRenderer.Parse(_simpleFlow);
}
```

Target: render a simple flowchart in < 5ms, complex in < 50ms.

## Test Data Generation Script

Generate reference data from beautiful-mermaid for cross-validation:

```bash
#!/bin/bash
# Run in beautiful-mermaid directory
for f in tests/inputs/**/*.mmd; do
    name=$(basename "$f" .mmd)
    bun -e "
        import { renderMermaidSVG, parseMermaid } from './src/index.ts'
        const text = require('fs').readFileSync('$f', 'utf-8')
        const svg = renderMermaidSVG(text)
        require('fs').writeFileSync('tests/golden/${name}.svg', svg)
    "
done
```

## Files to Create

| File | Purpose |
|---|---|
| `tests/Mermaid.Tests/Parsing/FlowchartParserTests.cs` | Flowchart parser unit tests |
| `tests/Mermaid.Tests/Parsing/StateParserTests.cs` | State parser unit tests |
| `tests/Mermaid.Tests/Text/TextMetricsTests.cs` | Text measurement tests |
| `tests/Mermaid.Tests/Text/CharWidthTests.cs` | Character width tests |
| `tests/Mermaid.Tests/Theming/ThemingTests.cs` | Theme configuration tests |
| `tests/Mermaid.Tests/Rendering/GoldenFileTests.cs` | Snapshot SVG tests |
| `tests/Mermaid.Tests/Rendering/CrossReferenceTests.cs` | Structural comparison |
| `tests/Mermaid.Tests/Layout/LayoutEngineTests.cs` | Layout correctness |
| `tests/Mermaid.Benchmarks/RenderBenchmarks.cs` | Performance benchmarks |

## Code Style Notes

All test files follow Elastic .NET `.editorconfig`:
- **Tab indentation**
- **`var`** for all local declarations
- **Allman braces** (opening brace on new line)
- **`_camelCase`** for private fields
- **Apache 2.0 license header** on every `.cs` file
