#if !NET8_0_OR_GREATER
// netstandard2.0 has no System.Collections.Frozen. The public API there exposes the mutable BCL collections instead
// (see the StringSet / StringMap aliases in Mermaider.csproj); these internal helpers keep the `.ToFrozenSet()` /
// `.ToFrozenDictionary()` call sites identical across targets.
namespace Mermaider.Compat
{
	internal static class FrozenSet
	{
		public static HashSet<T> ToFrozenSet<T>(this IEnumerable<T> source, IEqualityComparer<T>? comparer = null) => new(source, comparer);
	}

	internal static class FrozenDictionary
	{
		public static Dictionary<TKey, TValue> ToFrozenDictionary<TKey, TValue>(
			this IEnumerable<KeyValuePair<TKey, TValue>> source,
			IEqualityComparer<TKey>? comparer = null
		) where TKey : notnull
		{
			var map = new Dictionary<TKey, TValue>(comparer);
			foreach (var kv in source)
				map.Add(kv.Key, kv.Value);
			return map;
		}

		public static Dictionary<TKey, TValue> ToFrozenDictionary<TSource, TKey, TValue>(
			this IEnumerable<TSource> source,
			Func<TSource, TKey> keySelector,
			Func<TSource, TValue> elementSelector,
			IEqualityComparer<TKey>? comparer = null
		) where TKey : notnull
		{
			var map = new Dictionary<TKey, TValue>(comparer);
			foreach (var item in source)
				map.Add(keySelector(item), elementSelector(item));
			return map;
		}
	}
}

namespace Mermaider.Compat
{
	/// <summary>A set of chars with O(1) lookup (netstandard2.0 stand-in for the .NET type of the same name).</summary>
	internal sealed class SearchValues<T> where T : IEquatable<T>
	{
		private readonly bool[] _ascii = new bool[128];
		private readonly HashSet<T> _other = [];

		internal SearchValues(ReadOnlySpan<T> values)
		{
			foreach (var v in values)
			{
				if (v is char c && c < 128)
					_ascii[c] = true;
				else
					_ = _other.Add(v);
			}
		}

		public bool Contains(T value) => value is char c && c < 128 ? _ascii[c] : _other.Contains(value);
	}

	internal static class SearchValues
	{
		public static SearchValues<char> Create(ReadOnlySpan<char> values) => new(values);

		public static bool ContainsAny(this ReadOnlySpan<char> span, SearchValues<char> values)
		{
			foreach (var c in span)
			{
				if (values.Contains(c))
					return true;
			}
			return false;
		}

		public static bool ContainsAny(this string span, SearchValues<char> values) => span.AsSpan().ContainsAny(values);
	}
}
#endif
