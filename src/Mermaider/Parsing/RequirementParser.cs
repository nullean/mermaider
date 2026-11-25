using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

internal static partial class RequirementParser
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^title\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex TitlePattern();

	[GeneratedRegex(@"^direction\s+(TB|BT|LR|RL)\b", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex DirectionPattern();

	[GeneratedRegex(@"^(requirement|functionalRequirement|interfaceRequirement|performanceRequirement|physicalRequirement|designConstraint)\s+(.+?)\s*\{\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex RequirementOpenPattern();

	[GeneratedRegex(@"^element\s+(.+?)\s*\{\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex ElementOpenPattern();

	[GeneratedRegex(@"^(\w+)\s*:\s*(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex PropertyPattern();

	// A - type -> B
	[GeneratedRegex(@"^(.+?)\s*-\s*(contains|copies|derives|satisfies|verifies|refines|traces)\s*->\s*(.+)$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex ForwardRelationPattern();

	// B <- type - A
	[GeneratedRegex(@"^(.+?)\s*<-\s*(contains|copies|derives|satisfies|verifies|refines|traces)\s*-\s*(.+)$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex ReverseRelationPattern();

	// statements that are valid at the top level but carry nothing this parser renders
	[GeneratedRegex(@"^(?:classDef|class|style|accTitle|accDescr)\b|:::", RegexOptions.None, TimeoutMs)]
	private static partial Regex StylingPattern();

	[GeneratedRegex(@"^direction(?!\s+(?:TB|BT|LR|RL)\b)", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex DirectionKeywordPattern();

	internal static RequirementDiagram Parse(string[] lines, bool strict = true)
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

	private static RequirementDiagram ParseCore(string[] lines, bool strict)
	{
		string? title = null;
		var direction = Direction.TB;
		var requirements = new List<RequirementNode>();
		var elements = new List<RequirementElement>();
		var relations = new List<RequirementRelation>();

		for (var i = 1; i < lines.Length; i++)
		{
			var line = StripTrailingComment(lines[i]);

			var titleMatch = TitlePattern().Match(line);
			if (titleMatch.Success)
			{
				title = Unquote(titleMatch.Groups[1].Value.Trim());
				continue;
			}

			var dirMatch = DirectionPattern().Match(line);
			if (dirMatch.Success)
			{
				direction = ParseDirection(dirMatch.Groups[1].Value);
				continue;
			}

			if (strict && DirectionKeywordPattern().IsMatch(line))
				throw new MermaidParseException(
					"direction is not TB, BT, LR or RL",
					line,
					"'direction' followed by TB, BT, LR or RL",
					column: line.Length > 9 ? 11 : line.Length + 1
				);

			var reqMatch = RequirementOpenPattern().Match(line);
			if (reqMatch.Success)
			{
				var kind = ParseKind(reqMatch.Groups[1].Value);
				var name = Unquote(reqMatch.Groups[2].Value.Trim());
				i = ParseRequirementBody(lines, i + 1, line, name, kind, requirements, strict);
				continue;
			}

			var elemMatch = ElementOpenPattern().Match(line);
			if (elemMatch.Success)
			{
				var name = Unquote(elemMatch.Groups[1].Value.Trim());
				i = ParseElementBody(lines, i + 1, line, name, elements, strict);
				continue;
			}

			var fwdMatch = ForwardRelationPattern().Match(line);
			if (fwdMatch.Success)
			{
				var src = Unquote(fwdMatch.Groups[1].Value.Trim());
				var relType = ParseRelationType(fwdMatch.Groups[2].Value);
				var dst = Unquote(fwdMatch.Groups[3].Value.Trim());
				relations.Add(new RequirementRelation(src, dst, relType));
				continue;
			}

			var revMatch = ReverseRelationPattern().Match(line);
			if (revMatch.Success)
			{
				// B <- type - A  means A - type -> B
				var dst = Unquote(revMatch.Groups[1].Value.Trim());
				var relType = ParseRelationType(revMatch.Groups[2].Value);
				var src = Unquote(revMatch.Groups[3].Value.Trim());
				relations.Add(new RequirementRelation(src, dst, relType));
				continue;
			}

			if (strict)
				RequireKnownStatement(line);
		}

		return new RequirementDiagram { Title = title, Direction = direction, Requirements = requirements, Elements = elements, Relations = relations, };
	}

	private static int ParseRequirementBody(string[] lines, int start, string openLine, string name, RequirementKind kind, List<RequirementNode> requirements, bool strict)
	{
		string? id = null;
		string? text = null;
		var risk = RequirementRisk.Unspecified;
		var verify = RequirementVerifyMethod.Unspecified;

		var i = start;
		for (; i < lines.Length; i++)
		{
			var line = lines[i];
			if (line is "}")
				break;

			var prop = PropertyPattern().Match(line);
			if (strict)
				RequireProperty(line, prop, RequirementKeys, "id, text, risk or verifymethod", "requirement");
			if (!prop.Success)
				continue;

			var key = prop.Groups[1].Value;
			var value = Unquote(prop.Groups[2].Value.Trim());

			if (key.Equals("id", StringComparison.OrdinalIgnoreCase))
				id = value;
			else if (key.Equals("text", StringComparison.OrdinalIgnoreCase))
				text = value;
			else if (key.Equals("risk", StringComparison.OrdinalIgnoreCase))
				risk = ParseRisk(value);
			else if (key.Equals("verifymethod", StringComparison.OrdinalIgnoreCase) || key.Equals("verifyMethod", StringComparison.OrdinalIgnoreCase))
				verify = ParseVerifyMethod(value);
		}

		if (strict && i >= lines.Length)
			throw UnclosedBlock(openLine);

		requirements.Add(new RequirementNode(name, kind, id, text, risk, verify));
		return i;
	}

	private static int ParseElementBody(string[] lines, int start, string openLine, string name, List<RequirementElement> elements, bool strict)
	{
		string? type = null;
		string? docRef = null;

		var i = start;
		for (; i < lines.Length; i++)
		{
			var line = lines[i];
			if (line is "}")
				break;

			var prop = PropertyPattern().Match(line);
			if (strict)
				RequireProperty(line, prop, ElementKeys, "type or docref", "element");
			if (!prop.Success)
				continue;

			var key = prop.Groups[1].Value;
			var value = Unquote(prop.Groups[2].Value.Trim());

			if (key.Equals("type", StringComparison.OrdinalIgnoreCase))
				type = value;
			else if (key.Equals("docref", StringComparison.OrdinalIgnoreCase) || key.Equals("docRef", StringComparison.OrdinalIgnoreCase))
				docRef = value;
		}

		if (strict && i >= lines.Length)
			throw UnclosedBlock(openLine);

		elements.Add(new RequirementElement(name, type, docRef));
		return i;
	}

	private static readonly string[] RequirementKeys = ["id", "text", "risk", "verifymethod"];
	private static readonly string[] ElementKeys = ["type", "docref"];

	// `requirement r { %% note`: a comment may follow a statement, unless the `%%` sits inside a quoted name
	private static string StripTrailingComment(string line)
	{
		var comment = line.IndexOf("%%", StringComparison.Ordinal);
		if (comment <= 0)
			return line;

		var before = line[..comment];
		return before.Count(c => c == '"') % 2 == 0 ? before.TrimEnd() : line;
	}

	private static MermaidParseException UnclosedBlock(string openLine) =>
		new($"block '{openLine.TrimEnd('{', ' ')}' is never closed", openLine, "'}' to close the block", column: openLine.Length + 1);

	private static void RequireKnownStatement(string line)
	{
		if (StylingPattern().IsMatch(line) || TitlePattern().IsMatch(line))
			return;

		var arrow = line.IndexOf("->", StringComparison.Ordinal);
		if (arrow < 0)
			arrow = line.IndexOf("<-", StringComparison.Ordinal);
		if (arrow >= 0)
			throw new MermaidParseException(
				"relationship is incomplete or has an unknown type",
				line,
				"'<source> - <type> -> <target>' where type is contains, copies, derives, satisfies, verifies, refines or traces",
				column: arrow + 1
			);

		throw new MermaidParseException(
			$"unexpected text '{line}'",
			line,
			"a requirement or element block, a relationship such as 'a - satisfies -> b', or 'direction'",
			column: 1
		);
	}

	private static void RequireProperty(string line, Match prop, string[] keys, string keyList, string blockKind)
	{
		if (!prop.Success)
			throw new MermaidParseException(
				$"'{line}' is not a {blockKind} property",
				line,
				$"'key: value' with a key of {keyList}, or '}}' to close the block",
				column: 1
			);

		var key = prop.Groups[1].Value;
		if (Array.IndexOf(keys, key.ToLowerInvariant()) < 0)
			throw new MermaidParseException($"unknown {blockKind} property '{key}'", line, $"a key of {keyList}", column: 1);

		var value = prop.Groups[2].Value.Trim();
		var valueColumn = prop.Groups[2].Index + 1;
		if (value[0] == '"' && (value.Length < 3 || value[^1] != '"' || value.IndexOf('"', 1) != value.Length - 1))
			throw new MermaidParseException(
				$"quoted value for '{key}' is malformed",
				line,
				"a non-empty value in matching double quotes, e.g. \"some text\"",
				column: valueColumn
			);

		var lower = key.ToLowerInvariant();
		if (lower == "risk" && ParseRisk(value) == RequirementRisk.Unspecified)
			throw new MermaidParseException($"risk '{value}' is not a known level", line, "low, medium or high", column: valueColumn);

		if (lower == "verifymethod" && ParseVerifyMethod(value) == RequirementVerifyMethod.Unspecified)
			throw new MermaidParseException($"verifymethod '{value}' is not a known method", line, "analysis, demonstration, inspection or test", column: valueColumn);
	}

	private static Direction ParseDirection(string value) => value.ToUpperInvariant() switch
	{
		"TB" or "TD" => Direction.TB,
		"BT" => Direction.BT,
		"LR" => Direction.LR,
		"RL" => Direction.RL,
		_ => Direction.TB,
	};

	private static RequirementKind ParseKind(string value) => value.ToLowerInvariant() switch
	{
		"functionalrequirement" => RequirementKind.FunctionalRequirement,
		"interfacerequirement" => RequirementKind.InterfaceRequirement,
		"performancerequirement" => RequirementKind.PerformanceRequirement,
		"physicalrequirement" => RequirementKind.PhysicalRequirement,
		"designconstraint" => RequirementKind.DesignConstraint,
		_ => RequirementKind.Requirement,
	};

	private static RequirementRisk ParseRisk(string value) => value.ToLowerInvariant() switch
	{
		"low" => RequirementRisk.Low,
		"medium" => RequirementRisk.Medium,
		"high" => RequirementRisk.High,
		_ => RequirementRisk.Unspecified,
	};

	private static RequirementVerifyMethod ParseVerifyMethod(string value) => value.ToLowerInvariant() switch
	{
		"analysis" => RequirementVerifyMethod.Analysis,
		"demonstration" => RequirementVerifyMethod.Demonstration,
		"inspection" => RequirementVerifyMethod.Inspection,
		"test" => RequirementVerifyMethod.Test,
		_ => RequirementVerifyMethod.Unspecified,
	};

	private static RequirementRelationType ParseRelationType(string value) => value.ToLowerInvariant() switch
	{
		"contains" => RequirementRelationType.Contains,
		"copies" => RequirementRelationType.Copies,
		"derives" => RequirementRelationType.Derives,
		"satisfies" => RequirementRelationType.Satisfies,
		"verifies" => RequirementRelationType.Verifies,
		"refines" => RequirementRelationType.Refines,
		"traces" => RequirementRelationType.Traces,
		_ => RequirementRelationType.Traces,
	};

	private static string Unquote(string value)
	{
		if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
			return value[1..^1];
		return value;
	}
}
