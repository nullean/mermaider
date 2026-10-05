namespace Mermaider.Rendering;

/// <summary>
/// Numbers connected clusters (union-find) in order of the first member, so each cluster gets one palette colour.
/// Shared by flowchart/state nodes, ER entities and class diagrams.
/// </summary>
internal sealed class ClusterAssigner
{
	private readonly Dictionary<string, string> _parent;

	internal ClusterAssigner(IEnumerable<string> ids) =>
		_parent = ids.ToDictionary(i => i, i => i, StringComparer.Ordinal);

	internal string Find(string id)
	{
		while (_parent[id] != id)
		{
			_parent[id] = _parent[_parent[id]];
			id = _parent[id];
		}

		return id;
	}

	internal void Union(string a, string b)
	{
		if (_parent.ContainsKey(a) && _parent.ContainsKey(b))
			_parent[Find(a)] = Find(b);
	}

	/// <summary>Cluster number per id, in the order the ids are given (the first member of a cluster fixes its number).</summary>
	internal Dictionary<string, int> Number(IEnumerable<string> ids)
	{
		var index = new Dictionary<string, int>(StringComparer.Ordinal);
		var result = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (var id in ids)
		{
			var root = Find(id);
			if (!index.TryGetValue(root, out var i))
				index[root] = i = index.Count;
			result[id] = i;
		}

		return result;
	}
}
