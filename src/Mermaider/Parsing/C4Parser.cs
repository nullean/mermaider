using System.Globalization;
using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

internal static partial class C4Parser
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^C4(Context|Container|Component|Dynamic|Deployment)\b(?:\s+title\s+(.+))?$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex HeaderPattern();

	[GeneratedRegex(@"^title\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex TitlePattern();

	[GeneratedRegex(
		@"^(Person(?:_Ext)?|System(?:Db|Queue)?(?:_Ext)?|Container(?:Db|Queue)?(?:_Ext)?|Component(?:Db|Queue)?(?:_Ext)?|Deployment_Node|Node(?:_[LR])?|Boundary|Enterprise_Boundary|System_Boundary|Container_Boundary)\s*\((.+)\)\s*(\{)?\s*$",
		RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex ElementOrBoundaryPattern();

	[GeneratedRegex(
		@"^(BiRel|Rel_Back|Rel_U|Rel_Up|Rel_D|Rel_Down|Rel_L|Rel_Left|Rel_R|Rel_Right|RelIndex|Rel)\s*\((.+)\)\s*$",
		RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex RelationPattern();

	[GeneratedRegex(@"^UpdateLayoutConfig\s*\((.+)\)\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex LayoutConfigPattern();

	[GeneratedRegex(@"^Update(?:Element|Rel)Style\s*\(", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex StyleUpdatePattern();

	private sealed class BoundaryFrame(
		string alias,
		C4BoundaryType type,
		string label,
		string? typeLabel,
		bool isDeploymentNode,
		string? technology)
	{
		public string Alias { get; } = alias;
		public C4BoundaryType Type { get; } = type;
		public string Label { get; } = label;
		public string? TypeLabel { get; } = typeLabel;
		public bool IsDeploymentNode { get; } = isDeploymentNode;
		public string? Technology { get; } = technology;
		public List<C4Node> Children { get; } = [];
	}

	internal static C4Diagram Parse(string[] lines)
	{
		try
		{
			return ParseCore(lines);
		}
		catch (RegexMatchTimeoutException ex)
		{
			throw new MermaidParseException(
				$"Parsing timed out after {ex.MatchTimeout.TotalSeconds}s — input may contain pathological patterns.",
				ex);
		}
	}

	private static C4Diagram ParseCore(string[] lines)
	{
		var headerMatch = HeaderPattern().Match(lines[0]);
		var kind = headerMatch.Success
			? MapKind(headerMatch.Groups[1].Value)
			: DetectKindFallback(lines[0]);
		var title = headerMatch.Success && headerMatch.Groups[2].Success
			? Unquote(headerMatch.Groups[2].Value.Trim())
			: null;

		var shapeInRow = 4;
		var boundaryInRow = 2;
		var relations = new List<C4Relation>();
		var root = new List<C4Node>();
		var stack = new Stack<BoundaryFrame>();

		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];
			if (line is "{" or "}")
			{
				if (line is "}" && stack.Count > 0)
					FlushBoundary(stack, root);
				continue;
			}

			var titleMatch = TitlePattern().Match(line);
			if (titleMatch.Success)
			{
				title = Unquote(titleMatch.Groups[1].Value.Trim());
				continue;
			}

			var layoutMatch = LayoutConfigPattern().Match(line);
			if (layoutMatch.Success)
			{
				ParseLayoutConfig(layoutMatch.Groups[1].Value, ref shapeInRow, ref boundaryInRow);
				continue;
			}

			if (StyleUpdatePattern().IsMatch(line))
				continue;

			var relMatch = RelationPattern().Match(line);
			if (relMatch.Success)
			{
				var rel = ParseRelation(relMatch.Groups[1].Value, relMatch.Groups[2].Value);
				if (rel is not null)
					relations.Add(rel);
				continue;
			}

			var elMatch = ElementOrBoundaryPattern().Match(line);
			if (!elMatch.Success)
				continue;

			var typeName = elMatch.Groups[1].Value;
			var argsRaw = elMatch.Groups[2].Value;
			var opensBrace = elMatch.Groups[3].Success;
			var args = PositionalArgs(SplitArgs(argsRaw));
			if (args.Count == 0)
				continue;

			if (IsBoundaryKeyword(typeName))
			{
				var alias = args[0];
				var label = args.Count > 1 ? Unquote(args[1]) : alias;
				string? typeLabel = null;
				if (typeName.Equals("Boundary", StringComparison.OrdinalIgnoreCase) && args.Count > 2)
					typeLabel = Unquote(args[2]);

				var boundaryType = typeName.ToLowerInvariant() switch
				{
					"enterprise_boundary" => C4BoundaryType.Enterprise,
					"system_boundary" => C4BoundaryType.System,
					"container_boundary" => C4BoundaryType.Container,
					_ => C4BoundaryType.Boundary,
				};

				if (opensBrace || PeekOpensBrace(lines, i))
				{
					if (!opensBrace && i + 1 < lines.Length && lines[i + 1] is "{")
						i++;
					stack.Push(new BoundaryFrame(alias, boundaryType, label, typeLabel, isDeploymentNode: false, technology: null));
				}
				else
				{
					AddNode(new C4Boundary(alias, boundaryType, label, typeLabel, []), stack, root);
				}
				continue;
			}

			if (IsDeploymentNode(typeName))
			{
				var alias = args[0];
				var label = args.Count > 1 ? Unquote(args[1]) : alias;
				var techn = args.Count > 2 ? Unquote(args[2]) : null;
				var descr = args.Count > 3 ? Unquote(args[3]) : null;

				if (opensBrace || PeekOpensBrace(lines, i))
				{
					if (!opensBrace && i + 1 < lines.Length && lines[i + 1] is "{")
						i++;
					stack.Push(new BoundaryFrame(alias, C4BoundaryType.Boundary, label, typeLabel: null, isDeploymentNode: true, technology: techn));
				}
				else
				{
					AddNode(new C4Element(alias, C4ElementType.DeploymentNode, label, techn, descr, External: false), stack, root);
				}
				continue;
			}

			var element = ParseElement(typeName, args);
			if (element is not null)
				AddNode(element, stack, root);
		}

		while (stack.Count > 0)
			FlushBoundary(stack, root);

		return new C4Diagram
		{
			Kind = kind,
			Title = title,
			RootNodes = root,
			Relations = relations,
			ShapeInRow = Math.Max(1, shapeInRow),
			BoundaryInRow = Math.Max(1, boundaryInRow),
		};
	}

	private static C4DiagramKind MapKind(string capture) => capture.ToLowerInvariant() switch
	{
		"container" => C4DiagramKind.Container,
		"component" => C4DiagramKind.Component,
		"dynamic" => C4DiagramKind.Dynamic,
		"deployment" => C4DiagramKind.Deployment,
		_ => C4DiagramKind.Context,
	};

	private static C4DiagramKind DetectKindFallback(string header)
	{
		// Prefer longest / most specific token first
		if (header.StartsWith("C4Deployment", StringComparison.OrdinalIgnoreCase))
			return C4DiagramKind.Deployment;
		if (header.StartsWith("C4Component", StringComparison.OrdinalIgnoreCase))
			return C4DiagramKind.Component;
		if (header.StartsWith("C4Container", StringComparison.OrdinalIgnoreCase))
			return C4DiagramKind.Container;
		if (header.StartsWith("C4Dynamic", StringComparison.OrdinalIgnoreCase))
			return C4DiagramKind.Dynamic;
		return C4DiagramKind.Context;
	}

	private static bool PeekOpensBrace(string[] lines, int i) =>
		i + 1 < lines.Length && lines[i + 1] is "{";

	private static bool IsBoundaryKeyword(string typeName) =>
		typeName.Equals("Boundary", StringComparison.OrdinalIgnoreCase)
		|| typeName.Equals("Enterprise_Boundary", StringComparison.OrdinalIgnoreCase)
		|| typeName.Equals("System_Boundary", StringComparison.OrdinalIgnoreCase)
		|| typeName.Equals("Container_Boundary", StringComparison.OrdinalIgnoreCase);

	private static bool IsDeploymentNode(string typeName) =>
		typeName.Equals("Deployment_Node", StringComparison.OrdinalIgnoreCase)
		|| typeName.Equals("Node", StringComparison.OrdinalIgnoreCase)
		|| typeName.Equals("Node_L", StringComparison.OrdinalIgnoreCase)
		|| typeName.Equals("Node_R", StringComparison.OrdinalIgnoreCase);

	private static void AddNode(C4Node node, Stack<BoundaryFrame> stack, List<C4Node> root)
	{
		if (stack.Count > 0)
			stack.Peek().Children.Add(node);
		else
			root.Add(node);
	}

	private static void FlushBoundary(Stack<BoundaryFrame> stack, List<C4Node> root)
	{
		var frame = stack.Pop();
		var boundary = new C4Boundary(
			frame.Alias,
			frame.Type,
			frame.Label,
			frame.TypeLabel,
			frame.Children,
			frame.IsDeploymentNode,
			frame.Technology);
		if (stack.Count > 0)
			stack.Peek().Children.Add(boundary);
		else
			root.Add(boundary);
	}

	private static C4Element? ParseElement(string typeName, List<string> args)
	{
		if (args.Count == 0)
			return null;

		var external = typeName.EndsWith("_Ext", StringComparison.OrdinalIgnoreCase);
		var baseName = external ? typeName[..^4] : typeName;

		var elementType = baseName.ToLowerInvariant() switch
		{
			"person" => C4ElementType.Person,
			"system" => C4ElementType.System,
			"systemdb" => C4ElementType.SystemDb,
			"systemqueue" => C4ElementType.SystemQueue,
			"container" => C4ElementType.Container,
			"containerdb" => C4ElementType.ContainerDb,
			"containerqueue" => C4ElementType.ContainerQueue,
			"component" => C4ElementType.Component,
			"componentdb" => C4ElementType.ComponentDb,
			"componentqueue" => C4ElementType.ComponentQueue,
			_ => (C4ElementType?)null,
		};

		if (elementType is null)
			return null;

		var positional = PositionalArgs(args);
		if (positional.Count == 0)
			return null;

		var alias = positional[0];
		var label = positional.Count > 1 ? Unquote(positional[1]) : alias;

		string? technology = null;
		string? description = null;

		if (elementType is C4ElementType.Person or C4ElementType.System or C4ElementType.SystemDb or C4ElementType.SystemQueue)
		{
			if (positional.Count > 2)
				description = Unquote(positional[2]);
		}
		else
		{
			if (positional.Count > 2)
				technology = Unquote(positional[2]);
			if (positional.Count > 3)
				description = Unquote(positional[3]);
		}

		return new C4Element(alias, elementType.Value, label, technology, description, external);
	}

	private static C4Relation? ParseRelation(string keyword, string argsRaw)
	{
		// Rel_U/D/L/R (and Up/Down/Left/Right) accept as plain Rel for v1; layout direction hints ignored.
		var args = PositionalArgs(SplitArgs(argsRaw));
		var offset = keyword.Equals("RelIndex", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
		if (args.Count < offset + 2)
			return null;

		var from = args[offset];
		var to = args[offset + 1];
		// Rel_Back(from, to, …) draws to → from (C4-PlantUML / mermaid argument order).
		if (keyword.Equals("Rel_Back", StringComparison.OrdinalIgnoreCase))
			(from, to) = (to, from);

		var label = args.Count > offset + 2 ? Unquote(args[offset + 2]) : null;
		var techn = args.Count > offset + 3 ? Unquote(args[offset + 3]) : null;
		var bi = keyword.Equals("BiRel", StringComparison.OrdinalIgnoreCase);

		return new C4Relation(from, to, label, techn, bi);
	}

	private static List<string> PositionalArgs(List<string> args)
	{
		var result = new List<string>(args.Count);
		foreach (var a in args)
		{
			if (!a.StartsWith('$'))
				result.Add(a);
		}
		return result;
	}

	private static void ParseLayoutConfig(string argsRaw, ref int shapeInRow, ref int boundaryInRow)
	{
		foreach (var arg in SplitArgs(argsRaw))
		{
			var eq = arg.IndexOf('=');
			if (eq < 0)
				continue;
			var key = arg[..eq].Trim().TrimStart('$');
			var val = Unquote(arg[(eq + 1)..].Trim());
			if (!int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < 1)
				continue;
			if (key.Equals("c4ShapeInRow", StringComparison.OrdinalIgnoreCase))
				shapeInRow = n;
			else if (key.Equals("c4BoundaryInRow", StringComparison.OrdinalIgnoreCase))
				boundaryInRow = n;
		}
	}

	/// <summary>Split comma-separated args respecting quotes. Includes <c>$name=value</c> tokens.</summary>
	internal static List<string> SplitArgs(string raw)
	{
		var result = new List<string>();
		var sb = new System.Text.StringBuilder();
		var inQuotes = false;

		for (var i = 0; i < raw.Length; i++)
		{
			var c = raw[i];
			if (c == '"')
			{
				if (inQuotes && i + 1 < raw.Length && raw[i + 1] == '"')
				{
					_ = sb.Append('"');
					i++;
					continue;
				}
				inQuotes = !inQuotes;
				_ = sb.Append(c);
				continue;
			}

			if (c == ',' && !inQuotes)
			{
				var part = sb.ToString().Trim();
				if (part.Length > 0)
					result.Add(part);
				_ = sb.Clear();
				continue;
			}

			_ = sb.Append(c);
		}

		var last = sb.ToString().Trim();
		if (last.Length > 0)
			result.Add(last);

		return result;
	}

	private static string Unquote(string value)
	{
		value = value.Trim();
		if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
			return value[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
		return value;
	}
}
