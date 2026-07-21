using System.Buffers;
using System.Runtime.CompilerServices;

namespace Sugiyama.Internal;

/// <summary>
/// Flat, array-indexed graph representation for the Sugiyama pipeline.
/// All nodes (real + virtual) are identified by dense integer ordinals.
/// Working arrays are rented from ArrayPool for graphs > 64 nodes.
/// </summary>
internal sealed class GraphBuffer : IDisposable
{
	private const int StackAllocThreshold = 64;

	internal int NodeCount { get; private set; }
	internal int RealNodeCount { get; }
	internal int EdgeCount => Edges.Count;

	internal readonly string[] NodeIds;
	internal readonly double[] NodeWidths;
	internal readonly double[] NodeHeights;

	internal readonly List<GraphEdge> Edges;

	internal int[] Layers;
	internal int LayerCount;

	internal int[][] LayerNodes = [];
	internal int[] NodePositionInLayer;

	/// <summary>Pairs of node indices that must share the same layer. A is placed left of B.</summary>
	internal List<(int A, int B)> SameRankPairs = [];

	internal double[] X;
	internal double[] Y;

	// CSR out-adjacency: for node n, out-neighbors are OutAdjNeighbor[OutAdjStart[n]..OutAdjStart[n+1])
	internal int[] OutAdjStart = [];    // length NodeCount + 1
	internal int[] OutAdjNeighbor = []; // target node ordinals

	// CSR in-adjacency
	internal int[] InAdjStart = [];     // length NodeCount + 1
	internal int[] InAdjNeighbor = [];  // source node ordinals

	internal bool AdjacencyBuilt { get; private set; }

	private readonly List<int[]> _rentedArrays = [];

	internal GraphBuffer(int nodeCount, int edgeCapacity)
	{
		RealNodeCount = nodeCount;
		NodeCount = nodeCount;
		// Over-allocate to accommodate virtual nodes without frequent resizing
		var capacity = Math.Max(nodeCount * 2, 16);
		NodeIds = new string[nodeCount];
		NodeWidths = new double[nodeCount];
		NodeHeights = new double[nodeCount];
		Edges = new List<GraphEdge>(edgeCapacity);
		Layers = RentInt(capacity);
		NodePositionInLayer = RentInt(capacity);
		X = new double[capacity];
		Y = new double[capacity];
	}

	internal int AddVirtualNode()
	{
		var id = NodeCount;
		NodeCount++;
		EnsureCapacity();
		return id;
	}

	private void EnsureCapacity()
	{
		if (NodeCount <= X.Length)
			return;

		var newSize = Math.Max(NodeCount * 2, X.Length * 2);
		Layers = Grow(Layers, newSize);
		NodePositionInLayer = Grow(NodePositionInLayer, newSize);
		X = Grow(X, newSize);
		Y = Grow(Y, newSize);
	}

	internal int[] RentInt(int size)
	{
		if (size <= 0)
			return [];
		var arr = ArrayPool<int>.Shared.Rent(size);
		Array.Clear(arr, 0, size);
		_rentedArrays.Add(arr);
		return arr;
	}

	internal void RebuildAdjacency()
	{
		var n = NodeCount;
		var outStart = new int[n + 1];
		var inStart = new int[n + 1];

		foreach (var e in Edges)
		{
			outStart[e.From + 1]++;
			inStart[e.To + 1]++;
		}

		for (var i = 1; i <= n; i++)
		{
			outStart[i] += outStart[i - 1];
			inStart[i] += inStart[i - 1];
		}

		var outNeighbor = new int[Edges.Count];
		var inNeighbor = new int[Edges.Count];
		var outPos = new int[n];
		var inPos = new int[n];
		Array.Copy(outStart, outPos, n);
		Array.Copy(inStart, inPos, n);

		foreach (var e in Edges)
		{
			outNeighbor[outPos[e.From]++] = e.To;
			inNeighbor[inPos[e.To]++] = e.From;
		}

		OutAdjStart = outStart;
		OutAdjNeighbor = outNeighbor;
		InAdjStart = inStart;
		InAdjNeighbor = inNeighbor;
		AdjacencyBuilt = true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal ReadOnlySpan<GraphEdge> OutEdges(int node)
	{
		var list = new List<GraphEdge>();
		foreach (var e in Edges)
		{
			if (e.From == node)
				list.Add(e);
		}
		return list.ToArray();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal ReadOnlySpan<GraphEdge> InEdges(int node)
	{
		var list = new List<GraphEdge>();
		foreach (var e in Edges)
		{
			if (e.To == node)
				list.Add(e);
		}
		return list.ToArray();
	}

	private int[] Grow(int[] old, int newSize)
	{
		var arr = RentInt(newSize);
		Array.Copy(old, arr, Math.Min(old.Length, newSize));
		return arr;
	}

	private double[] Grow(double[] old, int newSize)
	{
		var arr = new double[newSize];
		Array.Copy(old, arr, Math.Min(old.Length, newSize));
		return arr;
	}

	public void Dispose()
	{
		foreach (var arr in _rentedArrays)
			ArrayPool<int>.Shared.Return(arr);
		_rentedArrays.Clear();
	}
}

internal readonly record struct GraphEdge(int From, int To, int OriginalIndex, bool IsVirtual = false, bool Reversed = false);
