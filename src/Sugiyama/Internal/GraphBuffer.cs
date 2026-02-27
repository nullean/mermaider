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

	internal double[] X;
	internal double[] Y;

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
