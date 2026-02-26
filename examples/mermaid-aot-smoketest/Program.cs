using Mermaid;
using Mermaid.Models;

var diagrams = new (string Name, string Source)[]
{
	("flowchart", """
		graph TD
		  A[Start] --> B{Decision}
		  B -->|Yes| C[OK]
		  B -->|No| D[Cancel]
		  C --> E[End]
		  D --> E
		"""),
	("state", """
		stateDiagram-v2
		  [*] --> Idle
		  Idle --> Processing : submit
		  Processing --> Done : ok
		  Done --> [*]
		"""),
	("sequence", """
		sequenceDiagram
		  Alice->>Bob: Hello
		  Bob-->>Alice: Hi
		"""),
	("class", """
		classDiagram
		  Animal <|-- Dog
		  class Animal {
		    +String name
		    +eat() void
		  }
		  class Dog {
		    +bark() void
		  }
		"""),
	("er", """
		erDiagram
		  CUSTOMER ||--o{ ORDER : places
		  ORDER ||--|{ LINE_ITEM : contains
		"""),
};

var pass = 0;
var fail = 0;

foreach (var (name, source) in diagrams)
{
	try
	{
		var svg = MermaidRenderer.RenderSvg(source);
		if (!svg.Contains("<svg") || !svg.Contains("</svg>"))
			throw new InvalidOperationException("SVG output missing root element");

		Console.WriteLine($"  OK  {name} ({svg.Length} chars)");
		pass++;
	}
	catch (Exception ex)
	{
		Console.WriteLine($" FAIL {name}: {ex.Message}");
		fail++;
	}
}

// Strict mode
try
{
	var svg = MermaidRenderer.RenderSvg("""
		graph TD
		  A[Start]:::ok --> B[End]
		""", new RenderOptions
	{
		Strict = new StrictModeOptions
		{
			AllowedClasses = [new DiagramClass { Name = "ok", Fill = "#D4EDDA", Stroke = "#28A745", Color = "#155724" }]
		}
	});

	if (!svg.Contains("<svg"))
		throw new InvalidOperationException("Strict mode SVG missing root");

	Console.WriteLine($"  OK  strict-mode ({svg.Length} chars)");
	pass++;
}
catch (Exception ex)
{
	Console.WriteLine($" FAIL strict-mode: {ex.Message}");
	fail++;
}

// SVG sanitizer
try
{
	var result = SvgSanitizer.Sanitize("<svg><rect width='10' height='10'/><script>alert(1)</script></svg>");
	if (!result.HasViolations)
		throw new InvalidOperationException("Sanitizer should have found violations");

	if (result.Svg.Contains("<script"))
		throw new InvalidOperationException("Sanitizer did not strip script");

	Console.WriteLine($"  OK  svg-sanitizer ({result.Violations.Count} violations stripped)");
	pass++;
}
catch (Exception ex)
{
	Console.WriteLine($" FAIL svg-sanitizer: {ex.Message}");
	fail++;
}

Console.WriteLine();
Console.WriteLine($"AOT smoke test: {pass} passed, {fail} failed");
return fail > 0 ? 1 : 0;
