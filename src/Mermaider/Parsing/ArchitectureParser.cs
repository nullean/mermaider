using System.Text.RegularExpressions;
using Mermaider.Models;
using Mermaider.Text;

namespace Mermaider.Parsing;

/// <summary>Parses <c>architecture-beta</c> diagram text into an <see cref="ArchitectureDiagram"/>.</summary>
internal static partial class ArchitectureParser
{
	private const int TimeoutMs = 2000;

	// the icon and the [title] are both optional; ids are plain words so the icon and title cannot be swallowed into the id
	[GeneratedRegex(@"^group\s+([^\s(\[\]{}]+)(?:\s*\(([^)]*)\))?(?:\s*\[([^\]]*)\])?(?:\s+in\s+(\S+))?\s*$", RegexOptions.None, TimeoutMs)]
	private static partial Regex GroupPattern();

	[GeneratedRegex(@"^service\s+([^\s(\[\]{}]+)(?:\s*\(([^)]*)\))?(?:\s*\[([^\]]*)\])?(?:\s+in\s+(\S+))?\s*$", RegexOptions.None, TimeoutMs)]
	private static partial Regex ServicePattern();

	[GeneratedRegex(@"^junction\s+(\S+?)(?:\s+in\s+(\S+))?\s*$", RegexOptions.None, TimeoutMs)]
	private static partial Regex JunctionPattern();

	// A(:side)? (--|-->|<--|<-->) (side:)?B — braces around group endpoints are stripped before matching.
	[GeneratedRegex(@"^(\S+?)(?::(L|R|T|B))?\s*(--|-->|<--|<-->)\s*(?:(L|R|T|B):)?(\S+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex EdgePattern();

	[GeneratedRegex(@"^(.+):(L|R|T|B)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex TrailingSidePattern();

	// `a :R -- L :b` is as valid as `a:R -- L:b`
	[GeneratedRegex(@"\s*:\s*", RegexOptions.None, TimeoutMs)]
	private static partial Regex ColonSpacingPattern();

	// anything shaped like an edge, valid or not, so strict mode can say what is wrong with it
	[GeneratedRegex(@"^(?<src>[^\s:]+)(?::(?<ss>[^\s<>-]*))?\s*(?<arrow><?-+>?)\s*(?:(?<ts>[^\s:]*):)?(?<tgt>\S*)(?<rest>.*)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex EdgeShapePattern();

	[GeneratedRegex(@"^(?:service|group|junction)\b", RegexOptions.None, TimeoutMs)]
	private static partial Regex DeclarationKeywordPattern();

	[GeneratedRegex(@"^(?:title\b|accTitle\b|accDescr\b)", RegexOptions.None, TimeoutMs)]
	private static partial Regex MetaPattern();

	internal static ArchitectureDiagram Parse(string[] lines, bool strict = true)
	{
		try
		{
			return ParseCore(lines, strict);
		}
		catch (RegexMatchTimeoutException ex)
		{
			throw new MermaidParseException($"Parsing timed out after {ex.MatchTimeout.TotalSeconds}s — input may contain pathological patterns.", ex);
		}
	}

	private static ArchitectureDiagram ParseCore(string[] lines, bool strict)
	{
		var groups = new List<ArchitectureGroup>();
		var services = new List<ArchitectureService>();
		var junctions = new List<ArchitectureJunction>();
		var edges = new List<ArchitectureEdge>();
		var declarations = new List<(string Kind, string Id, string? Parent, string Line)>();
		var edgeRefs = new List<(string Source, string Target, string Line)>();

		for (var i = 1; i < lines.Length; i++)
		{
			var line = StripTrailingComment(lines[i]);
			if (line.Length == 0)
				continue;

			var groupMatch = GroupPattern().Match(line);
			if (groupMatch.Success)
			{
				if (strict)
					RequireNonEmpty(line, groupMatch, "group");
				declarations.Add(("group", groupMatch.Groups[1].Value, groupMatch.Groups[4].Success ? groupMatch.Groups[4].Value : null, line));
				groups.Add(new ArchitectureGroup
				{
					Id = groupMatch.Groups[1].Value,
					Icon = groupMatch.Groups[2].Success ? groupMatch.Groups[2].Value : null,
					Title = groupMatch.Groups[3].Success ? MultilineUtils.NormalizeBrTags(groupMatch.Groups[3].Value) : "",
					ParentId = groupMatch.Groups[4].Success ? groupMatch.Groups[4].Value : null,
				});
				continue;
			}

			var serviceMatch = ServicePattern().Match(line);
			if (serviceMatch.Success)
			{
				if (strict)
					RequireNonEmpty(line, serviceMatch, "service");
				declarations.Add(("service", serviceMatch.Groups[1].Value, serviceMatch.Groups[4].Success ? serviceMatch.Groups[4].Value : null, line));
				services.Add(new ArchitectureService
				{
					Id = serviceMatch.Groups[1].Value,
					Icon = serviceMatch.Groups[2].Success ? serviceMatch.Groups[2].Value : Icons.IconRegistry.FallbackName,
					Title = serviceMatch.Groups[3].Success ? MultilineUtils.NormalizeBrTags(serviceMatch.Groups[3].Value) : "",
					GroupId = serviceMatch.Groups[4].Success ? serviceMatch.Groups[4].Value : null,
				});
				continue;
			}

			var junctionMatch = JunctionPattern().Match(line);
			if (junctionMatch.Success)
			{
				declarations.Add(("junction", junctionMatch.Groups[1].Value, junctionMatch.Groups[2].Success ? junctionMatch.Groups[2].Value : null, line));
				junctions.Add(new ArchitectureJunction
				{
					Id = junctionMatch.Groups[1].Value,
					GroupId = junctionMatch.Groups[2].Success ? junctionMatch.Groups[2].Value : null,
				});
				continue;
			}

			// Group endpoints are written as {id}; strip the braces — ids are unique across
			// groups/services/junctions so the edge grammar doesn't need to distinguish them.
			var edgeLine = ColonSpacingPattern().Replace(line.Replace("{group}", "", StringComparison.Ordinal), ":").Replace("{", "", StringComparison.Ordinal).Replace("}", "", StringComparison.Ordinal);
			var edgeMatch = EdgePattern().Match(edgeLine);
			// `a:R -- b:L` puts the target's side after its id; read it as `a:R -- L:b` rather than as an id containing a colon
			var trailingSide = edgeMatch.Success && !edgeMatch.Groups[4].Success ? TrailingSidePattern().Match(edgeMatch.Groups[5].Value) : Match.Empty;
			if (edgeMatch.Success && (!strict || (edgeMatch.Groups[2].Success && (edgeMatch.Groups[4].Success || trailingSide.Success))))
			{
				var arrow = edgeMatch.Groups[3].Value;
				var targetId = trailingSide.Success ? trailingSide.Groups[1].Value : edgeMatch.Groups[5].Value;
				edgeRefs.Add((edgeMatch.Groups[1].Value, targetId, line));
				edges.Add(new ArchitectureEdge
				{
					SourceId = edgeMatch.Groups[1].Value,
					SourceSide = ParseSide(edgeMatch.Groups[2]),
					TargetId = targetId,
					TargetSide = trailingSide.Success ? ParseSide(trailingSide.Groups[2]) : ParseSide(edgeMatch.Groups[4]),
					SourceArrow = arrow is "<--" or "<-->",
					TargetArrow = arrow is "-->" or "<-->",
				});
				continue;
			}

			if (strict)
				RequireKnownStatement(line, edgeLine);
		}

		if (strict)
			RequireConsistentIds(declarations, edgeRefs);

		return new ArchitectureDiagram { Groups = groups, Services = services, Junctions = junctions, Edges = edges, };
	}

	// `a:R -- L:b %% note`: a comment may follow a statement, unless the `%%` sits inside a [title]
	private static string StripTrailingComment(string line)
	{
		var comment = line.IndexOf("%%", StringComparison.Ordinal);
		if (comment <= 0)
			return line;

		var before = line[..comment];
		return before.Count(c => c == '[') == before.Count(c => c == ']') ? before.TrimEnd() : line;
	}

	private static void RequireNonEmpty(string line, Match match, string kind)
	{
		if (match.Groups[2].Success && match.Groups[2].Length == 0)
			throw new MermaidParseException($"{kind} has an empty icon", line, "an icon name between the parentheses, e.g. (server), or no parentheses", column: match.Groups[2].Index + 1);

		if (match.Groups[3].Success && match.Groups[3].Length == 0)
			throw new MermaidParseException($"{kind} has an empty title", line, "a title between the brackets, e.g. [Web server], or no brackets", column: match.Groups[3].Index + 1);
	}

	private static void RequireKnownStatement(string line, string edgeLine)
	{
		if (MetaPattern().IsMatch(line))
			return;

		var keyword = DeclarationKeywordPattern().Match(line);
		if (keyword.Success)
		{
			var form = keyword.Value switch
			{
				"service" => "service id(icon)[Title], optionally followed by 'in group'",
				"group" => "group id(icon)[Title], optionally followed by 'in parentGroup'",
				_ => "junction id, optionally followed by 'in group'",
			};
			var problem = line.EndsWith(" in", StringComparison.Ordinal) ? $"{keyword.Value} has 'in' but no group name" : $"{keyword.Value} declaration is malformed";
			throw new MermaidParseException(problem, line, form, column: line.Length + 1);
		}

		var shape = EdgeShapePattern().Match(edgeLine);
		if (shape.Success && shape.Groups["arrow"].Length > 0)
			RequireValidEdge(line, shape);

		throw new MermaidParseException(
			$"unexpected text '{line}'",
			line,
			"a 'group', 'service' or 'junction' declaration, or an edge such as 'a:R -- L:b'",
			column: 1
		);
	}

	private static void RequireValidEdge(string line, Match shape)
	{
		var arrow = shape.Groups["arrow"].Value;
		var arrowColumn = Math.Max(line.IndexOf(arrow, StringComparison.Ordinal), 0) + 1;
		if (arrow is not ("--" or "-->" or "<--" or "<-->"))
			throw new MermaidParseException($"edge arrow '{arrow}' is not valid", line, "'--', '-->', '<--' or '<-->'", column: arrowColumn);

		var source = shape.Groups["src"].Value;
		if (!shape.Groups["ss"].Success || shape.Groups["ss"].Length == 0)
			throw new MermaidParseException($"edge has no side on '{source}'", line, $"a side after the source, e.g. '{source}:R' (L, R, T or B)", column: arrowColumn);

		if (shape.Groups["ss"].Value is not ("L" or "R" or "T" or "B"))
			throw new MermaidParseException($"side '{shape.Groups["ss"].Value}' is not valid", line, "L, R, T or B", column: line.IndexOf(':') + 2);

		var target = shape.Groups["tgt"].Value;
		if (target.Length == 0)
			throw new MermaidParseException("edge has no target", line, "a side and a target id after the arrow, e.g. 'a:R -- L:b'", column: line.Length + 1);

		if (!shape.Groups["ts"].Success)
			throw new MermaidParseException($"edge has no side on '{target}'", line, $"a side before the target, e.g. 'L:{target}' (L, R, T or B)", column: arrowColumn + arrow.Length);

		if (shape.Groups["ts"].Value is not ("L" or "R" or "T" or "B"))
			throw new MermaidParseException($"side '{shape.Groups["ts"].Value}' is not valid", line, "L, R, T or B", column: arrowColumn + arrow.Length);

		var rest = shape.Groups["rest"].Value.Trim();
		if (rest.Length > 0)
			throw new MermaidParseException($"unexpected text '{rest}' after the edge", line, "the end of the edge", column: line.LastIndexOf(rest, StringComparison.Ordinal) + 1);
	}

	/// <summary>Ids are shared by groups, services and junctions; parents must be groups; edges join ids that exist.</summary>
	private static void RequireConsistentIds(List<(string Kind, string Id, string? Parent, string Line)> declarations, List<(string Source, string Target, string Line)> edgeRefs)
	{
		var kinds = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var (kind, id, _, line) in declarations)
		{
			if (!kinds.TryAdd(id, kind))
				throw new MermaidParseException($"{kind} id '{id}' is already used by a {kinds[id]}", line, "a unique id for every group, service and junction", column: line.IndexOf(id, StringComparison.Ordinal) + 1);
		}

		foreach (var (kind, id, parent, line) in declarations)
		{
			if (parent is null)
				continue;

			var column = line.LastIndexOf(parent, StringComparison.Ordinal) + 1;
			if (parent == id)
				throw new MermaidParseException($"{kind} '{id}' cannot be placed in itself", line, "a different group after 'in'", column: column);
			if (!kinds.TryGetValue(parent, out var parentKind))
				throw new MermaidParseException($"{kind} '{id}' is in group '{parent}', which is not declared", line, "the id of a declared group after 'in'", column: column);
			if (parentKind != "group")
				throw new MermaidParseException($"{kind} '{id}' is in '{parent}', which is a {parentKind} and not a group", line, "the id of a group after 'in'", column: column);
		}

		foreach (var (source, target, line) in edgeRefs)
		{
			foreach (var endpoint in new[] { source, target })
			{
				if (!kinds.ContainsKey(endpoint))
					throw new MermaidParseException($"edge refers to '{endpoint}', which is not declared", line, "the id of a declared group, service or junction", column: line.IndexOf(endpoint, StringComparison.Ordinal) + 1);
			}
		}
	}

	private static ArchitectureSide? ParseSide(Group group) =>
		group.Success
			? group.Value switch
			{
				"L" => ArchitectureSide.Left,
				"R" => ArchitectureSide.Right,
				"T" => ArchitectureSide.Top,
				"B" => ArchitectureSide.Bottom,
				_ => null,
			}
			: null;
}
