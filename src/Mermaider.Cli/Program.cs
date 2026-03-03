using Mermaider;
using Mermaider.Models;
using Mermaider.Theming;

if (args.Contains("--help") || args.Contains("-h"))
{
	PrintHelp();
	return 0;
}

if (args.Contains("--version"))
{
	Console.WriteLine(typeof(MermaidRenderer).Assembly.GetName().Version);
	return 0;
}

if (args.Contains("--list-themes"))
{
	foreach (var name in Themes.BuiltIn.Keys.Order())
		Console.WriteLine(name);
	return 0;
}

string? inputFile = null;
string? outputFile = null;
string? themeName = null;
var transparent = true;

for (var i = 0; i < args.Length; i++)
{
	switch (args[i])
	{
		case "-i" or "--input" when i + 1 < args.Length:
			inputFile = args[++i];
			break;
		case "-o" or "--output" when i + 1 < args.Length:
			outputFile = args[++i];
			break;
		case "-t" or "--theme" when i + 1 < args.Length:
			themeName = args[++i];
			break;
		case "--transparent":
			transparent = true;
			break;
		case "--no-transparent":
			transparent = false;
			break;
		default:
			if (!args[i].StartsWith('-') && inputFile == null)
				inputFile = args[i];
			break;
	}
}

string input;
if (inputFile != null)
{
	if (!File.Exists(inputFile))
	{
		Console.Error.WriteLine($"Error: file not found: {inputFile}");
		return 1;
	}
	input = File.ReadAllText(inputFile);
}
else if (!Console.IsInputRedirected)
{
	Console.Error.WriteLine("Error: no input. Provide a file argument or pipe input via stdin.");
	Console.Error.WriteLine("Run with --help for usage information.");
	return 1;
}
else
{
	input = Console.In.ReadToEnd();
}

if (string.IsNullOrWhiteSpace(input))
{
	Console.Error.WriteLine("Error: empty input.");
	return 1;
}

var options = BuildOptions(themeName, transparent);

try
{
	var svg = MermaidRenderer.RenderSvg(input, options);

	if (outputFile != null)
	{
		var dir = Path.GetDirectoryName(outputFile);
		if (!string.IsNullOrEmpty(dir))
			_ = Directory.CreateDirectory(dir);
		File.WriteAllText(outputFile, svg);
		Console.Error.WriteLine($"Written to {outputFile}");
	}
	else
	{
		Console.Write(svg);
	}

	return 0;
}
catch (MermaidParseException ex)
{
	Console.Error.WriteLine($"Parse error: {ex.Message}");
	return 1;
}
catch (Exception ex)
{
	Console.Error.WriteLine($"Error: {ex.Message}");
	return 2;
}

static RenderOptions BuildOptions(string? themeName, bool transparent)
{
	DiagramColors colors;
	if (themeName != null && Themes.BuiltIn.TryGetValue(themeName, out var theme))
		colors = theme;
	else if (themeName != null)
	{
		Console.Error.WriteLine($"Warning: unknown theme '{themeName}', using default.");
		colors = Themes.Default;
	}
	else
		colors = Themes.Default;

	return new RenderOptions
	{
		Bg = colors.Bg,
		Fg = colors.Fg,
		Line = colors.Line,
		Accent = colors.Accent,
		Muted = colors.Muted,
		Surface = colors.Surface,
		Border = colors.Border,
		Transparent = transparent,
	};
}

static void PrintHelp() => Console.WriteLine("""
		mermaid - Render Mermaid diagrams to SVG

		USAGE:
		  mermaid [options] [input-file]
		  cat diagram.mmd | mermaid > output.svg

		OPTIONS:
		  -i, --input <file>     Input .mmd file (or pass as positional arg)
		  -o, --output <file>    Output .svg file (default: stdout)
		  -t, --theme <name>     Theme name (use --list-themes to see options)
		  --transparent           Transparent background (default)
		  --no-transparent        Opaque background (uses --bg color)
		  --list-themes           List available theme names
		  --version               Show version
		  -h, --help              Show this help

		EXAMPLES:
		  mermaid diagram.mmd -o diagram.svg
		  mermaid -i flow.mmd -t tokyo-night -o flow.svg
		  echo "graph TD; A-->B" | mermaid > simple.svg
		  mermaid --list-themes
		""");
