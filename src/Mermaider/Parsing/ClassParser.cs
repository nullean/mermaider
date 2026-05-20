using System.Text.RegularExpressions;
using Mermaider.Models;
using Mermaider.Text;

namespace Mermaider.Parsing;

internal static partial class ClassParser
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^namespace\s+(\S+)\s*\{$", RegexOptions.None, TimeoutMs)]
	private static partial Regex NamespaceStartPattern();

	[GeneratedRegex(@"^direction\s+(TD|TB|LR|BT|RL)\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex DirectionPattern();

	[GeneratedRegex(@"^classDef\s+(\w+)\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex ClassDefPattern();

	[GeneratedRegex(@"^cssClass\s+""([^""]+)""\s+(\w+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex CssClassPattern();

	[GeneratedRegex(@"^style\s+([\w,-]+)\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex StylePattern();

	[GeneratedRegex(@"^class\s+(\S+?):::([\w][\w-]*)(?:\s*~(\w+)~)?\s*\{$", RegexOptions.None, TimeoutMs)]
	private static partial Regex ClassBlockWithStylePattern();

	[GeneratedRegex(@"^class\s+(\S+?)(?:\s*~(\w+)~)?\s*\{$", RegexOptions.None, TimeoutMs)]
	private static partial Regex ClassBlockPattern();

	[GeneratedRegex(@"^class\s+(\S+?):::([\w][\w-]*)(?:\s*~(\w+)~)?\s*$", RegexOptions.None, TimeoutMs)]
	private static partial Regex ClassOnlyWithStylePattern();

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

	[GeneratedRegex(@"^note\s+(?:for\s+(\S+)\s+)?""([^""]+)""$", RegexOptions.None, TimeoutMs)]
	private static partial Regex NotePattern();

	[GeneratedRegex(@"^(\S+?)\s+(--\(\)|\.\.?\(\)|\(\)--|\.?\(\)\.\.)\s+(\S+?)(?:\s*:\s*(.+))?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex LollipopPattern();

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
		Direction? direction = null;
		var notes = new List<ClassNote>();
		var classDefs = new Dictionary<string, IReadOnlyDictionary<string, string>>();
		var classAssignments = new Dictionary<string, string>();
		var nodeStyles = new Dictionary<string, Dictionary<string, string>>();

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

			// classDef name fill:#f9f,stroke:#333,stroke-width:4px
			var classDefMatch = ClassDefPattern().Match(line);
			if (classDefMatch.Success)
			{
				var name = classDefMatch.Groups[1].Value;
				var propsStr = classDefMatch.Groups[2].Value;
				classDefs[name] = ParseStyleProps(propsStr);
				continue;
			}

			// cssClass "ClassName" styleName
			var cssClassMatch = CssClassPattern().Match(line);
			if (cssClassMatch.Success)
			{
				var nodeIds = cssClassMatch.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries);
				var className = cssClassMatch.Groups[2].Value;
				foreach (var id in nodeIds)
					classAssignments[id] = className;
				continue;
			}

			// style NodeId fill:#f9f,stroke:#333
			var styleMatch = StylePattern().Match(line);
			if (styleMatch.Success)
			{
				var nodeIds = styleMatch.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries);
				var props = ParseStyleProps(styleMatch.Groups[2].Value);
				foreach (var id in nodeIds)
				{
					if (!nodeStyles.TryGetValue(id, out var existing))
					{
						existing = [];
						nodeStyles[id] = existing;
					}
					foreach (var kvp in props)
						existing[kvp.Key] = kvp.Value;
				}
				continue;
			}

			var dirMatch = DirectionPattern().Match(line);
			if (dirMatch.Success)
			{
				direction = Enum.Parse<Direction>(dirMatch.Groups[1].Value.ToUpperInvariant());
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

			// class Foo:::styleName { ... }
			var classBlockStyleMatch = ClassBlockWithStylePattern().Match(line);
			if (classBlockStyleMatch.Success)
			{
				var id = classBlockStyleMatch.Groups[1].Value;
				var styleName = classBlockStyleMatch.Groups[2].Value;
				var genericGroup = classBlockStyleMatch.Groups[3];
				var generic = genericGroup.Success ? genericGroup.Value : null;
				var (node, attrs, methods) = EnsureClass(classMap, id);
				if (generic != null)
				{
					node = node with { Label = $"{id}<{generic}>" };
					classMap[id] = (node, attrs, methods);
				}
				classAssignments[id] = styleName;
				currentClass = node;
				currentAttrs = attrs;
				currentMethods = methods;
				braceDepth = 1;
				currentNsClassIds?.Add(id);
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

			// class Foo:::styleName
			var classOnlyStyleMatch = ClassOnlyWithStylePattern().Match(line);
			if (classOnlyStyleMatch.Success)
			{
				var id = classOnlyStyleMatch.Groups[1].Value;
				var styleName = classOnlyStyleMatch.Groups[2].Value;
				var generic = classOnlyStyleMatch.Groups[3].Success ? classOnlyStyleMatch.Groups[3].Value : null;
				var (node, attrs, methods) = EnsureClass(classMap, id);
				if (generic != null)
				{
					node = node with { Label = $"{id}<{generic}>" };
					classMap[id] = (node, attrs, methods);
				}
				classAssignments[id] = styleName;
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

			var noteMatch = NotePattern().Match(line);
			if (noteMatch.Success)
			{
				var targetClass = noteMatch.Groups[1].Success ? noteMatch.Groups[1].Value : null;
				var text = noteMatch.Groups[2].Value;
				notes.Add(new ClassNote(targetClass, text));
				continue;
			}

			var lollipopMatch = LollipopPattern().Match(line);
			if (lollipopMatch.Success)
			{
				var from = lollipopMatch.Groups[1].Value;
				var to = lollipopMatch.Groups[3].Value;
				_ = EnsureClass(classMap, from);
				_ = EnsureClass(classMap, to);
				var label = lollipopMatch.Groups[4].Success ? lollipopMatch.Groups[4].Value.Trim() : null;
				relationships.Add(new ClassRelationship(from, to, ClassRelationType.Lollipop, ClassMarkerAt.To, label));
				continue;
			}

			var rel = ParseRelationship(line);
			if (rel != null)
			{
				_ = EnsureClass(classMap, rel.From);
				_ = EnsureClass(classMap, rel.To);
				relationships.Add(rel);
			}
		}

		var classes = classMap.Values
			.Select(v => v.Node with { Attributes = v.Attrs, Methods = v.Methods })
			.ToList();

		return new ClassDiagram
		{
			Classes = classes,
			Relationships = relationships,
			Namespaces = namespaces,
			Direction = direction,
			Notes = notes,
			ClassDefs = classDefs,
			ClassAssignments = classAssignments,
			NodeStyles = nodeStyles.ToDictionary(
				kvp => kvp.Key,
				kvp => (IReadOnlyDictionary<string, string>)kvp.Value),
		};
	}

	private static IReadOnlyDictionary<string, string> ParseStyleProps(string propsStr)
	{
		var props = new Dictionary<string, string>();
		foreach (var pair in propsStr.Split(','))
		{
			var colonIdx = pair.IndexOf(':');
			if (colonIdx <= 0)
				continue;
			var key = pair[..colonIdx].Trim();
			var val = pair[(colonIdx + 1)..].Trim();
			if (key.Length > 0 && val.Length > 0)
				props[key] = val;
		}
		return props;
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
		if (trimmed.Length == 0)
			return null;

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
		if (!match.Success)
			return null;

		var from = match.Groups[1].Value;
		var fromCard = match.Groups[2].Success ? MultilineUtils.NormalizeBrTags(match.Groups[2].Value) : null;
		var arrow = match.Groups[3].Value.Trim();
		var toCard = match.Groups[4].Success ? MultilineUtils.NormalizeBrTags(match.Groups[4].Value) : null;
		var to = match.Groups[5].Value;
		var label = match.Groups[6].Success ? MultilineUtils.NormalizeBrTags(match.Groups[6].Value.Trim()) : null;

		var parsed = ParseArrow(arrow);
		if (parsed == null)
			return null;

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
