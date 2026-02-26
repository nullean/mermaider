using System.Text.RegularExpressions;
using Mermaid.Models;
using Mermaid.Text;

namespace Mermaid.Parsing;

internal static partial class ClassParser
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^namespace\s+(\S+)\s*\{$", RegexOptions.None, TimeoutMs)]
	private static partial Regex NamespaceStartPattern();

	[GeneratedRegex(@"^class\s+(\S+?)(?:\s*~(\w+)~)?\s*\{$", RegexOptions.None, TimeoutMs)]
	private static partial Regex ClassBlockPattern();

	[GeneratedRegex(@"^class\s+(\S+?)(?:\s*~(\w+)~)?\s*$", RegexOptions.None, TimeoutMs)]
	private static partial Regex ClassOnlyPattern();

	[GeneratedRegex(@"^class\s+(\S+?)\s*\{\s*<<(\w+)>>\s*\}$", RegexOptions.None, TimeoutMs)]
	private static partial Regex InlineAnnotationPattern();

	[GeneratedRegex(@"^<<(\w+)>>$", RegexOptions.None, TimeoutMs)]
	private static partial Regex AnnotationPattern();

	[GeneratedRegex(@"^(\S+?)\s*:\s*(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex InlineAttrPattern();

	[GeneratedRegex(@"<\|--|--|\*--|o--|-->|\.\.>|\.\.\|>", RegexOptions.None, TimeoutMs)]
	private static partial Regex ArrowCheckPattern();

	[GeneratedRegex(@"^(\S+?)\s+(?:""([^""]*?)""\s+)?(<\|--|<\|\.\.|\*--|o--|-->|--\*|--o|--\|>|\.\.>|\.\.\|>|<--|<\.\.?|--)\s+(?:""([^""]*?)""\s+)?(\S+?)(?:\s*:\s*(.+))?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex RelationshipPattern();

	[GeneratedRegex(@"^[+\-#~]", RegexOptions.None, TimeoutMs)]
	private static partial Regex VisibilityPrefixPattern();

	[GeneratedRegex(@"^(.+?)\(([^)]*)\)(?:\s*(.+))?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex MethodSignaturePattern();

	internal static ClassDiagram Parse(string[] lines)
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

	private static ClassDiagram ParseCore(string[] lines)
	{
		var classMap = new Dictionary<string, (ClassNode Node, List<ClassMember> Attrs, List<ClassMember> Methods)>();
		var relationships = new List<ClassRelationship>();
		var namespaces = new List<ClassNamespace>();

		ClassNode? currentClass = null;
		List<ClassMember>? currentAttrs = null;
		List<ClassMember>? currentMethods = null;
		var braceDepth = 0;

		string? currentNsName = null;
		List<string>? currentNsClassIds = null;

		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];

			if (currentClass != null && braceDepth > 0)
			{
				if (line == "}")
				{
					braceDepth--;
					if (braceDepth == 0)
					{
						currentClass = null;
						currentAttrs = null;
						currentMethods = null;
					}
					continue;
				}

				var annotMatch = AnnotationPattern().Match(line);
				if (annotMatch.Success)
				{
					var (node, attrs, methods) = classMap[currentClass.Id];
					classMap[currentClass.Id] = (node with { Annotation = annotMatch.Groups[1].Value }, attrs, methods);
					currentClass = classMap[currentClass.Id].Node;
					continue;
				}

				var member = ParseMember(line);
				if (member.HasValue)
				{
					if (member.Value.Member.IsMethod)
						currentMethods!.Add(member.Value.Member);
					else
						currentAttrs!.Add(member.Value.Member);
				}
				continue;
			}

			var nsMatch = NamespaceStartPattern().Match(line);
			if (nsMatch.Success)
			{
				currentNsName = nsMatch.Groups[1].Value;
				currentNsClassIds = [];
				continue;
			}

			if (line == "}" && currentNsName != null)
			{
				namespaces.Add(new ClassNamespace(currentNsName, currentNsClassIds!));
				currentNsName = null;
				currentNsClassIds = null;
				continue;
			}

			var classBlockMatch = ClassBlockPattern().Match(line);
			if (classBlockMatch.Success)
			{
				var id = classBlockMatch.Groups[1].Value;
				var generic = classBlockMatch.Groups[2].Success ? classBlockMatch.Groups[2].Value : null;
				var (node, attrs, methods) = EnsureClass(classMap, id);
				if (generic != null)
				{
					node = node with { Label = $"{id}<{generic}>" };
					classMap[id] = (node, attrs, methods);
				}
				currentClass = node;
				currentAttrs = attrs;
				currentMethods = methods;
				braceDepth = 1;
				currentNsClassIds?.Add(id);
				continue;
			}

			var classOnlyMatch = ClassOnlyPattern().Match(line);
			if (classOnlyMatch.Success)
			{
				var id = classOnlyMatch.Groups[1].Value;
				var generic = classOnlyMatch.Groups[2].Success ? classOnlyMatch.Groups[2].Value : null;
				var (node, attrs, methods) = EnsureClass(classMap, id);
				if (generic != null)
				{
					node = node with { Label = $"{id}<{generic}>" };
					classMap[id] = (node, attrs, methods);
				}
				currentNsClassIds?.Add(id);
				continue;
			}

			var inlineAnnotMatch = InlineAnnotationPattern().Match(line);
			if (inlineAnnotMatch.Success)
			{
				var (node, attrs, methods) = EnsureClass(classMap, inlineAnnotMatch.Groups[1].Value);
				classMap[node.Id] = (node with { Annotation = inlineAnnotMatch.Groups[2].Value }, attrs, methods);
				continue;
			}

			var inlineAttrMatch = InlineAttrPattern().Match(line);
			if (inlineAttrMatch.Success)
			{
				var rest = inlineAttrMatch.Groups[2].Value;
				if (!ArrowCheckPattern().IsMatch(rest))
				{
					var (node, attrs, methods) = EnsureClass(classMap, inlineAttrMatch.Groups[1].Value);
					var member = ParseMember(rest);
					if (member.HasValue)
					{
						if (member.Value.Member.IsMethod)
							methods.Add(member.Value.Member);
						else
							attrs.Add(member.Value.Member);
					}
					continue;
				}
			}

			var rel = ParseRelationship(line);
			if (rel != null)
			{
				EnsureClass(classMap, rel.From);
				EnsureClass(classMap, rel.To);
				relationships.Add(rel);
			}
		}

		var classes = classMap.Values
			.Select(v => v.Node with { Attributes = v.Attrs, Methods = v.Methods })
			.ToList();

		return new ClassDiagram { Classes = classes, Relationships = relationships, Namespaces = namespaces };
	}

	private static (ClassNode Node, List<ClassMember> Attrs, List<ClassMember> Methods) EnsureClass(
		Dictionary<string, (ClassNode Node, List<ClassMember> Attrs, List<ClassMember> Methods)> classMap,
		string id)
	{
		if (classMap.TryGetValue(id, out var existing))
			return existing;

		var node = new ClassNode { Id = id, Label = id, Attributes = [], Methods = [] };
		var attrs = new List<ClassMember>();
		var methods = new List<ClassMember>();
		classMap[id] = (node, attrs, methods);
		return (node, attrs, methods);
	}

	private static (ClassMember Member, bool IsMethod)? ParseMember(string line)
	{
		var trimmed = line.Trim().TrimEnd(';');
		if (trimmed.Length == 0) return null;

		var visibility = ClassVisibility.None;
		var rest = trimmed;
		if (VisibilityPrefixPattern().IsMatch(rest))
		{
			visibility = rest[0] switch
			{
				'+' => ClassVisibility.Public,
				'-' => ClassVisibility.Private,
				'#' => ClassVisibility.Protected,
				'~' => ClassVisibility.Package,
				_ => ClassVisibility.None,
			};
			rest = rest[1..].TrimStart();
		}

		var methodMatch = MethodSignaturePattern().Match(rest);
		if (methodMatch.Success)
		{
			var name = methodMatch.Groups[1].Value.Trim();
			var parms = methodMatch.Groups[2].Success && methodMatch.Groups[2].Value.Trim().Length > 0
				? methodMatch.Groups[2].Value.Trim()
				: null;
			var type = methodMatch.Groups[3].Success && methodMatch.Groups[3].Value.Trim().Length > 0
				? methodMatch.Groups[3].Value.Trim()
				: null;
			var isStatic = name.EndsWith('$') || rest.Contains('$');
			var isAbstract = name.EndsWith('*') || rest.Contains('*');

			return (new ClassMember(
				visibility,
				name.TrimEnd('$', '*'),
				type,
				isStatic,
				isAbstract,
				IsMethod: true,
				parms), true);
		}

		var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		string memberName;
		string? memberType = null;
		if (parts.Length >= 2)
		{
			memberType = parts[0];
			memberName = string.Join(' ', parts[1..]);
		}
		else
		{
			memberName = parts.Length > 0 ? parts[0] : rest;
		}

		var isStaticAttr = memberName.EndsWith('$');
		var isAbstractAttr = memberName.EndsWith('*');

		return (new ClassMember(
			visibility,
			memberName.TrimEnd('$', '*'),
			memberType,
			isStaticAttr,
			isAbstractAttr), false);
	}

	private static ClassRelationship? ParseRelationship(string line)
	{
		var match = RelationshipPattern().Match(line);
		if (!match.Success) return null;

		var from = match.Groups[1].Value;
		var fromCard = match.Groups[2].Success ? MultilineUtils.NormalizeBrTags(match.Groups[2].Value) : null;
		var arrow = match.Groups[3].Value.Trim();
		var toCard = match.Groups[4].Success ? MultilineUtils.NormalizeBrTags(match.Groups[4].Value) : null;
		var to = match.Groups[5].Value;
		var label = match.Groups[6].Success ? MultilineUtils.NormalizeBrTags(match.Groups[6].Value.Trim()) : null;

		var parsed = ParseArrow(arrow);
		if (parsed == null) return null;

		return new ClassRelationship(from, to, parsed.Value.Type, parsed.Value.MarkerAt, label, fromCard, toCard);
	}

	private static (ClassRelationType Type, ClassMarkerAt MarkerAt)? ParseArrow(string arrow) =>
		arrow switch
		{
			"<|--" => (ClassRelationType.Inheritance, ClassMarkerAt.From),
			"--|>" => (ClassRelationType.Inheritance, ClassMarkerAt.To),
			"<|.." => (ClassRelationType.Realization, ClassMarkerAt.From),
			"..|>" => (ClassRelationType.Realization, ClassMarkerAt.To),
			"*--" => (ClassRelationType.Composition, ClassMarkerAt.From),
			"--*" => (ClassRelationType.Composition, ClassMarkerAt.To),
			"o--" => (ClassRelationType.Aggregation, ClassMarkerAt.From),
			"--o" => (ClassRelationType.Aggregation, ClassMarkerAt.To),
			"-->" => (ClassRelationType.Association, ClassMarkerAt.To),
			"<--" => (ClassRelationType.Association, ClassMarkerAt.From),
			"..>" => (ClassRelationType.Dependency, ClassMarkerAt.To),
			"<.." => (ClassRelationType.Dependency, ClassMarkerAt.From),
			"--" => (ClassRelationType.Association, ClassMarkerAt.To),
			_ => null,
		};
}
