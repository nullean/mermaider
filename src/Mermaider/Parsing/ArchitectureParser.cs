using System.Text.RegularExpressions;
using Mermaider.Models;
using Mermaider.Text;

namespace Mermaider.Parsing;

/// <summary>Parses <c>architecture-beta</c> diagram text into an <see cref="ArchitectureDiagram"/>.</summary>
internal static partial class ArchitectureParser
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^group\s+(\S+?)(?:\(([^)]*)\))?\s*\[([^\]]*)\](?:\s+in\s+(\S+))?\s*$", RegexOptions.None, TimeoutMs)]
	private static partial Regex GroupPattern();

	[GeneratedRegex(@"^service\s+(\S+?)(?:\(([^)]*)\))?\s*\[([^\]]*)\](?:\s+in\s+(\S+))?\s*$", RegexOptions.None, TimeoutMs)]
	private static partial Regex ServicePattern();

	[GeneratedRegex(@"^junction\s+(\S+?)(?:\s+in\s+(\S+))?\s*$", RegexOptions.None, TimeoutMs)]
	private static partial Regex JunctionPattern();

	// A(:side)? (--|-->|<--|<-->) (side:)?B — braces around group endpoints are stripped before matching.
	[GeneratedRegex(@"^(\S+?)(?::(L|R|T|B))?\s*(--|-->|<--|<-->)\s*(?:(L|R|T|B):)?(\S+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex EdgePattern();

	internal static ArchitectureDiagram Parse(string[] lines)
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

	private static ArchitectureDiagram ParseCore(string[] lines)
	{
		var groups = new List<ArchitectureGroup>();
		var services = new List<ArchitectureService>();
		var junctions = new List<ArchitectureJunction>();
		var edges = new List<ArchitectureEdge>();

		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];
			if (line.Length == 0)
				continue;

			var groupMatch = GroupPattern().Match(line);
			if (groupMatch.Success)
			{
				groups.Add(new ArchitectureGroup
				{
					Id = groupMatch.Groups[1].Value,
					Icon = groupMatch.Groups[2].Success ? groupMatch.Groups[2].Value : null,
					Title = MultilineUtils.NormalizeBrTags(groupMatch.Groups[3].Value),
					ParentId = groupMatch.Groups[4].Success ? groupMatch.Groups[4].Value : null,
				});
				continue;
			}

			var serviceMatch = ServicePattern().Match(line);
			if (serviceMatch.Success)
			{
				services.Add(new ArchitectureService
				{
					Id = serviceMatch.Groups[1].Value,
					Icon = serviceMatch.Groups[2].Success ? serviceMatch.Groups[2].Value : Icons.IconRegistry.FallbackName,
					Title = MultilineUtils.NormalizeBrTags(serviceMatch.Groups[3].Value),
					GroupId = serviceMatch.Groups[4].Success ? serviceMatch.Groups[4].Value : null,
				});
				continue;
			}

			var junctionMatch = JunctionPattern().Match(line);
			if (junctionMatch.Success)
			{
				junctions.Add(new ArchitectureJunction
				{
					Id = junctionMatch.Groups[1].Value,
					GroupId = junctionMatch.Groups[2].Success ? junctionMatch.Groups[2].Value : null,
				});
				continue;
			}

			// Group endpoints are written as {id}; strip the braces — ids are unique across
			// groups/services/junctions so the edge grammar doesn't need to distinguish them.
			var edgeLine = line.Replace("{", "", StringComparison.Ordinal).Replace("}", "", StringComparison.Ordinal);
			var edgeMatch = EdgePattern().Match(edgeLine);
			if (edgeMatch.Success)
			{
				var arrow = edgeMatch.Groups[3].Value;
				edges.Add(new ArchitectureEdge
				{
					SourceId = edgeMatch.Groups[1].Value,
					SourceSide = ParseSide(edgeMatch.Groups[2]),
					TargetId = edgeMatch.Groups[5].Value,
					TargetSide = ParseSide(edgeMatch.Groups[4]),
					SourceArrow = arrow is "<--" or "<-->",
					TargetArrow = arrow is "-->" or "<-->",
				});
			}
		}

		return new ArchitectureDiagram
		{
			Groups = groups,
			Services = services,
			Junctions = junctions,
			Edges = edges,
		};
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
