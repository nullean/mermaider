using System.Text.RegularExpressions;
using Mermaider.Models;
using Mermaider.Text;

namespace Mermaider.Parsing;

internal static partial class ErParser
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^(\S+?|""[^""]+"")\s*(?:\[(?:""([^""]+)""|([^\]]+))\])?\s*\{$", RegexOptions.None, TimeoutMs)]
	private static partial Regex EntityBlockPattern();

	[GeneratedRegex(@"^(\S+)\s+(\S+)(?:\s+(.+))?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex AttributePattern();

	[GeneratedRegex(@"""([^""]*)""", RegexOptions.None, TimeoutMs)]
	private static partial Regex CommentPattern();

	// `--` is an identifying relationship; `..`, `.-` and `-.` are non-identifying
	[GeneratedRegex(@"^(\S+)\s+([|o}{]+(?:--|\.\.|\.-|-\.)[|o}{]+)\s+(\S+?)(?:\s*:\s*(.+))?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex RelationshipPattern();

	[GeneratedRegex(@"^([|o}{]+)(--|\.\.|\.-|-\.)([|o}{]+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex CardinalitySplitPattern();

	// anything shaped like `A <cardinality><line><cardinality> B : label`, valid or not, so strict mode can say what is wrong with it
	[GeneratedRegex(@"^(?<a>""[^""]*""|\S+)\s+(?<card>(?=[|o}{.\-])[|o}{]*[-.]*[|o}{]*)(?:\s*(?<b>""[^""]*""|[^\s:]+))?\s*(?<rest>.*)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex RelationshipShapePattern();

	[GeneratedRegex(@"^([|o}{]*)([-.]*)([|o}{]*)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex CardinalityShapePattern();

	[GeneratedRegex(@"^(?<type>\S+)\s+(?<name>\S+)(?:\s+(?<rest>.+))?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex AttributeShapePattern();

	[GeneratedRegex(@"^(?:(?:PK|FK|UK)(?:\s*,\s*(?:PK|FK|UK))*)?$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex KeyListPattern();

	[GeneratedRegex(@"^[^\s\[]+\[(?:""[^""]+""|[^\]""]+)\]$", RegexOptions.None, TimeoutMs)]
	private static partial Regex EntityAliasPattern();

	[GeneratedRegex(@"^direction\s+(TD|TB|LR|BT|RL)\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex DirectionPattern();

	internal static ErDiagram Parse(string[] lines, bool strict = true)
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

	private static ErDiagram ParseCore(string[] lines, bool strict)
	{
		string? openLine = null;
		var entityMap = new Dictionary<string, (ErEntity Entity, List<ErAttributeInfo> Attrs)>();
		var relationships = new List<ErRelationship>();
		Direction? direction = null;
		ErEntity? currentEntity = null;
		List<ErAttributeInfo>? currentAttrs = null;

		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];

			if (currentEntity == null)
			{
				var dirMatch = DirectionPattern().Match(line);
				if (dirMatch.Success)
				{
					direction = Enum.Parse<Direction>(dirMatch.Groups[1].Value.ToUpperInvariant());
					continue;
				}
			}

			if (currentEntity != null)
			{
				if (line[0] == '}')
				{
					// `} B` and `}B` close the block and carry on with the next statement
					currentEntity = null;
					currentAttrs = null;
					line = line[1..].TrimStart();
					if (line.Length == 0)
						continue;
					if (strict && line[0] is '{' or '}')
						throw new MermaidParseException(
							$"unexpected '{line[0]}' after the closing '}}'",
							lines[i],
							"the end of the block, or a new statement",
							column: 2
						);
				}
				else
				{
					if (strict)
						RequireAttribute(line);

					var attr = ParseAttribute(line);
					if (attr != null)
						currentAttrs!.Add(attr);
					continue;
				}
			}

			var entityMatch = EntityBlockPattern().Match(line);
			if (entityMatch.Success || (line.EndsWith('{') && IsBalancedHeader(line) && !RelationshipShapePattern().IsMatch(line)))
			{
				if (strict && entityMatch.Success && entityMatch.Groups[1].Value[0] != '"' && entityMatch.Groups[1].Value.IndexOfAny(['[', ']']) >= 0)
					throw new MermaidParseException(
						"entity alias is malformed",
						line,
						"an alias in square brackets, e.g. NAME[\"Display name\"], before '{'",
						column: line.IndexOf('[') + 1
					);

				var id = entityMatch.Success ? Unquote(entityMatch.Groups[1].Value) : Unquote(line[..^1].Trim());
				var alias = entityMatch.Groups[2].Success ? entityMatch.Groups[2].Value : entityMatch.Groups[3].Success ? entityMatch.Groups[3].Value : null;
				var (entity, attrs) = EnsureEntity(entityMap, id);
				if (alias != null)
				{
					entity = entity with { Label = alias };
					entityMap[id] = (entity, attrs);
				}
				currentEntity = entity;
				currentAttrs = attrs;
				openLine = line;
				continue;
			}

			if (strict)
				RequireValidStatement(line);

			var rel = ParseRelationshipLine(line);
			if (rel != null)
			{
				_ = EnsureEntity(entityMap, rel.Entity1);
				_ = EnsureEntity(entityMap, rel.Entity2);
				relationships.Add(rel);
			}
		}

		if (strict && currentEntity != null)
			throw new MermaidParseException(
				$"entity '{currentEntity.Id}' block is never closed",
				openLine!,
				"'}' to close the entity block",
				column: openLine!.Length + 1
			);

		var entities = entityMap.Values.Select(v => v.Entity with { Attributes = v.Attrs }).ToList();

		return new ErDiagram { Entities = entities, Relationships = relationships, Direction = direction };
	}

	// both sides of a relationship are written with these, e.g. `||--o{`: exactly one of each end marker, in either direction
	private static readonly string[] ValidCardinalities = ["||", "|o", "o|", "}|", "|{", "}o", "o{"];

	private static string Unquote(string value) => value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;

	// a block header that the entity pattern does not know, such as `A "x" {`, still opens a block when its quotes and brackets are paired
	private static bool IsBalancedHeader(string line)
	{
		var open = 0;
		var close = 0;
		var quotes = 0;
		foreach (var c in line)
		{
			if (c == '[')
				open++;
			else if (c == ']')
				close++;
			else if (c == '"')
				quotes++;
		}
		return open == close && quotes % 2 == 0 && !line.EndsWith("{{", StringComparison.Ordinal) && line.Length > 1;
	}

	private static void RequireValidStatement(string line)
	{
		if (line == "}")
			throw new MermaidParseException("'}' closes no entity block", line, "an entity block opened with 'NAME {'", column: 1);

		var shape = RelationshipShapePattern().Match(line);
		if (shape.Success)
		{
			var split = CardinalityShapePattern().Match(shape.Groups["card"].Value);
			var column = shape.Groups["card"].Index + 1;
			var style = split.Groups[2].Value;
			var styleValid = style is "--" or ".." or ".-" or "-.";
			var hasMarker = split.Groups[1].Value.IndexOfAny(['|', '}', '{']) >= 0 || split.Groups[3].Value.IndexOfAny(['|', '}', '{']) >= 0;
			if (styleValid || (hasMarker && shape.Groups["card"].Value != "{"))
			{
				if (!styleValid)
					throw new MermaidParseException(
						style.Length == 0 ? "relationship has no line between its cardinalities" : $"relationship line '{style}' is not valid",
						line,
						"'--' (identifying) or '..' (non-identifying) between the cardinalities, e.g. 'A ||--o{ B : places'",
						column: column + (style.Length == 0 ? shape.Groups["card"].Length : split.Groups[2].Index)
					);

				foreach (var side in new[] { split.Groups[1], split.Groups[3] })
				{
					if (Array.IndexOf(ValidCardinalities, side.Value) < 0)
						throw new MermaidParseException(
							side.Length == 0 ? "relationship is missing a cardinality" : $"cardinality '{side.Value}' is not valid",
							line,
							"a cardinality marker: '||' (exactly one), '|o' (zero or one), '}|' (one or more) or '}o' (zero or more), mirrored as 'o|', '|{', 'o{' on the right",
							column: column + (side.Length == 0 ? split.Groups[2].Index : side.Index)
						);
				}

				RequireCompleteRelationship(line, shape);
			}
		}

		if (line.EndsWith('{') && !IsBalancedHeader(line))
			throw new MermaidParseException(
				"entity block header is malformed",
				line,
				"an entity name, optionally followed by [alias], then '{'",
				column: line.Length
			);

		var bracket = line.IndexOf('[');
		if (bracket > 0 && line.IndexOf(' ') is var space && (space < 0 || space > bracket) && !EntityAliasPattern().IsMatch(line))
			throw new MermaidParseException("entity alias is malformed", line, "an alias in square brackets, e.g. NAME[\"Display name\"]", column: bracket + 1);
	}

	private static void RequireCompleteRelationship(string line, Match shape)
	{
		var b = shape.Groups["b"];
		var cardEnd = shape.Groups["card"].Index + shape.Groups["card"].Length;
		if (!b.Success || b.Length == 0)
			throw new MermaidParseException(
				"relationship has no target entity",
				line,
				"an entity name after the cardinalities, e.g. 'A ||--o{ B : places'",
				column: cardEnd + 1
			);

		var rest = shape.Groups["rest"].Value.Trim();
		var restColumn = shape.Groups["rest"].Index + 1;
		// Mermaid itself requires the label, but Mermaider renders a relationship without one (see the optional-label spec), so it is not an error here
		if (rest.Length == 0)
			return;

		if (rest[0] != ':')
			throw new MermaidParseException(
				$"unexpected text '{rest}' after the target entity",
				line,
				"': label' after the target entity",
				column: restColumn + shape.Groups["rest"].Value.Length - shape.Groups["rest"].Value.TrimStart().Length
			);

		var label = rest[1..].Trim();
		if (label.Length == 0)
			throw new MermaidParseException("relationship label is empty", line, "label text after ':', or a quoted \"\"", column: line.Length + 1);

		if (label.Count(c => c == '"') % 2 != 0)
			throw new MermaidParseException("relationship label has an unclosed quote", line, "a closing '\"'", column: line.LastIndexOf('"') + 1);
	}

	private static void RequireAttribute(string line)
	{
		var m = AttributeShapePattern().Match(line);
		if (!m.Success)
			throw new MermaidParseException(
				$"attribute '{line}' has no name",
				line,
				"'type name', optionally followed by PK, FK or UK and a \"comment\", or '}' to close the entity",
				column: line.Length + 1
			);

		var type = m.Groups["type"];
		if (!char.IsLetter(type.Value[0]) && type.Value[0] != '_')
			throw new MermaidParseException($"attribute type '{type.Value}' is not valid", line, "a type such as 'string' or 'int'", column: type.Index + 1);

		var name = m.Groups["name"];
		if (!char.IsLetter(name.Value[0]) && name.Value[0] != '_' && name.Value[0] != '*')
			throw new MermaidParseException(
				$"attribute name '{name.Value}' is not valid",
				line,
				"a name starting with a letter or underscore",
				column: name.Index + 1
			);

		var rest = m.Groups["rest"].Success ? m.Groups["rest"].Value : "";
		var quote = rest.IndexOf('"');
		var keys = (quote < 0 ? rest : rest[..quote]).Trim();
		var restColumn = m.Groups["rest"].Index + 1;
		if (quote >= 0)
		{
			var tail = rest[quote..].Trim();
			if (tail.Length < 2 || tail[^1] != '"' || tail.IndexOf('"', 1) != tail.Length - 1)
				throw new MermaidParseException(
					tail.Count(c => c == '"') % 2 != 0 ? "attribute comment has an unclosed quote" : "attribute comment must be one quoted string at the end",
					line,
					"a single \"comment\" as the last item",
					column: restColumn + quote
				);
		}

		if (!KeyListPattern().IsMatch(keys))
			throw new MermaidParseException(
				$"'{keys}' is not a key list",
				line,
				"PK, FK or UK (comma separated, e.g. 'PK, FK'), then an optional \"comment\"",
				column: restColumn
			);
	}

	private static (ErEntity Entity, List<ErAttributeInfo> Attrs) EnsureEntity(
		Dictionary<string, (ErEntity Entity, List<ErAttributeInfo> Attrs)> entityMap,
		string id
	)
	{
		if (entityMap.TryGetValue(id, out var existing))
			return existing;

		var entity = new ErEntity { Id = id, Label = id, Attributes = [] };
		var attrs = new List<ErAttributeInfo>();
		entityMap[id] = (entity, attrs);
		return (entity, attrs);
	}

	private static ErAttributeInfo? ParseAttribute(string line)
	{
		var match = AttributePattern().Match(line);
		if (!match.Success)
			return null;

		var type = match.Groups[1].Value;
		var name = match.Groups[2].Value;
		var rest = match.Groups[3].Success ? match.Groups[3].Value.Trim() : "";

		var keys = new List<ErKeyType>();
		string? comment = null;

		var commentMatch = CommentPattern().Match(rest);
		if (commentMatch.Success)
			comment = MultilineUtils.NormalizeBrTags(commentMatch.Groups[1].Value);

		var restWithoutComment = CommentPattern().Replace(rest, "").Trim();
		foreach (var part in restWithoutComment.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			var upper = part.ToUpperInvariant();
			if (upper is "PK")
				keys.Add(ErKeyType.PK);
			else if (upper is "FK")
				keys.Add(ErKeyType.FK);
			else if (upper is "UK")
				keys.Add(ErKeyType.UK);
		}

		return new ErAttributeInfo(type, name, keys, comment);
	}

	private static ErRelationship? ParseRelationshipLine(string line)
	{
		var match = RelationshipPattern().Match(line);
		if (!match.Success)
			return null;

		var entity1 = match.Groups[1].Value;
		var cardinalityStr = match.Groups[2].Value;
		var entity2 = match.Groups[3].Value;
		var rawLabel = match.Groups[4].Success ? match.Groups[4].Value.Trim().Trim('"', '\'') : "";
		var label = rawLabel.Length > 0 ? MultilineUtils.NormalizeBrTags(rawLabel) : "";

		var lineMatch = CardinalitySplitPattern().Match(cardinalityStr);
		if (!lineMatch.Success)
			return null;

		var leftStr = lineMatch.Groups[1].Value;
		var lineStyle = lineMatch.Groups[2].Value;
		var rightStr = lineMatch.Groups[3].Value;

		var cardinality1 = ParseCardinality(leftStr);
		var cardinality2 = ParseCardinality(rightStr);
		if (cardinality1 == null || cardinality2 == null)
			return null;

		return new ErRelationship(entity1, entity2, cardinality1.Value, cardinality2.Value, label, lineStyle == "--");
	}

	private static ErCardinality? ParseCardinality(string str)
	{
		var normalized = str.Replace('}', '{');
		var sorted = new string(normalized.OrderBy(c => c).ToArray());

		return sorted switch
		{
			"||" => ErCardinality.One,
			"o|" => ErCardinality.ZeroOne,
			"{|" => ErCardinality.Many,
			"o{" => ErCardinality.ZeroMany,
			_ => null,
		};
	}
}
