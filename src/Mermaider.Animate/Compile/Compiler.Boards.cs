using System.Xml.Linq;
using Mermaider.Animate.Scene;
using Mermaider.Animate.Score;
using Mermaider.Animate.Timeline;

namespace Mermaider.Animate.Compile;

/// <summary>
/// Boards and trees: kanban cards move between columns, trees grow from their root, and a diagram whose relations are
/// numbered (a C4 dynamic diagram) plays them in that order.
/// </summary>
internal sealed partial class Compiler
{
	private Dictionary<string, List<SceneTarget>>? _columns;
	private readonly Dictionary<SceneTarget, Vec> _offsets = [];
	private readonly Dictionary<string, XElement> _counts = [];
	private double _cardGap = 8;
	private double _headerOffset = 44;

	/// <summary>Cards slide to the bottom of another column; the cards below them in their old column close the gap.</summary>
	private double Move(MoveStatement move, double at)
	{
		if (_scene.Group(move.Column) is not { } column || !_scene.Targets.Any(t => t.Parent == move.Column))
		{
			if (
				_scene.Groups.Count == 0 ||
				!_scene.Targets.Any(
					t => t.Parent is not null && _scene.Group(t.Parent)?.Elements.FirstOrDefault() is { } g && SvgScene.HasClass(g, "kanban-column")
				)
			)
				_diagnostics.Add(new(move.Line, "move slides kanban cards between columns."));
			else if (_scene.Group(move.Column) is null)
				_diagnostics.Add(new(move.Line, $"There is no column '{move.Column}'.{Known(_scene.Groups.Select(g => g.Id))}"));
			if (_scene.Group(move.Column) is null)
				return at;
		}
		var columns = Columns(out _);
		var end = at;
		var cards = Resolve(move.Targets);
		for (var i = 0; i < cards.Count; i++)
		{
			var card = cards[i];
			var t = at + (move.Each ? i * _spec.Stagger * 2 : 0);
			var from = columns.FirstOrDefault(c => c.Value.Contains(card)).Key;
			if (from is null || from == move.Column)
				continue;

			// where the card lands: under the last card of the new column, or under its header
			var target = columns[move.Column];
			var y = target.Count > 0 ? target.Max(c => c.Box.Bottom + Offset(c).Y) + _cardGap : _scene.Group(move.Column)!.Box.Y + _headerOffset;
			// the card keeps its inset: measured from the column it was drawn in, not the one it is in now
			var home = _scene.Group(card.Parent ?? from) ?? _scene.Group(from)!;
			var x = _scene.Group(move.Column)!.Box.X + (card.Box.X - home.Box.X);
			var landing = new Vec(x - card.Box.X, y - card.Box.Y);

			// the cards below it in the old column move up into its place
			var below = columns[from].Where(c => c.Box.Y + Offset(c).Y > card.Box.Y + Offset(card).Y).ToList();
			var lift = card.Box.Height + _cardGap;
			foreach (var other in below)
				Slide(other, Offset(other) + new Vec(0, -lift), t + (_spec.Camera * 0.3));

			// a moving card passes over the columns, so it is painted after them; a full column grows to take it
			var wrapper = Outer(card.Elements[0]);
			wrapper.Remove();
			_overlay.AddBeforeSelf(wrapper);
			Grow(move.Column, y + card.Box.Height + _cardGap, t);
			Slide(card, landing, t);
			_ = columns[from].Remove(card);
			target.Add(card);
			Recount(from, columns[from].Count, t + (_spec.Camera * 0.5));
			Recount(move.Column, target.Count, t + (_spec.Camera * 0.9));
			end = Math.Max(end, t + _spec.Camera);
		}
		return end;
	}

	private readonly Dictionary<string, double> _columnBottoms = [with(StringComparer.Ordinal)];

	/// <summary>Stretches a column's body downwards from its top so that it reaches <paramref name="bottom"/>.</summary>
	private void Grow(string columnId, double bottom, double at)
	{
		if (_scene.Group(columnId)?.Elements.FirstOrDefault() is not { } column || column.Elements(SvgScene.Ns + "rect").FirstOrDefault() is not { } body)
			return;
		var box = SvgScene.BoxOf(body);
		var current = _columnBottoms.TryGetValue(columnId, out var b) ? b : box.Bottom;
		if (bottom <= current)
			return;
		_columnBottoms[columnId] = bottom;
		var stretch = ChannelFor(Outer(body), Property.Transform, Channel.Transform(0, 0, 1, 1), ReducedMotion.Cut);
		stretch.Origin = new Vec(box.X - _stage.X, box.Y - _stage.Y);
		stretch.Animate(at, _spec.Camera, Channel.Transform(0, 0, 1, (bottom - box.Y) / box.Height), Easing.Move);
	}

	private Vec Offset(SceneTarget card) => _offsets.TryGetValue(card, out var offset) ? offset : new Vec(0, 0);

	private void Slide(SceneTarget card, Vec to, double at)
	{
		foreach (var element in card.Elements)
		{
			var movement = Movement(element, card.Box.Center);
			var scale = movement.ValueAt(at)[2];
			movement.Animate(at, _spec.Camera, Channel.Transform(to.X, to.Y, scale), Easing.Move);
		}
		_offsets[card] = to;
	}

	private Dictionary<string, List<SceneTarget>> Columns(out double gap)
	{
		if (_columns is null)
		{
			_columns = [with(StringComparer.Ordinal)];
			foreach (var group in _scene.Groups)
				_columns[group.Id] = [.. _scene.Targets.Where(t => t.Parent == group.Id).OrderBy(t => t.Box.Y)];
			// the board's own spacing: the gap between stacked cards, and where the first card sits below a header
			var stacked = _columns.Values.FirstOrDefault(c => c.Count > 1);
			if (stacked is not null)
				_cardGap = stacked[1].Box.Y - stacked[0].Box.Bottom;
			var filled = _columns.FirstOrDefault(c => c.Value.Count > 0);
			if (filled.Value is not null)
				_headerOffset = filled.Value[0].Box.Y - _scene.Group(filled.Key)!.Box.Y;
		}
		gap = _cardGap;
		return _columns;
	}

	/// <summary>A column's count in its header changes: the old number fades out as the new one fades in, in its place.</summary>
	private void Recount(string columnId, int count, double at)
	{
		if (_scene.Group(columnId) is not { } column)
			return;
		if (!_counts.TryGetValue(columnId, out var current))
		{
			var original = (string?)column.Elements[0].Attribute("data-count");
			current = _scene
				.Root
				.Descendants(SvgScene.Ns + "text")
				.FirstOrDefault(
					t => t.Value == original && (string?)t.Attribute("text-anchor") == "middle" && SvgScene.BoxOf(column.Elements[0]) is var box && Num.Parse(
						(string?)t.Attribute("x")
					) > box.X && Num.Parse((string?)t.Attribute("x")) < box.Right && Num.Parse((string?)t.Attribute("y")) < box.Y + 30
				);
			if (current is null)
				return;
		}
		var next = new XElement(current);
		next.Value = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
		next.SetAttributeValue("class", null);
		next.SetAttributeValue("opacity", "0");
		_overlay.Add(next);
		Opacity(current, ReducedMotion.Cut).Animate(at, _spec.Exit * 0.6, Channel.Value(0), Easing.Exit);
		Opacity(next, ReducedMotion.Cut, 0).Animate(at + (_spec.Exit * 0.4), _spec.Enter * 0.6, Channel.Value(1), Easing.Enter);
		_counts[columnId] = next;
	}

	// ---- trees --------------------------------------------------------------------------------------------------------

	private int TreeDepth(SceneTarget node)
	{
		var depth = 0;
		for (var parent = node.Parent; parent is not null && depth < 64; parent = _scene.Target(parent)?.Parent)
			depth++;
		return depth;
	}

	/// <summary>
	/// A tree grows from its root: a mindmap level by level, every branch of a level together; a treeview row by row, as
	/// a folder listing opens. Each node's link from its parent draws just before it appears.
	/// </summary>
	private double BuildTree(double at)
	{
		var nodes = _scene.Ordered;
		var rows = _scene.Root.Descendants().Any(e => SvgScene.HasClass(e, "treeview-node"));
		var levels = rows ? nodes.Select(n => new List<SceneTarget> { n }).ToList() : [.. nodes.GroupBy(TreeDepth).OrderBy(g => g.Key).Select(g => g.ToList())];
		var t = at;
		foreach (var level in levels)
			t = GrowLevel(level, t) - (rows ? _spec.Enter * 0.75 : _spec.Enter * 0.3);
		return t + _spec.Enter;
	}

	private double GrowLevel(List<SceneTarget> level, double at)
	{
		var end = at;
		var drawn = at;
		foreach (var link in _scene.Edges.Where(e => level.Any(n => n.Id == e.To)))
			drawn = Math.Max(drawn, Draw(link, at));
		var appear = drawn > at ? drawn - (_spec.Enter * 0.4) : at;
		for (var i = 0; i < level.Count; i++)
			end = Math.Max(end, Show(level[i], appear + (i * _spec.Stagger * 0.5)));
		return end;
	}

	/// <summary>A node's subtree grows out of it, level by level; until then it is hidden.</summary>
	private double Expand(ExpandStatement expand, double at)
	{
		if (!_scene.Tree)
		{
			_diagnostics.Add(new(expand.Line, "expand grows a mindmap's or a treeview's branches. For other diagrams use show or build."));
			return at;
		}
		var t = at;
		foreach (var root in Resolve(expand.Targets))
		{
			var level = _scene.Targets.Where(n => n.Parent == root.Id).OrderBy(n => n.Index).ToList();
			while (level.Count > 0)
			{
				t = GrowLevel(level, t) - (_spec.Enter * 0.3);
				var ids = new HashSet<string>(level.Select(n => n.Id), StringComparer.Ordinal);
				level = [.. _scene.Targets.Where(n => n.Parent is not null && ids.Contains(n.Parent)).OrderBy(n => n.Index)];
			}
		}
		return t + _spec.Enter;
	}

	// ---- numbered relations -------------------------------------------------------------------------------------------

	/// <summary>
	/// Relations play in the order their labels number them ("1. browses", "2. reads"), or in the order the diagram lists
	/// them: each draws if it has not, and a token carries its label along it.
	/// </summary>
	private double PlayRelations(PlayStatement play, double at)
	{
		static int? Number(string? label)
		{
			if (label is null)
				return null;
			var digits = new string(label.TrimStart().TakeWhile(char.IsDigit).ToArray());
			return digits.Length > 0 ? int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture) : null;
		}

		var relations = _scene.Edges.Select((e, i) => (Edge: e, Order: Number(e.Label) ?? (1000 + i))).OrderBy(r => r.Order).Select(r => r.Edge).ToList();
		var first = play.First ?? 1;
		var last = play.Last ?? relations.Count;
		if (relations.Count == 0 || first > relations.Count || last > relations.Count || first > last)
		{
			_diagnostics.Add(new(play.Line, $"The diagram has {relations.Count} relations, so #{first}..#{last} is out of range."));
			return at;
		}
		var t = at;
		for (var i = first - 1; i < last; i++)
		{
			var relation = relations[i];
			var label = relation.Label is { } l
				? l.TrimStart().TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9').TrimStart('.', ':', ')', ' ')
				: null;
			t = Send(
				relation,
				false,
				t,
				label is { Length: > 0 } ? label : null,
				null,
				null,
				Speed.Normal,
				Fate.Arrives,
				_scene.Target(relation.To)
			) + _spec.Beat;
		}
		return t;
	}
}
