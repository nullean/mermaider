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

	[GeneratedRegex(@"^class\s+(\S+?)(?:\s*~(\w+)~)?(?:\s*\[(?:""([^""]*)""|([^\]""]*))\])?\s*\{$", RegexOptions.None, TimeoutMs)]
	private static partial Regex ClassBlockPattern();

	[GeneratedRegex(@"^class\s+(\S+?)(?:\s*~(\w+)~)?(?:\s*\[(?:""([^""]*)""|([^\]""]*))\])?\s*$", RegexOptions.None, TimeoutMs)]
	private static partial Regex ClassOnlyPattern();

	[GeneratedRegex(@"^class\s+(\S+?)\s*\{\s*<<(\w+)>>\s*\}$", RegexOptions.None, TimeoutMs)]
	private static partial Regex InlineAnnotationPattern();

	[GeneratedRegex(@"^<<(\w+)>>$", RegexOptions.None, TimeoutMs)]
	private static partial Regex AnnotationPattern();

	[GeneratedRegex(@"^(\S+?)\s*:\s*(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex InlineAttrPattern();

	[GeneratedRegex(@"<\|--|--|\*--|o--|-->|\.\.>|\.\.\|>", RegexOptions.None, TimeoutMs)]
	private static partial Regex ArrowCheckPattern();

	[GeneratedRegex(@"^(\S+?)\s+(?:""([^""]*?)""\s+)?(<\|--|<\|\.\.|\*--|o--|-->|--\*|--o|--\|>|\.\.>|\.\.\|>|<--|<\.\.?|--|\.\.)\s+(?:""([^""]*?)""\s+)?(\S+?)(?:\s*:\s*(.+))?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex RelationshipPattern();

	[GeneratedRegex(@"^[+\-#~]", RegexOptions.None, TimeoutMs)]
	private static partial Regex VisibilityPrefixPattern();

	[GeneratedRegex(@"^(.+?)\(([^)]*)\)(?:\s*(.+))?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex MethodSignaturePattern();

	[GeneratedRegex(@"^note\s+(?:for\s+(\S+)\s+)?""([^""]+)""$", RegexOptions.None, TimeoutMs)]
	private static partial Regex NotePattern();

	[GeneratedRegex(@"^([^~\s]+)~([^~\s]+)~$", RegexOptions.None, TimeoutMs)]
	private static partial Regex GenericNamePattern();

	[GeneratedRegex(@"^(\S+?)\s+(--\(\)|\.\.?\(\)|\(\)--|\.?\(\)\.\.)\s+(\S+?)(?:\s*:\s*(.+))?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex LollipopPattern();

	// the marker at either end of a relationship line: `<|--`, `*--`, `o--`, `<--`, `()--`, `--|>`, `--*`, `--o`, `-->`, `--()`
	private const string LeftMarker = @"(?:<\||\*|o|<|>|\(\)|\|>)";
	private const string RightMarker = @"(?:\|>|\*|o|>|\(\)|<)";

	[GeneratedRegex("^" + LeftMarker + @"?(?:--|\.\.)" + RightMarker + @"?$|^" + LeftMarker + @"?---$", RegexOptions.None, TimeoutMs)]
	private static partial Regex ValidArrowPattern();

	// anything that is clearly meant as a relationship line, valid or not: made of marker and line characters only
	[GeneratedRegex(@"^[<>|*o()]*[-.]+[<>|*o()]*$", RegexOptions.None, TimeoutMs)]
	private static partial Regex ArrowLikePattern();

	[GeneratedRegex(@"""[^""]*""|\S+", RegexOptions.None, TimeoutMs)]
	private static partial Regex TokenPattern();

	[GeneratedRegex(@"^note(?:\s+for\s+(\S+?))?\s*""[^""]+""$", RegexOptions.None, TimeoutMs)]
	private static partial Regex NoteShapePattern();

	[GeneratedRegex(@"^namespace\b", RegexOptions.None, TimeoutMs)]
	private static partial Regex NamespaceKeywordPattern();

	[GeneratedRegex(@"^note(?:\s|$)", RegexOptions.None, TimeoutMs)]
	private static partial Regex NoteKeywordPattern();

	internal static ClassDiagram Parse(string[] lines, bool strict = true)
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

	private static ClassDiagram ParseCore(string[] lines, bool strict)
	{
		var classMap = new Dictionary<string, (ClassNode Node, List<ClassMember> Attrs, List<ClassMember> Methods)>();
		var relationships = new List<ClassRelationship>();
		var namespaces = new List<ClassNamespace>();
		Direction? direction = null;
		var notes = new List<ClassNote>();

		ClassNode? currentClass = null;
		List<ClassMember>? currentAttrs = null;
		List<ClassMember>? currentMethods = null;
		var braceDepth = 0;

		string? currentNsName = null;
		List<string>? currentNsClassIds = null;
		string? openLine = null;
		string? nsOpenLine = null;
		var nsStatements = 0;
		var unknownOpeners = 0;

		for (var i = 1; i < lines.Length; i++)
		{
			var line = StripTrailingComment(lines[i]);

			if (currentClass != null && braceDepth > 0)
			{
				if (strict && line != "}")
					RequireClassBodyLine(line, currentClass.Id, openLine!);

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

			if (strict && currentNsName != null && line != "}")
				RequireAllowedInNamespace(line);

			var dirMatch = DirectionPattern().Match(line);
			if (dirMatch.Success)
			{
				direction = Enum.Parse<Direction>(dirMatch.Groups[1].Value.ToUpperInvariant());
				continue;
			}

			var nsMatch = NamespaceStartPattern().Match(line);
			if (nsMatch.Success)
			{
				if (strict && currentNsName != null)
					throw new MermaidParseException("a namespace cannot be declared inside another namespace", line, "the closing '}' of the namespace first", column: 1);

				nsOpenLine = line;
				nsStatements = 0;
				currentNsName = nsMatch.Groups[1].Value;
				currentNsClassIds = [];
				continue;
			}

			if (strict && !nsMatch.Success && NamespaceKeywordPattern().IsMatch(line))
				throw new MermaidParseException("namespace declaration is malformed", line, "'namespace Name {' on one line", column: line.Length + 1);

			if (line == "}" && currentNsName == null)
			{
				if (strict && unknownOpeners == 0)
					throw new MermaidParseException("'}' closes no class or namespace", line, "a class or namespace block opened with '{'", column: 1);
				unknownOpeners--;
				continue;
			}

			if (line == "}" && currentNsName != null)
			{
				if (strict && nsStatements == 0)
					throw new MermaidParseException($"namespace '{currentNsName}' is empty", line, "at least one class inside the namespace", column: 1);

				namespaces.Add(new ClassNamespace(currentNsName, currentNsClassIds!));
				currentNsName = null;
				currentNsClassIds = null;
				continue;
			}

			if (strict && ClassKeywordPattern().IsMatch(line))
				RequireClassDeclaration(line);

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
				if (ExplicitLabel(classBlockMatch) is { } blockLabel)
				{
					node = node with { Label = blockLabel };
					classMap[id] = (node, attrs, methods);
				}
				openLine = line;
				nsStatements++;
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
				if (ExplicitLabel(classOnlyMatch) is { } onlyLabel)
				{
					node = node with { Label = onlyLabel };
					classMap[id] = (node, attrs, methods);
				}
				nsStatements++;
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
					var (node, attrs, methods) = EnsureClassRef(classMap, inlineAttrMatch.Groups[1].Value);
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

			if (strict && NoteKeywordPattern().IsMatch(line))
				RequireNote(line);

			var noteMatch = NotePattern().Match(line);
			if (noteMatch.Success)
			{
				nsStatements++;
				var targetClass = noteMatch.Groups[1].Success ? noteMatch.Groups[1].Value : null;
				var text = noteMatch.Groups[2].Value;
				notes.Add(new ClassNote(targetClass, text));
				continue;
			}

			if (strict)
				RequireValidRelationship(line);

			var lollipopMatch = LollipopPattern().Match(line);
			if (lollipopMatch.Success)
			{
				var from = EnsureClassRef(classMap, lollipopMatch.Groups[1].Value).Node.Id;
				var to = EnsureClassRef(classMap, lollipopMatch.Groups[3].Value).Node.Id;
				var label = lollipopMatch.Groups[4].Success ? lollipopMatch.Groups[4].Value.Trim() : null;
				relationships.Add(new ClassRelationship(from, to, ClassRelationType.Lollipop, ClassMarkerAt.To, label));
				continue;
			}

			var rel = ParseRelationship(line);
			if (rel != null)
			{
				var from = EnsureClassRef(classMap, rel.From).Node.Id;
				var to = EnsureClassRef(classMap, rel.To).Node.Id;
				relationships.Add(rel with { From = from, To = to });
				continue;
			}

			if (line.EndsWith('{'))
				unknownOpeners++;
		}

		if (strict)
		{
			if (currentClass != null && braceDepth > 0)
				throw new MermaidParseException($"class '{currentClass.Id}' body is never closed", openLine!, "'}' to close the class", column: openLine!.Length + 1);

			if (currentNsName != null)
				throw new MermaidParseException($"namespace '{currentNsName}' is never closed", nsOpenLine!, "'}' to close the namespace", column: nsOpenLine!.Length + 1);
		}

		var classes = classMap.Values.Select(v => v.Node with { Attributes = v.Attrs, Methods = v.Methods }).ToList();

		return new ClassDiagram { Classes = classes, Relationships = relationships, Namespaces = namespaces, Direction = direction, Notes = notes };
	}

	private static string? ExplicitLabel(Match m) =>
		m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Success ? m.Groups[4].Value.Trim() : null;

	// `A <|-- B %% note`: a comment may follow a statement, unless the `%%` sits inside quotes
	private static string StripTrailingComment(string line)
	{
		var comment = line.IndexOf("%%", StringComparison.Ordinal);
		if (comment <= 0)
			return line;

		var before = line[..comment];
		return before.Count(c => c == '"') % 2 == 0 ? before.TrimEnd() : line;
	}

	// a class body holds members and `<<annotations>>`; a brace in it means the class before was never closed, or a stray one
	private static void RequireClassBodyLine(string line, string classId, string openLine)
	{
		var brace = line.IndexOfAny(['{', '}']);
		if (brace < 0)
			return;

		if (ClassBlockPattern().IsMatch(line))
			throw new MermaidParseException($"class '{classId}' is never closed before the next class starts", openLine, $"'}}' to close the class before '{line}'", column: openLine.Length + 1);

		throw new MermaidParseException($"unexpected '{line[brace]}' inside the body of class '{classId}'", line, "a member without braces, or '}' on a line of its own to close the class", column: brace + 1);
	}

	private static bool IsArrowLike(string token)
	{
		// the halves of an arrow left on their own: `A <|`, `A * B`, `A o B`
		if (token == "o" || (token.Length > 0 && token.All(c => c is '<' or '>' or '|' or '*' or '(' or ')')))
			return true;

		if (token.Length == 0 || !ArrowLikePattern().IsMatch(token))
			return false;

		var lineChars = token.Count(c => c is '-' or '.');
		return lineChars >= 2 || token.IndexOfAny(['<', '>', '|', '*', '(', ')']) >= 0;
	}

	// statements whose quotes mean something else, and that never contain a relationship
	[GeneratedRegex(@"^(?:note|title|accTitle|accDescr|click|link|callback|style|classDef|cssClass|class)\b", RegexOptions.None, TimeoutMs)]
	private static partial Regex StatementKeywordPattern();

	[GeneratedRegex(@"^class(?:\s|$)", RegexOptions.None, TimeoutMs)]
	private static partial Regex ClassKeywordPattern();

	private static void RequireClassDeclaration(string line)
	{
		if (line.Trim() == "class")
			throw new MermaidParseException("class has no name", line, "a class name after 'class', e.g. 'class Animal'", column: line.Length + 1);

		RequireClosedGenerics(line, line);
	}

	// `List~int~` is a generic; an odd number of `~` leaves one open
	private static void RequireClosedGenerics(string text, string line)
	{
		if (text.Count(c => c == '~') % 2 != 0)
			throw new MermaidParseException("generic type is not closed", line, "a closing '~', e.g. List~int~", column: line.LastIndexOf('~') + 1);
	}

	private static void RequireNote(string line)
	{
		if (NoteShapePattern().IsMatch(line))
			return;

		var quotes = line.Count(c => c == '"');
		var problem = quotes % 2 != 0 ? "note text is not closed" : line.Contains("\"\"", StringComparison.Ordinal) ? "note has no text" : "note is malformed";
		throw new MermaidParseException(problem, line, "'note \"text\"' or 'note for ClassName \"text\"'", column: quotes % 2 != 0 ? line.LastIndexOf('"') + 1 : 1);
	}

	// inside `namespace N { ... }` only class declarations (and notes) are accepted
	private static void RequireAllowedInNamespace(string line)
	{
		var colon = FindLabelColon(line);
		var head = colon < 0 ? line : line[..colon];
		foreach (Match token in TokenPattern().Matches(head))
		{
			if (!token.Value.StartsWith('"') && IsArrowLike(token.Value))
				throw new MermaidParseException("a relationship cannot be declared inside a namespace", line, "only class declarations inside the namespace, with relationships after its closing '}'", column: token.Index + 1);
		}

		if (DirectionPattern().IsMatch(line))
			throw new MermaidParseException("'direction' cannot be set inside a namespace", line, "only class declarations inside the namespace", column: 1);

		if (InlineAttrPattern().IsMatch(line) && !line.StartsWith("class", StringComparison.Ordinal) && !line.StartsWith("note", StringComparison.Ordinal))
			throw new MermaidParseException("a member cannot be declared inside a namespace", line, "only class declarations inside the namespace, with members in the class body", column: 1);
	}

	// the first ':' outside double quotes starts a label (or a member)
	private static int FindLabelColon(string line)
	{
		var inQuotes = false;
		for (var i = 0; i < line.Length; i++)
		{
			if (line[i] == '"')
				inQuotes = !inQuotes;
			else if (line[i] == ':' && !inQuotes)
				return i;
		}
		return -1;
	}

	/// <summary>Rejects relationship lines that are shaped like one but cannot be read as one.</summary>
	private static void RequireValidRelationship(string line)
	{
		var colon = FindLabelColon(line);
		var head = colon < 0 ? line : line[..colon];
		var tokens = TokenPattern().Matches(head).Cast<Match>().ToList();
		var arrows = tokens.Where(t => !t.Value.StartsWith('"') && IsArrowLike(t.Value)).ToList();
		if (arrows.Count == 0)
		{
			// `Customer "1"` and `Student "0..*`: a class name and a cardinality, with no arrow after them
			if (colon < 0 && tokens.Count > 0 && !StatementKeywordPattern().IsMatch(line))
			{
				if (head.Count(c => c == '"') % 2 != 0)
					throw new MermaidParseException("quoted text is not closed", line, "a closing '\"'", column: head.LastIndexOf('"') + 1);

				if (tokens.Count == 2 && tokens[1].Value.StartsWith('"'))
					throw new MermaidParseException("a cardinality needs an arrow and a second class", line, "an arrow after the cardinality, e.g. 'Customer \"1\" --> \"*\" Order'", column: line.Length + 1);

				if (tokens.Count == 2 && tokens[1].Value == ".")
					throw new MermaidParseException("relationship arrow is incomplete", line, "an arrow such as '..' or '..>' and a target class", column: tokens[1].Index + 1);
			}
			return;
		}

		if (head.Count(c => c == '"') % 2 != 0)
			throw new MermaidParseException("quoted cardinality is not closed", line, "a closing '\"', e.g. \"1\" or \"0..*\"", column: head.LastIndexOf('"') + 1);

		var arrow = arrows[0];
		if (!ValidArrowPattern().IsMatch(arrow.Value))
			throw new MermaidParseException(
				$"relationship arrow '{arrow.Value}' is not valid",
				line,
				"an arrow such as '<|--', '*--', 'o--', '-->', '..>', '..|>', '--', '..' or '()--'",
				column: arrow.Index + 1
			);

		if (arrows.Count > 1)
			throw new MermaidParseException($"a second arrow '{arrows[1].Value}' follows the first", line, "one relationship per line: 'A --> B'", column: arrows[1].Index + 1);

		var k = tokens.IndexOf(arrow);
		var before = tokens.Take(k).ToList();
		var after = tokens.Skip(k + 1).ToList();
		if (before.Count == 0)
			throw new MermaidParseException($"relationship '{arrow.Value}' has no source class", line, "a class name before the arrow, e.g. 'Animal <|-- Dog'", column: 1);

		if (before[0].Value.StartsWith('"'))
			throw new MermaidParseException("a quoted cardinality comes after the class name, not before it", line, "a class name before the arrow", column: 1);

		if (before.Count(t => t.Value.StartsWith('"')) > 1)
			throw new MermaidParseException("only one cardinality can precede the arrow", line, "a class name and at most one quoted cardinality before the arrow", column: before[^1].Index + 1);

		var targetIndex = after.Count > 0 && after[0].Value.StartsWith('"') ? 1 : 0;
		if (after.Count <= targetIndex)
			throw new MermaidParseException($"relationship '{arrow.Value}' has no target class", line, "a class name after the arrow, e.g. 'Animal <|-- Dog'", column: line.Length + 1);

		var target = after[targetIndex];
		if (target.Value.StartsWith('"'))
			throw new MermaidParseException("only one cardinality can follow the arrow", line, "a class name after the arrow", column: target.Index + 1);

		var extra = after.Skip(targetIndex + 1).FirstOrDefault(t => t.Value.StartsWith('"'));
		if (extra != null)
			throw new MermaidParseException($"unexpected {extra.Value} after the target class", line, "an optional ': label' after the target class", column: extra.Index + 1);

		RequireClosedGenerics(before[0].Value, line);
		RequireClosedGenerics(target.Value, line);
	}

	private static (ClassNode Node, List<ClassMember> Attrs, List<ClassMember> Methods) EnsureClass(
		Dictionary<string, (ClassNode Node, List<ClassMember> Attrs, List<ClassMember> Methods)> classMap,
		string id
	)
	{
		if (classMap.TryGetValue(id, out var existing))
			return existing;

		var node = new ClassNode { Id = id, Label = id, Attributes = [], Methods = [] };
		var attrs = new List<ClassMember>();
		var methods = new List<ClassMember>();
		classMap[id] = (node, attrs, methods);
		return (node, attrs, methods);
	}

	/// <summary>
	/// <see cref="EnsureClass"/> for a class named where it is used, such as a relationship endpoint: <c>List~T~</c> is
	/// the class <c>List</c>, labelled <c>List&lt;T&gt;</c> unless it was declared with a label already.
	/// </summary>
	private static (ClassNode Node, List<ClassMember> Attrs, List<ClassMember> Methods) EnsureClassRef(
		Dictionary<string, (ClassNode Node, List<ClassMember> Attrs, List<ClassMember> Methods)> classMap,
		string name
	)
	{
		var generic = GenericNamePattern().Match(name);
		if (!generic.Success)
			return EnsureClass(classMap, name);

		var id = generic.Groups[1].Value;
		var (node, attrs, methods) = EnsureClass(classMap, id);
		if (node.Label == id)
		{
			node = node with { Label = $"{id}<{generic.Groups[2].Value}>" };
			classMap[id] = (node, attrs, methods);
		}
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
			var parms = methodMatch.Groups[2].Success && methodMatch.Groups[2].Value.Trim().Length > 0 ? methodMatch.Groups[2].Value.Trim() : null;
			// the classifier ($ static, * abstract) sits right after () or after the return type: `sleep()$`, `add()$ int`, `walk() bool*`
			var typeText = methodMatch.Groups[3].Success ? methodMatch.Groups[3].Value.Trim().Trim('$', '*').Trim() : "";
			var type = typeText.Length > 0 ? typeText : null;
			var isStatic = name.EndsWith('$') || rest.Contains('$');
			var isAbstract = name.EndsWith('*') || rest.Contains('*');

			return (new ClassMember(visibility, name.TrimEnd('$', '*'), type, isStatic, isAbstract, IsMethod: true, parms), true);
		}

		var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		string memberName;
		string? memberType = null;
		if (parts.Length >= 2)
		{
			memberType = parts[0];
#if NETSTANDARD2_0
			memberName = string.Join(' ', parts.Skip(1));
#else
			memberName = string.Join(' ', parts[1..]);
#endif
		}
		else
		{
			memberName = parts.Length > 0 ? parts[0] : rest;
		}

		var isStaticAttr = memberName.EndsWith('$');
		var isAbstractAttr = memberName.EndsWith('*');

		return (new ClassMember(visibility, memberName.TrimEnd('$', '*'), memberType, isStaticAttr, isAbstractAttr), false);
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

	private static (ClassRelationType Type, ClassMarkerAt MarkerAt)? ParseArrow(string arrow) => arrow switch
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
		"--" => (ClassRelationType.Link, ClassMarkerAt.To),
		".." => (ClassRelationType.DashedLink, ClassMarkerAt.To),
		_ => null,
	};
}
