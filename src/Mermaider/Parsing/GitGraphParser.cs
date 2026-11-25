using System.Globalization;
using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

internal static partial class GitGraphParser
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^commit(?:\s+(.+))?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex CommitPattern();

	[GeneratedRegex(@"^branch\s+""?([^""\s]+)""?(?:\s+order:\s*(\d+))?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex BranchPattern();

	[GeneratedRegex(@"^(?:checkout|switch)\s+""?([^""]+)""?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex CheckoutPattern();

	[GeneratedRegex(@"^merge\s+""?([^""]+?)""?(?:\s+(.+))?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex MergePattern();

	[GeneratedRegex(@"^cherry-pick\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex CherryPickPattern();

	[GeneratedRegex(@"id:\s*""([^""]+)""", RegexOptions.None, TimeoutMs)]
	private static partial Regex IdAttr();

	[GeneratedRegex(@"tag:\s*""([^""]+)""", RegexOptions.None, TimeoutMs)]
	private static partial Regex TagAttr();

	[GeneratedRegex(@"type:\s*(NORMAL|HIGHLIGHT|REVERSE)", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex TypeAttr();

	[GeneratedRegex(@"parent:\s*""([^""]+)""", RegexOptions.None, TimeoutMs)]
	private static partial Regex ParentAttr();

	internal static GitGraph Parse(string[] lines, bool strict = true)
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

	private static GitGraph ParseCore(string[] lines, bool strict)
	{
		var history = strict ? new History() : null;
		var actions = new List<GitAction>();
		var orientation = GitGraphOrientation.LR;

		var firstLine = lines[0];
		if (firstLine.Contains("TB:", StringComparison.OrdinalIgnoreCase))
			orientation = GitGraphOrientation.TB;
		else if (firstLine.Contains("BT:", StringComparison.OrdinalIgnoreCase))
			orientation = GitGraphOrientation.BT;

		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];

			history?.Apply(line);

			var branchMatch = BranchPattern().Match(line);
			if (branchMatch.Success)
			{
				int? order = branchMatch.Groups[2].Success ? int.Parse(branchMatch.Groups[2].Value, CultureInfo.InvariantCulture) : null;
				actions.Add(new GitBranchAction(branchMatch.Groups[1].Value, order));
				continue;
			}

			var checkoutMatch = CheckoutPattern().Match(line);
			if (checkoutMatch.Success)
			{
				actions.Add(new GitCheckoutAction(checkoutMatch.Groups[1].Value));
				continue;
			}

			var mergeMatch = MergePattern().Match(line);
			if (mergeMatch.Success)
			{
				var rest = mergeMatch.Groups[2].Success ? mergeMatch.Groups[2].Value : "";
				actions.Add(new GitMergeAction(mergeMatch.Groups[1].Value, ExtractAttr(IdAttr(), rest), ExtractAttr(TagAttr(), rest), ExtractType(rest)));
				continue;
			}

			var cherryMatch = CherryPickPattern().Match(line);
			if (cherryMatch.Success)
			{
				var rest = cherryMatch.Groups[1].Value;
				actions.Add(new GitCherryPickAction(ExtractAttr(IdAttr(), rest) ?? "", ExtractAttr(ParentAttr(), rest)));
				continue;
			}

			var commitMatch = CommitPattern().Match(line);
			if (commitMatch.Success)
			{
				var rest = commitMatch.Groups[1].Success ? commitMatch.Groups[1].Value : "";
				actions.Add(new GitCommitAction { Id = ExtractAttr(IdAttr(), rest), Tag = ExtractAttr(TagAttr(), rest), Type = ExtractType(rest), });
			}
		}

		return new GitGraph { Actions = actions, Orientation = orientation };
	}

	/// <summary>
	/// Strict-mode check of one statement against Mermaid's grammar, and of the repository history it implies
	/// (checking out, merging or cherry-picking things that do not exist), which Mermaid also refuses while parsing.
	/// </summary>
	private sealed class History
	{
		private sealed record Entry(string Branch, string[] Parents, bool IsMerge);

		private readonly Dictionary<string, Entry> _commits = [];
		private readonly Dictionary<string, string?> _heads = new() { ["main"] = null, };
		private string _current = "main";
		private int _auto;

		internal void Apply(string line)
		{
			var keyword = ReadWord(line, 0, out var pos);
			switch (keyword)
			{
				case "commit":
					{
						SkipSpaces(line, ref pos);
						if (pos < line.Length && line[pos] is '"' or '\'')
							ReadQuoted(line, ref pos);
						var attrs = ReadAttributes(line, pos, ["id", "msg", "tag", "type"]);
						AddCommit(attrs.GetValueOrDefault("id"), isMerge: false, otherHead: null);
						break;
					}
				case "branch":
					{
						var name = ReadName(line, ref pos, keyword);
						SkipSpaces(line, ref pos);
						if (pos < line.Length)
						{
							var attr = ReadWord(line, pos, out var after);
							if (attr != "order" || after >= line.Length || line[after] != ':')
								throw Unexpected(line, pos, "'order: <number>' or the end of the line");
							after++;
							SkipSpaces(line, ref after);
							var digits = after;
							while (digits < line.Length && char.IsAsciiDigit(line[digits]))
								digits++;
							if (digits == after)
								throw new MermaidParseException("'order:' has no number", line, "a whole number after 'order:'", column: after + 1);
							if (digits < line.Length)
								throw Unexpected(line, digits, "the end of the line");
						}
						if (_heads.ContainsKey(name))
							throw new MermaidParseException($"branch '{name}' already exists", line, "a new branch name", column: 1);
						_heads[name] = _heads[_current];
						_current = name;
						break;
					}
				case "checkout":
				case "switch":
					{
						var name = ReadName(line, ref pos, keyword);
						SkipSpaces(line, ref pos);
						if (pos < line.Length)
							throw Unexpected(line, pos, "the end of the line");
						if (!_heads.ContainsKey(name))
							throw new MermaidParseException($"cannot {keyword} branch '{name}': it has not been created", line, $"an existing branch, or 'branch {name}' first", column: line.IndexOf(name, StringComparison.Ordinal) + 1);
						_current = name;
						break;
					}
				case "merge":
					{
						var name = ReadName(line, ref pos, keyword);
						var attrs = ReadAttributes(line, pos, ["id", "tag", "type"]);
						Merge(line, name, attrs.GetValueOrDefault("id"));
						break;
					}
				case "cherry-pick":
					{
						var attrs = ReadAttributes(line, pos, ["id", "tag", "parent"]);
						CherryPick(line, attrs.GetValueOrDefault("id"), attrs.GetValueOrDefault("parent"));
						break;
					}
				case "title":
				case "accTitle":
				case "accDescr":
					break;
				default:
					throw new MermaidParseException(
						$"unrecognized statement '{(keyword.Length > 0 ? keyword : line)}'",
						line,
						"'commit', 'branch', 'checkout', 'switch', 'merge' or 'cherry-pick'",
						column: 1
					);
			}
		}

		private string NewId() => "\0auto" + _auto++.ToString(CultureInfo.InvariantCulture);

		private void AddCommit(string? customId, bool isMerge, string? otherHead)
		{
			var id = customId is { Length: > 0 } ? customId : NewId();
			var head = _heads[_current];
			var parents = head is null ? [] : otherHead is null ? new[] { head } : [head, otherHead];
			_commits[id] = new Entry(_current, parents, isMerge);
			_heads[_current] = id;
		}

		private void Merge(string line, string other, string? customId)
		{
			var head = _heads[_current];
			if (other == _current)
				throw new MermaidParseException("cannot merge a branch into itself", line, "a different branch to merge", column: line.IndexOf(other, StringComparison.Ordinal) + 1);
			if (head is null)
				throw new MermaidParseException($"cannot merge into branch '{_current}': it has no commits", line, "a commit on the current branch before 'merge'", column: 1);
			if (!_heads.TryGetValue(other, out var otherHead))
				throw new MermaidParseException($"cannot merge branch '{other}': it does not exist", line, "an existing branch", column: line.IndexOf(other, StringComparison.Ordinal) + 1);
			if (otherHead is null)
				throw new MermaidParseException($"cannot merge branch '{other}': it has no commits", line, "a branch that has commits", column: line.IndexOf(other, StringComparison.Ordinal) + 1);
			if (otherHead == head)
				throw new MermaidParseException($"cannot merge branch '{other}' into '{_current}': both are at the same commit", line, "a branch with commits the current branch lacks", column: 1);
			if (customId is { Length: > 0 } && _commits.ContainsKey(customId))
				throw new MermaidParseException($"a commit with id '{customId}' already exists", line, "a unique merge id", column: line.IndexOf(customId, StringComparison.Ordinal) + 1);
			AddCommit(customId, isMerge: true, otherHead);
		}

		private void CherryPick(string line, string? id, string? parent)
		{
			if (id is not { Length: > 0 } || !_commits.TryGetValue(id, out var source))
				throw new MermaidParseException(
					id is not { Length: > 0 } ? "cherry-pick has no source commit id" : $"cherry-pick source commit '{id}' does not exist",
					line,
					"'cherry-pick id: \"<id of an existing commit>\"'",
					column: id is null ? line.Length + 1 : line.IndexOf(id, StringComparison.Ordinal) + 1
				);
			if (source.IsMerge && string.IsNullOrEmpty(parent))
				throw new MermaidParseException($"commit '{id}' is a merge commit: cherry-pick needs 'parent: \"<id>\"'", line, "a parent commit id", column: 1);
			if (!string.IsNullOrEmpty(parent) && !source.Parents.Contains(parent))
				throw new MermaidParseException($"'{parent}' is not an immediate parent of commit '{id}'", line, "one of the commit's parents", column: line.IndexOf(parent, StringComparison.Ordinal) + 1);
			if (source.Branch == _current)
				throw new MermaidParseException($"commit '{id}' is already on branch '{_current}'", line, "a commit from another branch", column: 1);
			if (_heads[_current] is null)
				throw new MermaidParseException($"cannot cherry-pick onto branch '{_current}': it has no commits", line, "a commit on the current branch first", column: 1);
			AddCommit(null, isMerge: false, otherHead: null);
		}

		private static string ReadWord(string line, int start, out int end)
		{
			end = start;
			while (end < line.Length && (char.IsAsciiLetter(line[end]) || line[end] == '-'))
				end++;
			return line[start..end];
		}

		private static void SkipSpaces(string line, ref int pos)
		{
			while (pos < line.Length && char.IsWhiteSpace(line[pos]))
				pos++;
		}

		private static string ReadQuoted(string line, ref int pos)
		{
			var quote = line[pos];
			var close = line.IndexOf(quote, pos + 1);
			if (close < 0)
				throw new MermaidParseException("string is never closed", line, $"a closing {quote}", column: pos + 1);
			var text = line[(pos + 1)..close];
			pos = close + 1;
			return text;
		}

		private static string ReadName(string line, ref int pos, string keyword)
		{
			var wordEnd = pos;
			if (wordEnd >= line.Length || !char.IsWhiteSpace(line[wordEnd]))
				throw new MermaidParseException($"'{keyword}' has no branch name", line, "a branch name", column: line.Length + 1);
			SkipSpaces(line, ref pos);
			if (pos >= line.Length)
				throw new MermaidParseException($"'{keyword}' has no branch name", line, "a branch name", column: line.Length + 1);
			if (line[pos] is '"' or '\'')
				return ReadQuoted(line, ref pos);
			var start = pos;
			while (pos < line.Length && !char.IsWhiteSpace(line[pos]) && line[pos] is not ('"' or '\''))
				pos++;
			return line[start..pos];
		}

		private static Dictionary<string, string> ReadAttributes(string line, int pos, string[] allowed)
		{
			var values = new Dictionary<string, string>(StringComparer.Ordinal);
			while (true)
			{
				SkipSpaces(line, ref pos);
				if (pos >= line.Length)
					return values;

				var wordStart = pos;
				var word = ReadWord(line, pos, out pos);
				if (word.Length == 0 || pos >= line.Length || line[pos] != ':' || Array.IndexOf(allowed, word) < 0)
					throw Unexpected(line, wordStart, $"{string.Join(", ", allowed.Select(a => a + ":"))} or the end of the line");

				pos++;
				SkipSpaces(line, ref pos);
				if (word == "type")
				{
					var kind = ReadWord(line, pos, out var kindEnd);
					if (kind is not ("NORMAL" or "REVERSE" or "HIGHLIGHT"))
						throw new MermaidParseException(
							kind.Length == 0 ? "'type:' has no value" : $"'{kind}' is not a commit type",
							line,
							"NORMAL, REVERSE or HIGHLIGHT",
							column: pos + 1
						);
					values[word] = kind;
					pos = kindEnd;
					continue;
				}

				if (pos >= line.Length || line[pos] is not ('"' or '\''))
					throw new MermaidParseException($"'{word}:' has no quoted value", line, $"a quoted string after '{word}:'", column: pos + 1);
				values[word] = ReadQuoted(line, ref pos);
			}
		}

		private static MermaidParseException Unexpected(string line, int column, string expected)
		{
			var end = column;
			while (end < line.Length && !char.IsWhiteSpace(line[end]))
				end++;
			return new MermaidParseException($"unexpected text '{line[column..end]}'", line, expected, column: column + 1);
		}
	}

	private static string? ExtractAttr(Regex pattern, string text)
	{
		var match = pattern.Match(text);
		return match.Success ? match.Groups[1].Value : null;
	}

	private static GitCommitType ExtractType(string text)
	{
		var match = TypeAttr().Match(text);
		if (!match.Success)
			return GitCommitType.Normal;
		return match.Groups[1].Value.ToUpperInvariant() switch
		{
			"HIGHLIGHT" => GitCommitType.Highlight,
			"REVERSE" => GitCommitType.Reverse,
			_ => GitCommitType.Normal,
		};
	}
}
