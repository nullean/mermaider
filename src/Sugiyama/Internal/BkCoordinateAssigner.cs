namespace Sugiyama.Internal;

/// <summary>
/// Brandes–Köpf BALANCED horizontal coordinate assignment, port-aware.
///
/// Reference: U. Brandes and B. Köpf, "Fast and Simple Horizontal Coordinate Assignment",
/// Graph Drawing (GD 2001), LNCS 2265, pp. 31–44.
///
/// Four alignment+compaction passes (TL, TR, BL, BR), aligned to the smallest layout and combined
/// by averaging the two median candidates. Edge ends are not at node centres: a node's incident
/// edges on one side get evenly distributed ports (the i-th of k at width·(i+1)/(k+1)), ordered by
/// neighbour position, and blocks align connected PORTS (nodes in a block get an "inner shift").
///
/// Gaps that carry edge labels get one dummy per edge (width = label extent), exactly like a long-edge
/// dummy, so edges run port → column → port. Passes are computed as a left-compaction over a mirrored
/// "view" for the rightward passes.
/// </summary>
internal static class BkCoordinateAssigner
{
	private sealed class Lg
	{
		internal int N;
		internal int OrigN;
		internal double[] W = [];
		internal bool[] Virt = [];
		internal int[] Layer = [];
		internal int[][] LayerNodes = [];
		internal int[] Pos = [];
		internal List<(int From, int To, int Orig)> Edges = [];
		internal double[] OutOff = [];
		internal double[] InOff = [];
		internal List<int>[] InNb = [];
		internal List<int>[] OutNb = [];
		internal int[] DummyOfGraphEdge = [];
		internal int[] FirstHalf = [];
		internal int[] SecondHalf = [];
		internal HashSet<int> NoAlign = [];
	}

	internal static void Run(GraphBuffer graph, double nodeSpacing)
	{
		var lg = Build(graph);
		ComputePortOffsets(lg);

		var n = lg.N;
		var xLayouts = new double[4][];
		for (var k = 0; k < 4; k++)
			xLayouts[k] = RunPass(lg, k < 2, (k % 2) == 0, nodeSpacing);

		var mins = new double[4];
		var maxs = new double[4];
		var smallest = 0;
		for (var k = 0; k < 4; k++)
		{
			var lo = double.MaxValue;
			var hi = double.MinValue;
			for (var i = 0; i < n; i++)
			{
				lo = Math.Min(lo, xLayouts[k][i]);
				hi = Math.Max(hi, xLayouts[k][i] + lg.W[i]);
			}
			mins[k] = lo;
			maxs[k] = hi;
			if (hi - lo < maxs[smallest] - mins[smallest])
				smallest = k;
		}

		for (var k = 0; k < 4; k++)
		{
			var shift = (k % 2) == 0 ? mins[smallest] - mins[k] : maxs[smallest] - maxs[k];
			for (var i = 0; i < n; i++)
				xLayouts[k][i] += shift;
		}

		var x = new double[n];
		var tmp = new double[4];
		for (var i = 0; i < n; i++)
		{
			for (var k = 0; k < 4; k++)
				tmp[k] = xLayouts[k][i];
			Array.Sort(tmp);
			x[i] = (tmp[1] + tmp[2]) / 2.0;
		}

		// Median averaging can violate minimum separation; one sweep per layer restores it.
		var byX = new List<int>();
		for (var li = 0; li < lg.LayerNodes.Length; li++)
		{
			byX.Clear();
			byX.AddRange(lg.LayerNodes[li]);
			byX.Sort((p, q) => x[p].CompareTo(x[q]));
			for (var idx = 1; idx < byX.Count; idx++)
			{
				var prev = byX[idx - 1];
				var curr = byX[idx];
				var minX = x[prev] + lg.W[prev] + Gap(lg, prev, curr, nodeSpacing);
				if (x[curr] < minX)
					x[curr] = minX;
			}
		}

		// Normalise so the leftmost graph node is at 0 (columns follow in the same frame).
		var minGraphX = double.MaxValue;
		for (var i = 0; i < lg.OrigN; i++)
			minGraphX = Math.Min(minGraphX, x[i]);
		for (var i = 0; i < n; i++)
			x[i] -= minGraphX;

		for (var i = 0; i < lg.OrigN; i++)
			graph.X[i] = i >= graph.RealNodeCount ? x[i] + (lg.W[i] / 2) : x[i];

		var ec = graph.Edges.Count;
		graph.ColumnX = new double[ec];
		graph.PortOutOff = new double[ec];
		graph.PortInOff = new double[ec];
		for (var ei = 0; ei < ec; ei++)
		{
			var d = lg.DummyOfGraphEdge[ei];
			graph.ColumnX[ei] = d >= 0 ? x[d] + (lg.W[d] / 2) : double.NaN;
			graph.PortOutOff[ei] = lg.FirstHalf[ei] >= 0 ? lg.OutOff[lg.FirstHalf[ei]] : 0;
			graph.PortInOff[ei] = lg.SecondHalf[ei] >= 0 ? lg.InOff[lg.SecondHalf[ei]] : 0;
		}
	}

	private static double Gap(Lg g, int u, int w, double nodeSpacing)
		=> (g.Virt[u] || g.Virt[w]) ? nodeSpacing * 0.75 : nodeSpacing;

	// ---------------------------------------------------------------- structure

	private static Lg Build(GraphBuffer graph)
	{
		var n0 = graph.NodeCount;
		var edges = graph.Edges;
		var labelExtent = graph.EdgeLabelExtent;

		// A label belongs to the first segment of its chain (source is a real node).
		double OwnedLabel(int ei)
		{
			var e = edges[ei];
			if (labelExtent is null || e.From >= graph.RealNodeCount || e.OriginalIndex >= labelExtent.Length)
				return 0;
			return labelExtent[e.OriginalIndex];
		}

		var adjacent = new bool[edges.Count];
		var gapLabelled = new bool[Math.Max(graph.LayerCount, 1)];
		for (var ei = 0; ei < edges.Count; ei++)
		{
			var e = edges[ei];
			adjacent[ei] = graph.Layers[e.To] == graph.Layers[e.From] + 1;
			if (adjacent[ei] && OwnedLabel(ei) > 0)
				gapLabelled[graph.Layers[e.From]] = true;
		}

		var newLayer = new int[graph.LayerCount];
		var dummyLayer = new int[graph.LayerCount];
		var cursor = 0;
		for (var li = 0; li < graph.LayerCount; li++)
		{
			newLayer[li] = cursor++;
			dummyLayer[li] = gapLabelled[li] && li < graph.LayerCount - 1 ? cursor++ : -1;
		}

		var dummyCount = 0;
		for (var ei = 0; ei < edges.Count; ei++)
		{
			if (adjacent[ei] && dummyLayer[graph.Layers[edges[ei].From]] >= 0)
				dummyCount++;
		}

		var lg = new Lg
		{
			OrigN = n0,
			N = n0 + dummyCount,
		};
		lg.W = new double[lg.N];
		lg.Virt = new bool[lg.N];
		lg.Layer = new int[lg.N];
		lg.Pos = new int[lg.N];
		lg.DummyOfGraphEdge = new int[edges.Count];
		lg.FirstHalf = new int[edges.Count];
		lg.SecondHalf = new int[edges.Count];
		Array.Fill(lg.DummyOfGraphEdge, -1);
		Array.Fill(lg.FirstHalf, -1);
		Array.Fill(lg.SecondHalf, -1);

		for (var i = 0; i < n0; i++)
		{
			lg.W[i] = i < graph.RealNodeCount ? graph.NodeWidths[i] : 0;
			lg.Virt[i] = i >= graph.RealNodeCount;
			lg.Layer[i] = newLayer[graph.Layers[i]];
		}

		// A long edge's first virtual node carries its label's width, so the label dummy and the long vertical share one
		// properly spaced column (the virtual node's position stays its centre in graph.X, see Run).
		for (var ei = 0; ei < edges.Count; ei++)
		{
			var e = edges[ei];
			if (adjacent[ei] && e.To >= graph.RealNodeCount && dummyLayer[graph.Layers[e.From]] >= 0)
				lg.W[e.To] = Math.Max(lg.W[e.To], OwnedLabel(ei));
		}

		var layerCount = cursor;
		var layerLists = new List<int>[layerCount];
		for (var li = 0; li < layerCount; li++)
			layerLists[li] = [];
		for (var li = 0; li < graph.LayerCount; li++)
			layerLists[newLayer[li]].AddRange(graph.LayerNodes[li]);

		var nextDummy = n0;
		var pending = new List<(int Ei, int D)>?[graph.LayerCount];
		for (var ei = 0; ei < edges.Count; ei++)
		{
			var e = edges[ei];
			if (!adjacent[ei] || dummyLayer[graph.Layers[e.From]] < 0)
				continue;
			var d = nextDummy++;
			lg.W[d] = OwnedLabel(ei);
			lg.Virt[d] = true;
			lg.Layer[d] = dummyLayer[graph.Layers[e.From]];
			lg.DummyOfGraphEdge[ei] = d;
			(pending[graph.Layers[e.From]] ??= []).Add((ei, d));
		}

		for (var li = 0; li < graph.LayerCount; li++)
		{
			if (pending[li] is not { } list)
				continue;
			list.Sort((p, q) =>
			{
				var ep = edges[p.Ei];
				var eq = edges[q.Ei];
				var c = graph.NodePositionInLayer[ep.From].CompareTo(graph.NodePositionInLayer[eq.From]);
				if (c != 0)
					return c;
				c = graph.NodePositionInLayer[ep.To].CompareTo(graph.NodePositionInLayer[eq.To]);
				return c != 0 ? c : ep.OriginalIndex.CompareTo(eq.OriginalIndex);
			});
			foreach (var (_, d) in list)
				layerLists[dummyLayer[li]].Add(d);
		}

		lg.LayerNodes = layerLists.Select(l => l.ToArray()).ToArray();
		for (var li = 0; li < layerCount; li++)
		{
			for (var i = 0; i < lg.LayerNodes[li].Length; i++)
				lg.Pos[lg.LayerNodes[li][i]] = i;
		}

		for (var ei = 0; ei < edges.Count; ei++)
		{
			var e = edges[ei];
			if (!adjacent[ei])
				continue;
			var d = lg.DummyOfGraphEdge[ei];
			if (d >= 0)
			{
				lg.FirstHalf[ei] = lg.Edges.Count;
				// Long edge: the gap dummy aligns with its virtual node (one straight column carrying the label), not the source port.
				if (e.To >= graph.RealNodeCount)
					_ = lg.NoAlign.Add(lg.Edges.Count);
				lg.Edges.Add((e.From, d, e.OriginalIndex));
				lg.SecondHalf[ei] = lg.Edges.Count;
				lg.Edges.Add((d, e.To, e.OriginalIndex));
			}
			else
			{
				lg.FirstHalf[ei] = lg.Edges.Count;
				lg.SecondHalf[ei] = lg.Edges.Count;
				lg.Edges.Add((e.From, e.To, e.OriginalIndex));
			}
		}

		lg.InNb = new List<int>[lg.N];
		lg.OutNb = new List<int>[lg.N];
		for (var i = 0; i < lg.N; i++)
		{
			lg.InNb[i] = [];
			lg.OutNb[i] = [];
		}
		for (var ei = 0; ei < lg.Edges.Count; ei++)
		{
			if (lg.NoAlign.Contains(ei))
				continue;
			var (from, to, _) = lg.Edges[ei];
			lg.OutNb[from].Add(to);
			lg.InNb[to].Add(from);
		}

		return lg;
	}

	/// <summary>Justified port offsets (from node left) per edge end; dummies attach at their centre.</summary>
	private static void ComputePortOffsets(Lg g)
	{
		g.OutOff = new double[g.Edges.Count];
		g.InOff = new double[g.Edges.Count];
		var outs = new List<int>?[g.N];
		var ins = new List<int>?[g.N];
		for (var ei = 0; ei < g.Edges.Count; ei++)
		{
			(outs[g.Edges[ei].From] ??= []).Add(ei);
			(ins[g.Edges[ei].To] ??= []).Add(ei);
		}

		for (var v = 0; v < g.N; v++)
		{
			var w = g.W[v];
			var attachesAtCentre = v >= g.OrigN;
			if (outs[v] is { } o)
			{
				o.Sort((a, b) =>
				{
					var c = g.Pos[g.Edges[a].To].CompareTo(g.Pos[g.Edges[b].To]);
					return c != 0 ? c : g.Edges[a].Orig.CompareTo(g.Edges[b].Orig);
				});
				for (var i = 0; i < o.Count; i++)
					g.OutOff[o[i]] = attachesAtCentre ? w / 2 : w * (i + 1) / (o.Count + 1);
			}
			if (ins[v] is { } inn)
			{
				inn.Sort((a, b) =>
				{
					var c = g.Pos[g.Edges[a].From].CompareTo(g.Pos[g.Edges[b].From]);
					return c != 0 ? c : g.Edges[a].Orig.CompareTo(g.Edges[b].Orig);
				});
				for (var i = 0; i < inn.Count; i++)
					g.InOff[inn[i]] = attachesAtCentre ? w / 2 : w * (i + 1) / (inn.Count + 1);
			}
		}
	}

	// ---------------------------------------------------------------- passes

	private static double[] RunPass(Lg g, bool downward, bool leftward, double nodeSpacing)
	{
		var n = g.N;
		var view = new int[g.LayerNodes.Length][];
		var pos = new int[n];
		for (var li = 0; li < g.LayerNodes.Length; li++)
		{
			var src = g.LayerNodes[li];
			var arr = new int[src.Length];
			for (var i = 0; i < src.Length; i++)
				arr[i] = leftward ? src[i] : src[src.Length - 1 - i];
			view[li] = arr;
			for (var i = 0; i < arr.Length; i++)
				pos[arr[i]] = i;
		}

		var edgeOf = new Dictionary<long, int>();
		for (var ei = 0; ei < g.Edges.Count; ei++)
			_ = edgeOf.TryAdd(((long)g.Edges[ei].From * g.N) + g.Edges[ei].To, ei);

		double PortFacing(int a, int b)
		{
			var off = edgeOf.TryGetValue(((long)a * g.N) + b, out var e1) ? g.OutOff[e1]
				: edgeOf.TryGetValue(((long)b * g.N) + a, out var e2) ? g.InOff[e2]
				: g.W[a] / 2;
			return leftward ? off : g.W[a] - off;
		}

		var (root, align) = VertAlign(g, view, pos, downward);

		var inner = new double[n];
		for (var v = 0; v < n; v++)
		{
			if (root[v] != v)
				continue;
			var a = v;
			while (align[a] != v)
			{
				var b = align[a];
				inner[b] = inner[a] + PortFacing(a, b) - PortFacing(b, a);
				a = b;
			}
		}

		var x = Compact(g, view, pos, root, align, inner, nodeSpacing);
		var result = new double[n];
		for (var v = 0; v < n; v++)
			result[v] = leftward ? x[v] : -(x[v] + g.W[v]);
		return result;
	}

	private static (int[] root, int[] align) VertAlign(Lg g, int[][] view, int[] pos, bool downward)
	{
		var n = g.N;
		var root = new int[n];
		var align = new int[n];
		for (var i = 0; i < n; i++)
		{
			root[i] = i;
			align[i] = i;
		}

		var blocked = MarkType1Conflicts(g);
		var layers = view.Length;
		var li0 = downward ? 1 : layers - 2;
		var li1 = downward ? layers : -1;
		var liStep = downward ? 1 : -1;

		for (var li = li0; li != li1; li += liStep)
		{
			var r = -1;
			foreach (var v in view[li])
			{
				var nb = new List<int>(downward ? g.InNb[v] : g.OutNb[v]);
				if (nb.Count == 0)
					continue;
				nb.Sort((p, q) => pos[p].CompareTo(pos[q]));
				var d = nb.Count;
				for (var m = (d - 1) / 2; m <= d / 2; m++)
				{
					if (align[v] != v)
						break;
					var u = nb[m];
					if (downward ? blocked.Contains((u, v)) : blocked.Contains((v, u)))
						continue;
					if (pos[u] > r)
					{
						align[u] = v;
						root[v] = root[u];
						align[v] = root[v];
						r = pos[u];
					}
				}
			}
		}

		return (root, align);
	}

	private static double[] Compact(Lg g, int[][] view, int[] pos, int[] root, int[] align, double[] inner, double nodeSpacing)
	{
		var n = g.N;
		var x = new double[n];
		var placed = new bool[n];
		var sink = new int[n];
		var shift = new double[n];
		for (var i = 0; i < n; i++)
		{
			sink[i] = i;
			shift[i] = double.PositiveInfinity;
		}

		foreach (var layer in view)
		{
			foreach (var v in layer)
			{
				if (root[v] == v && !placed[v])
					PlaceBlock(g, view, pos, root, align, inner, sink, shift, x, placed, nodeSpacing, v);
			}
		}

		var result = new double[n];
		for (var v = 0; v < n; v++)
		{
			result[v] = x[root[v]] + inner[v];
			var s = shift[sink[root[v]]];
			if (s < double.PositiveInfinity)
				result[v] += s;
		}
		return result;
	}

	private static void PlaceBlock(
		Lg g, int[][] view, int[] pos, int[] root, int[] align, double[] inner,
		int[] sink, double[] shift, double[] x, bool[] placed, double nodeSpacing, int v)
	{
		placed[v] = true;

		var minInner = 0.0;
		var t = v;
		do
		{
			minInner = Math.Min(minInner, inner[t]);
			t = align[t];
		} while (t != v);
		x[v] = -minInner;

		var w = v;
		do
		{
			var layer = view[g.Layer[w]];
			var posW = pos[w];
			if (posW > 0)
			{
				var uNode = layer[posW - 1];
				var uRoot = root[uNode];
				if (!placed[uRoot])
					PlaceBlock(g, view, pos, root, align, inner, sink, shift, x, placed, nodeSpacing, uRoot);

				var sep = g.W[uNode] + Gap(g, uNode, w, nodeSpacing) + inner[uNode] - inner[w];
				if (sink[v] == v)
					sink[v] = sink[uRoot];

				if (sink[v] != sink[uRoot])
				{
					var candidate = x[v] - x[uRoot] - sep;
					if (candidate < shift[sink[uRoot]])
						shift[sink[uRoot]] = candidate;
				}
				else
				{
					var minX = x[uRoot] + sep;
					if (x[v] < minX)
						x[v] = minX;
				}
			}
			w = align[w];
		} while (w != v);
	}

	/// <summary>
	/// Type-1 conflicts: a non-inner segment crossing an inner segment (both ends virtual/dummy).
	/// Stored as (upper, lower) pairs regardless of pass direction.
	/// </summary>
	private static HashSet<(int U, int V)> MarkType1Conflicts(Lg g)
	{
		var blocked = new HashSet<(int, int)>();
		for (var upperLi = 0; upperLi < g.LayerNodes.Length - 1; upperLi++)
		{
			var upper = g.LayerNodes[upperLi];
			var lower = g.LayerNodes[upperLi + 1];
			var k0 = 0;

			for (var l1 = 0; l1 < lower.Length; l1++)
			{
				var v = lower[l1];
				var innerUpperPos = -1;
				if (g.Virt[v])
				{
					foreach (var u in g.InNb[v])
					{
						if (g.Layer[u] == upperLi && g.Virt[u])
						{
							innerUpperPos = g.Pos[u];
							break;
						}
					}
				}

				if (l1 == lower.Length - 1 || innerUpperPos >= 0)
				{
					var k1 = innerUpperPos >= 0 ? innerUpperPos : upper.Length - 1;
					for (var l = k0; l <= l1; l++)
					{
						var w = lower[l];
						foreach (var u in g.InNb[w])
						{
							if (g.Layer[u] != upperLi)
								continue;
							var posU = g.Pos[u];
							if (posU < k0 || posU > k1)
								_ = blocked.Add((u, w));
						}
					}
					k0 = innerUpperPos >= 0 ? innerUpperPos : k0;
				}
			}
		}
		return blocked;
	}
}
