using System.Globalization;
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
var ascii = false;
var plain = false;
var width = 120;
var style = DiagramStyle.Quiet;
var gradient = true;
double? tint = null;
int? elevation = null;

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
		case "--ascii":
			ascii = true;
			break;
		case "--plain":
			ascii = true;
			plain = true;
			break;
		case "--width" when i + 1 < args.Length:
			_ = int.TryParse(args[++i], out width);
			break;
		case "--style":
			if (i + 1 >= args.Length || !TryParseStyle(args[++i], out style))
				return UsageError("--style expects one of: quiet, blueprint, tonal");
			break;
		case "--gradient":
			gradient = true;
			break;
		case "--no-gradient":
			gradient = false;
			break;
		case "--tint":
			if (i + 1 >= args.Length
				|| !double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var t)
				|| !double.IsFinite(t) || t is < 0.5 or > 1.5)
				return UsageError("--tint expects a number between 0.5 and 1.5 (default 1)");
			tint = t;
			break;
		case "--elevation":
			if (i + 1 >= args.Length
				|| !int.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out var e)
				|| e is < 0 or > 2)
				return UsageError("--elevation expects 0 (none), 1 (default) or 2 (adds ambient shadow)");
			elevation = e;
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

var options = BuildOptions(themeName, transparent) with
{
	Style = style,
	Gradient = gradient,
	Tint = tint,
	Elevation = elevation,
};

try
{
	var svg = ascii
		? MermaidRenderer.RenderAscii(input, new AsciiOptions { Ascii = plain, Width = width < 20 ? 120 : width })
		: MermaidRenderer.RenderSvg(input, options);

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
catch (MermaidSvgException ex)
{
	Console.Error.WriteLine($"SVG error: {ex.Message}");
	return 2;
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

static bool TryParseStyle(string value, out DiagramStyle style)
{
	style = DiagramStyle.Quiet;
	switch (value.ToLowerInvariant())
	{
		case "quiet":
			return true;
		case "blueprint":
			style = DiagramStyle.Blueprint;
			return true;
		case "tonal":
			style = DiagramStyle.Tonal;
			return true;
		default:
			return false;
	}
}

static int UsageError(string message)
{
	Console.Error.WriteLine($"Error: {message}");
	Console.Error.WriteLine("Run with --help for usage information.");
	return 1;
}

static void PrintHelp() => Console.WriteLine("""
		mermaid - Render Mermaid diagrams to SVG

		USAGE:
		  mermaid [options] [input-file]
		  cat diagram.mmd | mermaid > output.svg

		OPTIONS:
		  -i, --input <file>     Input .mmd file (or pass as positional arg)
		  -o, --output <file>    Output .svg file (default: stdout)
		  --ascii                 Draw the diagram as text instead of SVG
		  --plain                 As --ascii, with no characters above ASCII
		  --width <n>             How wide text output may be (default 120)
		  -t, --theme <name>     Theme name (use --list-themes to see options)
		  --transparent           Transparent background (default)
		  --no-transparent        Opaque background (uses --bg color)
		  --style <name>          Style preset: quiet (default), blueprint, tonal
		  --no-gradient           Flat fills instead of the top-to-bottom gradient
		  --tint <0.5-1.5>        Strength of colour-family tints (default 1)
		  --elevation <0|1|2>     Shadows: 0 none, 1 default, 2 adds ambient
		                          (blueprint never draws shadows)
		  --list-themes           List available theme names
		  --version               Show version
		  -h, --help              Show this help

		EXAMPLES:
		  mermaid diagram.mmd -o diagram.svg
		  mermaid -i flow.mmd -t tokyo-night -o flow.svg
		  mermaid flow.mmd --style blueprint --elevation 0 -o flow.svg
		  mermaid flow.mmd -t github-dark --style tonal --tint 0.8 -o flow.svg
		  echo "graph TD; A-->B" | mermaid > simple.svg
		  mermaid --list-themes
		""");
