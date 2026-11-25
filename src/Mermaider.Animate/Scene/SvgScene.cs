using System.Xml.Linq;

namespace Mermaider.Animate.Scene;

internal enum SceneKind
{
	Graph,
	State,
	Sequence,
	Architecture,
	Gantt,
	Timeline,
	GitGraph,
}

internal enum TargetKind
{
	Node,
	Group,
	Actor,
}

/// <summary>An animatable thing in the rendered diagram: its elements, and the box it occupies.</summary>
internal sealed class SceneTarget(string id, TargetKind kind, string label)
{
	public string Id { get; } = id;
	public TargetKind Kind { get; } = kind;
	public string Label { get; } = label;
	public List<XElement> Elements { get; } = [];
	public Box Box { get; set; } = Box.Empty;

	/// <summary>The id of the group this target sits in, when the diagram nests it.</summary>
	public string? Parent { get; set; }

	/// <summary>The 1-based place in the source, for elements addressed by number (#3).</summary>
	public int Index { get; set; }

	/// <summary>A gantt task's dates.</summary>
	public DateTime Start { get; set; }

	public DateTime End { get; set; }

	/// <summary>The part of a gantt task that grows (its bar or milestone), or of a timeline period its moment.</summary>
	public XElement? Mark { get; set; }

	/// <summary>Things shown after this target when it appears: a timeline period's events, in order.</summary>
	public List<SceneTarget> Followers { get; } = [];

	/// <summary>A line drawn when this target appears: a timeline period's drop-line.</summary>
	public SceneEdge? Drop { get; set; }

	/// <summary>A gitgraph commit's branch.</summary>
	public string? Branch { get; set; }
}

/// <summary>A connector: a graph or architecture edge, or a sequence message.</summary>
internal sealed class SceneEdge(string from, string to, XElement element, Polyline path)
{
	public string From { get; } = from;
	public string To { get; } = to;

	/// <summary>The group (or path) the edge is drawn by.</summary>
	public XElement Element { get; } = element;

	/// <summary>The stroked element inside <see cref="Element"/> (the element itself for a bare path).</summary>
	public XElement? Stroke { get; init; }

	public List<XElement> Labels { get; } = [];
	public Polyline Path { get; } = path;
	public bool Dotted { get; init; }
	public bool Thick { get; init; }
	public bool ArrowEnd { get; init; }
	public bool ArrowStart { get; init; }
	public string? Label { get; init; }

	/// <summary>Position among edges with the same endpoints (1-based), or among messages for a sequence.</summary>
	public int Index { get; set; } = 1;

	public override string ToString() => $"{From} -> {To}";
}

/// <summary>
/// The rendered diagram, read back through the <c>data-*</c> attributes every renderer writes. Animation never changes
/// how the diagram is laid out or painted: it only wraps elements and adds an overlay on top.
/// </summary>
internal sealed class SvgScene
{
	internal static readonly XNamespace Ns = "http://www.w3.org/2000/svg";

	private readonly Dictionary<string, SceneTarget> _targets = [with(StringComparer.Ordinal)];
	private readonly Dictionary<string, SceneTarget> _groups = [with(StringComparer.Ordinal)];

	private SvgScene(XDocument document, SceneKind kind, string prefix)
	{
		Prefix = prefix;
		Document = document;
		Kind = kind;
		Root = document.Root!;
		var viewBox = ((string?)Root.Attribute("viewBox") ?? "0 0 0 0").Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
		ViewBox = viewBox.Length == 4
			? new Box(Num.Parse(viewBox[0]), Num.Parse(viewBox[1]), Num.Parse(viewBox[2]), Num.Parse(viewBox[3]))
			: new Box(0, 0, 0, 0);
	}

	public XDocument Document { get; }
	public XElement Root { get; }
	public SceneKind Kind { get; }
	public Box ViewBox { get; set; }

	/// <summary>A prefix for the overlay's ids, classes and keyframes, unique per source and options so many animations share a page.</summary>
	public string Prefix { get; }

	/// <summary>The flow direction for a graph: LR, RL, TB or BT.</summary>
	public string Direction { get; init; } = "TB";

	public IReadOnlyCollection<SceneTarget> Targets => _targets.Values;
	public IReadOnlyCollection<SceneTarget> Groups => _groups.Values;
	public List<SceneEdge> Edges { get; } = [];

	/// <summary>Sequence notes in reading order, played between the messages around them.</summary>
	public List<(XElement Element, double Y)> Notes { get; } = [];

	public SceneTarget? Target(string id) => _targets.TryGetValue(id, out var t) ? t : null;

	/// <summary>A target named by its label, for elements whose source gives no id (gantt tasks, timeline periods).</summary>
	public SceneTarget? TargetByLabel(string label) =>
		_targets.Values.FirstOrDefault(t => string.Equals(t.Label, label, StringComparison.Ordinal)) ?? _targets.Values.FirstOrDefault(
			t => string.Equals(t.Label, label, StringComparison.OrdinalIgnoreCase)
		);

	/// <summary>Targets in source order, for the types whose elements are numbered.</summary>
	public List<SceneTarget> Ordered => [.. _targets.Values.Where(t => t.Index > 0).OrderBy(t => t.Index)];

	/// <summary>For a gantt chart: the vertical extent of the plot and the x of a date.</summary>
	public (double Top, double Bottom, Func<DateTime, double> X)? TimeAxis { get; private set; }

	public SceneTarget? Group(string id) => _groups.TryGetValue(id, out var t) ? t : null;

	/// <summary>Every target, group and edge element, for focus to dim what it does not name.</summary>
	public IEnumerable<XElement> AllElements =>
		_groups
			.Values
			.SelectMany(g => g.Elements)
			.Concat(_targets.Values.SelectMany(t => t.Elements))
			.Concat(Edges.Select(e => e.Element))
			.Concat(Edges.SelectMany(e => e.Labels))
			.Concat(Notes.Select(n => n.Element));

	internal static SvgScene Read(string svg, string diagramSource, string prefix)
	{
		var document = XDocument.Parse(svg, LoadOptions.PreserveWhitespace);
		var root = document.Root!;
		var kind = DetectKind(root, diagramSource);
		var scene = new SvgScene(document, kind, prefix)
		{
			Dynamic = FirstLine(diagramSource).StartsWith("C4Dynamic", StringComparison.Ordinal),
			Direction = DetectDirection(diagramSource, kind)
		};
		scene.ReadElements();
		return scene;
	}

	private static SceneKind DetectKind(XElement root, string source)
	{
		var head = FirstLine(source);
		if (head.StartsWith("sequenceDiagram", StringComparison.Ordinal))
			return SceneKind.Sequence;
		if (head.StartsWith("architecture", StringComparison.Ordinal))
			return SceneKind.Architecture;
		if (head.StartsWith("stateDiagram", StringComparison.Ordinal))
			return SceneKind.State;
		if (head.StartsWith("gantt", StringComparison.OrdinalIgnoreCase))
			return SceneKind.Gantt;
		if (head.StartsWith("timeline", StringComparison.OrdinalIgnoreCase))
			return SceneKind.Timeline;
		if (head.StartsWith("gitGraph", StringComparison.OrdinalIgnoreCase))
			return SceneKind.GitGraph;
		return root.Descendants().Any(e => HasClass(e, "message")) ? SceneKind.Sequence : SceneKind.Graph;
	}

	/// <summary>The first line that is not blank, a comment or front matter: the diagram's header.</summary>
	private static string FirstLine(string source)
	{
		var inFrontMatter = false;
		foreach (var raw in source.Split('\n'))
		{
			var line = raw.Trim();
			if (line == "---")
			{
				inFrontMatter = !inFrontMatter;
				continue;
			}
			if (inFrontMatter || line.Length == 0 || line.StartsWith("%%", StringComparison.Ordinal))
				continue;
			return line;
		}
		return "";
	}

	private static string DetectDirection(string source, SceneKind kind)
	{
		if (kind is SceneKind.Architecture or SceneKind.Timeline or SceneKind.GitGraph or SceneKind.Gantt)
			return "LR";
		foreach (var raw in source.Split('\n'))
		{
			var line = raw.Trim();
			if (line.Length == 0 || line.StartsWith("%%", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal))
				continue;
			var words = line.Split([' ', '\t', ';'], StringSplitOptions.RemoveEmptyEntries);
			if (words.Length >= 2 && words[0] is "flowchart" or "graph")
				return words[1] switch
				{
					"LR" => "LR",
					"RL" => "RL",
					"BT" => "BT",
					_ => "TB",
				};
			if (line.StartsWith("direction ", StringComparison.Ordinal))
				return line[10..].Trim() is "LR" ? "LR" : "TB";
			if (!line.StartsWith("stateDiagram", StringComparison.Ordinal))
				break;
		}
		return "TB";
	}

	internal static bool HasClass(XElement element, string cls)
	{
		var value = (string?)element.Attribute("class");
		if (value is null)
			return false;
		foreach (var part in value.Split(' '))
			if (part == cls)
				return true;
		return false;
	}

	private void ReadElements()
	{
		switch (Kind)
		{
			case SceneKind.Gantt:
				ReadGantt();
				return;
			case SceneKind.Timeline:
				ReadTimeline();
				return;
			case SceneKind.GitGraph:
				ReadGitGraph();
				return;
		}
		var edgeOrdinals = new Dictionary<(string, string), int>();
		foreach (var element in Root.Descendants().ToList())
		{
			if (element.Name.Namespace != Ns)
				continue;
			var id = (string?)element.Attribute("data-id");
			var name = element.Name.LocalName;

			if (
				name == "g" && id is not null &&
				(HasClass(element, "node") || HasClass(element, "architecture-service") || HasClass(element, "class-node") || HasClass(
					element,
					"entity"
				) || HasClass(element, "kanban-card"))
			)
				AddTarget(_targets, id, TargetKind.Node, element);
			else if (name == "g" && id is not null && HasClass(element, "actor"))
				AddTarget(_targets, id, TargetKind.Actor, element);
			else if (
				name == "g" && id is not null &&
				(HasClass(element, "subgraph") || HasClass(element, "architecture-group") || HasClass(element, "architecture-group-title") || HasClass(
					element,
					"kanban-column"
				) || HasClass(element, "c4-boundary"))
			)
				AddTarget(_groups, id, TargetKind.Group, element);
			else if (
				name == "line" && HasClass(element, "lifeline") && (string?)element.Attribute("data-actor") is { } actor && _targets.TryGetValue(
					actor,
					out var owner
				)
			)
				owner.Elements.Add(element);
			else if (
				name == "path" && (HasClass(element, "class-relationship") || HasClass(element, "c4-relation") || HasClass(element, "block-edge")) &&
				(string?)element.Attribute("data-from") is { } relFrom && (string?)element.Attribute("data-to") is { } relTo
			)
				AddEdge(
					edgeOrdinals,
					relFrom,
					relTo,
					element,
					element,
					null,
					LabelOf(element, relFrom, relTo),
					element.Attribute("marker-end") is not null,
					element.Attribute("marker-start") is not null
				);
			else if (
				name == "path" && HasClass(element, "er-relationship") && (string?)element.Attribute("data-entity1") is { } e1 &&
				(string?)element.Attribute("data-entity2") is { } e2
			)
				AddEdge(edgeOrdinals, e1, e2, element, element, null, (string?)element.Attribute("data-label"), false, false);
			else if (
				name == "path" && HasClass(element, "edge") && (string?)element.Attribute("data-from") is { } from &&
				(string?)element.Attribute("data-to") is { } to
			)
				AddEdge(
					edgeOrdinals,
					from,
					to,
					element,
					element,
					(string?)element.Attribute("data-style"),
					(string?)element.Attribute("data-label"),
					(string?)element.Attribute("data-arrow-end") != "false",
					(string?)element.Attribute("data-arrow-start") == "true"
				);
			else if (
				name == "path" && HasClass(element, "architecture-edge") && (string?)element.Attribute("data-source") is { } source &&
				(string?)element.Attribute("data-target") is { } target
			)
				AddEdge(
					edgeOrdinals,
					source,
					target,
					element,
					element,
					null,
					null,
					element.Attribute("marker-end") is not null,
					element.Attribute("marker-start") is not null
				);
			else if (
				name == "g" && HasClass(element, "message") && (string?)element.Attribute("data-from") is { } mFrom &&
				(string?)element.Attribute("data-to") is { } mTo
			)
			{
				var stroke = element.Elements().FirstOrDefault(e => e.Name.LocalName is "line" or "path");
				if (stroke is not null)
					AddEdge(
						edgeOrdinals,
						mFrom,
						mTo,
						element,
						stroke,
						(string?)element.Attribute("data-line-style") == "dashed" ? "dotted" : "solid",
						(string?)element.Attribute("data-label"),
						stroke.Attribute("marker-end") is not null,
						false
					);
				if (stroke is not null && Edges.Count > 0 && ReferenceEquals(Edges[^1].Element, element))
					Edges[^1].Labels.AddRange(element.Elements().Where(e => !ReferenceEquals(e, stroke)));
			}
			else if (name == "g" && HasClass(element, "sequence-note"))
				Notes.Add((element, BoxOf(element).Y));
		}

		// edge labels follow their edges (graph renderers write them as separate groups; ER ones carry only their text)
		foreach (var label in Root.Descendants(Ns + "g").Where(e => HasClass(e, "edge-label")))
		{
			var from = (string?)label.Attribute("data-from");
			var to = (string?)label.Attribute("data-to");
			var text = (string?)label.Attribute("data-label");
			var edge = from is not null
				? Edges.FirstOrDefault(e => e.From == from && e.To == to && e.Labels.Count == 0)
				: Edges.FirstOrDefault(e => e.Label == text && e.Labels.Count == 0);
			edge?.Labels.Add(label);
		}

		// a kanban card knows its column; trees (mindmap, treeview) are read by index and parent
		foreach (var card in _targets.Values.Where(t => t.Elements.Count > 0 && HasClass(t.Elements[0], "kanban-card")))
			card.Parent = (string?)card.Elements[0].Attribute("data-column");
		ReadTree("mindmap-node", "mindmap-link");
		ReadTree("treeview-node", null);

		// an ER relationship's cardinality marks are unclassed shapes at its ends: they belong to it, and appear with it
		var er = Edges.Where(e => SvgScene.HasClass(e.Element, "er-relationship")).ToList();
		if (er.Count > 0)
			foreach (var mark in Root.Elements().Where(e => e.Name.LocalName is "path" or "circle" && e.Attribute("class") is null).ToList())
			{
				var c = BoxOf(mark).Center;
				var owner = er.Select(e => (Edge: e, Distance: Math.Min((e.Path.Start - c).Length, (e.Path.End - c).Length))).OrderBy(p => p.Distance).First();
				if (owner.Distance < 32)
					owner.Edge.Labels.Add(mark);
			}

		// nesting: a target whose box sits inside a group's box belongs to the smallest such group
		foreach (var target in _targets.Values.Concat(_groups.Values))
		{
			SceneTarget? best = null;
			foreach (var group in _groups.Values)
			{
				if (ReferenceEquals(group, target) || !Contains(group.Box, target.Box))
					continue;
				if (best is null || group.Box.Width * group.Box.Height < best.Box.Width * best.Box.Height)
					best = group;
			}
			// only a containing group sets a parent: trees and kanban cards already know theirs
			if (best is not null)
				target.Parent = best.Id;
		}

		if (Kind == SceneKind.Sequence)
		{
			var i = 1;
			foreach (var edge in Edges)
				edge.Index = i++;
		}
	}

	private string? LabelOf(XElement edge, string from, string to) =>
		(string?)edge.Attribute("data-label") ?? Root
			.Descendants(Ns + "g")
			.Where(e => HasClass(e, "edge-label") && (string?)e.Attribute("data-from") == from && (string?)e.Attribute("data-to") == to)
			.Select(TextOf)
			.FirstOrDefault();

	/// <summary>Tree nodes become targets <c>#index</c> labelled by their text, with their parent; links join parent to child.</summary>
	private void ReadTree(string nodeClass, string? linkClass)
	{
		var nodes = Root.Descendants(Ns + "g").Where(e => HasClass(e, nodeClass)).ToList();
		foreach (var node in nodes)
		{
			var index = (int)Num.Parse((string?)node.Attribute("data-index"));
			var target = new SceneTarget($"#{index}", TargetKind.Node, (string?)node.Attribute("data-label") ?? TextOf(node))
			{
				Index = index,
				Parent = (string?)node.Attribute("data-parent") is { } parent ? $"#{parent}" : null,
			};
			target.Elements.Add(node);
			target.Box = BoxOf(node);
			_targets[target.Id] = target;
		}
		if (nodes.Count == 0)
			return;
		var links = Root
			.Descendants(Ns + "path")
			.Where(e => e.Attribute("data-from") is not null && (linkClass is null || HasClass(e, linkClass)) && !HasClass(e, "edge"))
			.ToList();
		foreach (var link in links)
		{
			var points = PathData.Flatten((string?)link.Attribute("d") ?? "");
			if (points.Count >= 2)
				Edges.Add(new SceneEdge($"#{(string?)link.Attribute("data-from")}", $"#{(string?)link.Attribute("data-to")}", link, new Polyline(points))
				{
					Stroke = link
				});
		}
		Tree = true;
	}

	/// <summary>A C4 dynamic diagram: its numbered relations play in order.</summary>
	public bool Dynamic { get; init; }

	/// <summary>Whether the diagram is a tree (mindmap, treeview): its targets carry parents and build from the root.</summary>
	public bool Tree { get; private set; }

	private static string TextOf(XElement element) => string.Join(" ", element.Descendants(Ns + "text").Select(t => t.Value.Trim()).Where(v => v.Length > 0));

	private static DateTime? Date(string? value) =>
		value is not null && DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)
			? d
			: null;

	private void ReadGantt()
	{
		var tasks = Root.Descendants(Ns + "g").Where(e => HasClass(e, "gantt-task")).ToList();
		foreach (var task in tasks)
		{
			var index = (int)Num.Parse((string?)task.Attribute("data-index"));
			var id = (string?)task.Attribute("data-id") ?? $"#{index}";
			var target = new SceneTarget(id, TargetKind.Node, TextOf(task))
			{
				Index = index,
				Start = Date((string?)task.Attribute("data-start")) ?? DateTime.MinValue,
				End = Date((string?)task.Attribute("data-end")) ?? DateTime.MinValue,
				Mark = task.Elements().FirstOrDefault(e => e.Name.LocalName is "rect" or "path" or "polygon"),
			};
			target.Elements.Add(task);
			target.Box = target.Mark is { } mark ? BoxOf(mark) : BoxOf(task);
			_targets[id] = target;
		}

		// the date axis, fitted from two bars with different starts: x = a + b * days
		var bars = _targets.Values.Where(t => t.Mark?.Name.LocalName == "rect" && !t.Box.IsEmpty).OrderBy(t => t.Start).ToList();
		if (bars.Count == 0)
			return;
		var first = bars[0];
		var other = bars.FirstOrDefault(b => b.Start != first.Start);
		var perDay = other is not null
			? (other.Box.X - first.Box.X) / (other.Start - first.Start).TotalDays
			: (first.End - first.Start).TotalDays > 0 ? first.Box.Width / (first.End - first.Start).TotalDays : 1;
		var originX = first.Box.X;
		var origin = first.Start;
		var grid = Root.Descendants(Ns + "line").Where(l => (string?)l.Attribute("x1") == (string?)l.Attribute("x2")).Select(BoxOf).ToList();
		var top = grid.Count > 0 ? grid.Min(b => b.Y) : _targets.Values.Min(t => t.Box.Y) - 12;
		var bottom = grid.Count > 0 ? grid.Max(b => b.Bottom) : _targets.Values.Max(t => t.Box.Bottom) + 12;
		TimeAxis = (top, bottom, date => originX + ((date - origin).TotalDays * perDay));
	}

	private void ReadTimeline()
	{
		foreach (var period in Root.Descendants(Ns + "g").Where(e => HasClass(e, "timeline-period")).ToList())
		{
			var index = (int)Num.Parse((string?)period.Attribute("data-index"));
			var key = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
			var target = new SceneTarget($"#{index}", TargetKind.Node, (string?)period.Attribute("data-label") ?? TextOf(period)) { Index = index };
			target.Elements.Add(period);
			target.Box = BoxOf(period);
			target.Mark = Root.Descendants(Ns + "g").FirstOrDefault(e => HasClass(e, "timeline-moment") && (string?)e.Attribute("data-period") == key);
			if (target.Mark is not null)
				target.Elements.Add(target.Mark);
			if (Root.Descendants(Ns + "line").FirstOrDefault(e => HasClass(e, "timeline-drop") && (string?)e.Attribute("data-period") == key) is { } drop)
			{
				var a = new Vec(Num.Parse((string?)drop.Attribute("x1")), Num.Parse((string?)drop.Attribute("y1")));
				var b = new Vec(Num.Parse((string?)drop.Attribute("x2")), Num.Parse((string?)drop.Attribute("y2")));
				target.Drop = new SceneEdge(target.Id, target.Id, drop, new Polyline([a, b])) { Stroke = drop, Dotted = true };
			}
			foreach (var evt in Root.Descendants(Ns + "g").Where(e => HasClass(e, "timeline-event") && (string?)e.Attribute("data-period") == key))
			{
				var follower = new SceneTarget($"#{index}.{(string?)evt.Attribute("data-index")}", TargetKind.Node, TextOf(evt));
				follower.Elements.Add(evt);
				follower.Box = BoxOf(evt);
				target.Followers.Add(follower);
			}
			_targets[target.Id] = target;
		}
	}

	private void ReadGitGraph()
	{
		var byIndex = new Dictionary<string, SceneTarget>(StringComparer.Ordinal);
		foreach (var commit in Root.Descendants(Ns + "g").Where(e => HasClass(e, "commit")).ToList())
		{
			var indexText = (string?)commit.Attribute("data-index") ?? "0";
			var index = (int)Num.Parse(indexText);
			var id = (string?)commit.Attribute("data-id") ?? $"#{index}";
			var target = new SceneTarget(id, TargetKind.Node, id) { Index = index, Branch = (string?)commit.Attribute("data-branch") };
			target.Elements.Add(commit);
			target.Box = BoxOf(commit);
			_targets[id] = target;
			byIndex[indexText] = target;
		}
		foreach (var badge in Root.Descendants(Ns + "g").Where(e => HasClass(e, "gitgraph-branch")).ToList())
			AddTarget(_groups, (string?)badge.Attribute("data-branch") ?? "", TargetKind.Group, badge);
		foreach (var link in Root.Descendants(Ns + "path").Where(e => HasClass(e, "gitgraph-link")).ToList())
		{
			if (
				!byIndex.TryGetValue((string?)link.Attribute("data-from") ?? "", out var from) ||
				!byIndex.TryGetValue((string?)link.Attribute("data-to") ?? "", out var to)
			)
				continue;
			var points = PathData.Flatten((string?)link.Attribute("d") ?? "");
			if (points.Count >= 2)
				Edges.Add(new SceneEdge(from.Id, to.Id, link, new Polyline(points)) { Stroke = link });
		}
	}

	private static bool Contains(Box outer, Box inner) =>
		!outer.IsEmpty && !inner.IsEmpty && inner.X >= outer.X - 0.5 && inner.Y >= outer.Y - 0.5 && inner.Right <= outer.Right +
			0.5 && inner.Bottom <= outer.Bottom + 0.5 && inner.Width * inner.Height < outer.Width * outer.Height;

	private static void AddTarget(Dictionary<string, SceneTarget> into, string id, TargetKind kind, XElement element)
	{
		if (!into.TryGetValue(id, out var target))
		{
			target = new SceneTarget(id, kind, (string?)element.Attribute("data-label") ?? id);
			into[id] = target;
		}
		target.Elements.Add(element);
		// an architecture group's title sits on its frame; the frame is the group's box
		if (!HasClass(element, "architecture-group-title") && !HasClass(element, "actor-bottom"))
			target.Box = target.Box.Union(BoxOf(element));
	}

	private void AddEdge(
		Dictionary<(string, string), int> ordinals,
		string from,
		string to,
		XElement element,
		XElement stroke,
		string? style,
		string? label,
		bool arrowEnd,
		bool arrowStart
	)
	{
		List<Vec> points = stroke.Name.LocalName == "line"
			? [
				new Vec(Num.Parse((string?)stroke.Attribute("x1")), Num.Parse((string?)stroke.Attribute("y1"))),
				new Vec(Num.Parse((string?)stroke.Attribute("x2")), Num.Parse((string?)stroke.Attribute("y2")))
			]
			: PathData.Flatten((string?)stroke.Attribute("d") ?? "");
		if (points.Count < 2)
			return;
		var key = (from, to);
		ordinals[key] = ordinals.TryGetValue(key, out var n) ? n + 1 : 1;
		Edges.Add(new SceneEdge(from, to, element, new Polyline(points))
		{
			Stroke = stroke,
			Dotted = style is "dotted" or "dashed",
			Thick = style == "thick",
			Label = label,
			ArrowEnd = arrowEnd,
			ArrowStart = arrowStart,
			Index = ordinals[key],
		});
	}

	/// <summary>The box a group of shapes covers: rects, circles, ellipses, lines, polygons and paths; text and icons do not count.</summary>
	internal static Box BoxOf(XElement element)
	{
		var box = Box.Empty;
		foreach (var shape in element.DescendantsAndSelf())
		{
			if (shape.Ancestors().TakeWhile(a => a != element).Any(a => a.Attribute("transform") is not null))
				continue;
			switch (shape.Name.LocalName)
			{
				case "rect":
					box = box.Union(
						new Box(
							Num.Parse((string?)shape.Attribute("x") ?? "0"),
							Num.Parse((string?)shape.Attribute("y") ?? "0"),
							Num.Parse((string?)shape.Attribute("width")),
							Num.Parse((string?)shape.Attribute("height"))
						)
					);
					break;
				case "circle":
					{
						var r = Num.Parse((string?)shape.Attribute("r"));
						box = box.Union(new Box(Num.Parse((string?)shape.Attribute("cx")) - r, Num.Parse((string?)shape.Attribute("cy")) - r, 2 * r, 2 * r));
						break;
					}
				case "ellipse":
					{
						var rx = Num.Parse((string?)shape.Attribute("rx"));
						var ry = Num.Parse((string?)shape.Attribute("ry"));
						box = box.Union(
							new Box(Num.Parse((string?)shape.Attribute("cx")) - rx, Num.Parse((string?)shape.Attribute("cy")) - ry, 2 * rx, 2 * ry)
						);
						break;
					}
				case "line":
					box = box.Include(new Vec(Num.Parse((string?)shape.Attribute("x1")), Num.Parse((string?)shape.Attribute("y1")))).Include(
						new Vec(Num.Parse((string?)shape.Attribute("x2")), Num.Parse((string?)shape.Attribute("y2")))
					);
					break;
				case "polygon":
				case "polyline":
					{
						var numbers = ((string?)shape.Attribute("points") ?? "").Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
						for (var i = 0; i + 1 < numbers.Length; i += 2)
							box = box.Include(new Vec(Num.Parse(numbers[i]), Num.Parse(numbers[i + 1])));
						break;
					}
				case "path":
					foreach (var p in PathData.Flatten((string?)shape.Attribute("d") ?? ""))
						box = box.Include(p);
					break;
			}
		}
		return box.IsEmpty || double.IsNaN(box.Width) ? Box.Empty : box;
	}

	public XElement? Marker(bool atStart)
	{
		var suffix = atStart ? "-mk-arrow-s" : "-mk-arrow";
		return Root.Descendants(Ns + "marker").FirstOrDefault(m => (string?)m.Attribute("id") is { } id && id.EndsWith(suffix, StringComparison.Ordinal));
	}
}
