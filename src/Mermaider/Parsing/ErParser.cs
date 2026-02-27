using System.Text.RegularExpressions;
using Mermaider.Models;
using Mermaider.Text;

namespace Mermaider.Parsing;

internal static partial class ErParser
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^(\S+)\s*\{$", RegexOptions.None, TimeoutMs)]
	private static partial Regex EntityBlockPattern();

	[GeneratedRegex(@"^(\S+)\s+(\S+)(?:\s+(.+))?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex AttributePattern();

	[GeneratedRegex(@"""([^""]*)""", RegexOptions.None, TimeoutMs)]
	private static partial Regex CommentPattern();

	[GeneratedRegex(@"^(\S+)\s+([|o}{]+(?:--|\.\.)[|o}{]+)\s+(\S+)\s*:\s*(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex RelationshipPattern();

	[GeneratedRegex(@"^([|o}{]+)(--|\.\.)([|o}{]+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex CardinalitySplitPattern();

	internal static ErDiagram Parse(string[] lines)
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

	private static ErDiagram ParseCore(string[] lines)
	{
		var entityMap = new Dictionary<string, (ErEntity Entity, List<ErAttributeInfo> Attrs)>();
		var relationships = new List<ErRelationship>();
		ErEntity? currentEntity = null;
		List<ErAttributeInfo>? currentAttrs = null;

		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];

			if (currentEntity != null)
			{
				if (line == "}")
				{
					currentEntity = null;
					currentAttrs = null;
					continue;
				}

				var attr = ParseAttribute(line);
				if (attr != null)
					currentAttrs!.Add(attr);
				continue;
			}

			var entityMatch = EntityBlockPattern().Match(line);
			if (entityMatch.Success)
			{
				var id = entityMatch.Groups[1].Value;
				var (entity, attrs) = EnsureEntity(entityMap, id);
				currentEntity = entity;
				currentAttrs = attrs;
				continue;
			}

			var rel = ParseRelationshipLine(line);
			if (rel != null)
			{
				_ = EnsureEntity(entityMap, rel.Entity1);
				_ = EnsureEntity(entityMap, rel.Entity2);
				relationships.Add(rel);
			}
		}

		var entities = entityMap.Values
			.Select(v => v.Entity with { Attributes = v.Attrs })
			.ToList();

		return new ErDiagram { Entities = entities, Relationships = relationships };
	}

	private static (ErEntity Entity, List<ErAttributeInfo> Attrs) EnsureEntity(
		Dictionary<string, (ErEntity Entity, List<ErAttributeInfo> Attrs)> entityMap,
		string id)
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
		var rawLabel = match.Groups[4].Value.Trim().Trim('"', '\'');
		var label = MultilineUtils.NormalizeBrTags(rawLabel);

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
