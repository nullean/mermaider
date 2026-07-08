using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

internal static partial class ArchitectureParser
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"^architecture(?:-beta)?\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex HeaderPattern();

	// group id(icon)[Label] [in parentId]
	[GeneratedRegex(
		@"^group\s+([A-Za-z_][\w-]*)\s*\(([^)]*)\)\s*\[([^\]]*)\](?:\s+in\s+([A-Za-z_][\w-]*))?\s*$",
		RegexOptions.None, TimeoutMs)]
	private static partial Regex GroupPattern();

	// service id(icon)[Label] [in parentId]
	[GeneratedRegex(
		@"^service\s+([A-Za-z_][\w-]*)\s*\(([^)]*)\)\s*\[([^\]]*)\](?:\s+in\s+([A-Za-z_][\w-]*))?\s*$",
		RegexOptions.None, TimeoutMs)]
	private static partial Regex ServicePattern();

	// Official: id:P -- P:id / id:P --> P:id
	// Fixture:  id:P -- id:P / id:P --> id:P
	// Optional {group} modifier after either id (ignored for endpoint resolution in v1 — id is still used)
	[GeneratedRegex(
		@"^([A-Za-z_][\w-]*)(?:\{group\})?:([TBLR])\s*(<)?--(>)?\s*(?:([TBLR]):([A-Za-z_][\w-]*)|([A-Za-z_][\w-]*):([TBLR]))(?:\{group\})?\s*$",
		RegexOptions.IgnoreCase, TimeoutMs)]
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
		var edges = new List<ArchitectureEdge>();

		for (var i = 0; i < lines.Length; i++)
		{
			var line = lines[i];
			if (i == 0 && HeaderPattern().IsMatch(line))
				continue;

			var groupMatch = GroupPattern().Match(line);
			if (groupMatch.Success)
			{
				var id = groupMatch.Groups[1].Value;
				var icon = groupMatch.Groups[2].Value.Trim();
				var label = groupMatch.Groups[3].Value.Trim();
				var parent = groupMatch.Groups[4].Success ? groupMatch.Groups[4].Value : null;
				groups.Add(new ArchitectureGroup(id, icon, label.Length > 0 ? label : id, parent));
				continue;
			}

			var serviceMatch = ServicePattern().Match(line);
			if (serviceMatch.Success)
			{
				var id = serviceMatch.Groups[1].Value;
				var icon = serviceMatch.Groups[2].Value.Trim();
				var label = serviceMatch.Groups[3].Value.Trim();
				var parent = serviceMatch.Groups[4].Success ? serviceMatch.Groups[4].Value : null;
				services.Add(new ArchitectureService(id, icon, label.Length > 0 ? label : id, parent));
				continue;
			}

			var edgeMatch = EdgePattern().Match(line);
			if (edgeMatch.Success)
			{
				var sourceId = edgeMatch.Groups[1].Value;
				var sourcePort = ParsePort(edgeMatch.Groups[2].Value);
				var arrowToSource = edgeMatch.Groups[3].Success;
				var arrowToTarget = edgeMatch.Groups[4].Success;

				string targetId;
				ArchitecturePort targetPort;
				if (edgeMatch.Groups[5].Success)
				{
					// Official: Port:id
					targetPort = ParsePort(edgeMatch.Groups[5].Value);
					targetId = edgeMatch.Groups[6].Value;
				}
				else
				{
					// Fixture: id:Port
					targetId = edgeMatch.Groups[7].Value;
					targetPort = ParsePort(edgeMatch.Groups[8].Value);
				}

				edges.Add(new ArchitectureEdge(
					sourceId, sourcePort, targetId, targetPort,
					arrowToTarget, arrowToSource));
			}
		}

		return new ArchitectureDiagram
		{
			Groups = groups,
			Services = services,
			Edges = edges,
		};
	}

	private static ArchitecturePort ParsePort(string token) =>
		char.ToUpperInvariant(token[0]) switch
		{
			'T' => ArchitecturePort.Top,
			'B' => ArchitecturePort.Bottom,
			'L' => ArchitecturePort.Left,
			'R' => ArchitecturePort.Right,
			_ => ArchitecturePort.Bottom,
		};
}
