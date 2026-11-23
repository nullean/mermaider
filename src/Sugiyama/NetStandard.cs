#if NETSTANDARD
// What this library calls that netstandard2.0 / 2.1 lack (members marked 2.0-only exist in 2.1). Extension helpers sit in Sugiyama.Compat, a namespace friend
// assemblies do not import, so they cannot clash with the real BCL members there.
using System.ComponentModel;

namespace System.Runtime.CompilerServices
{
	[EditorBrowsable(EditorBrowsableState.Never)]
	internal static class IsExternalInit;
}

#if NETSTANDARD2_0
namespace System
{
	// The compiler needs these exact types for `^1` and `a..b`.
	internal readonly struct Index : IEquatable<Index>
	{
		private readonly int _value;

		public Index(int value, bool fromEnd = false) => _value = fromEnd ? ~value : value;

		public static Index Start => new(0);
		public static Index End => new(~0);
		public static implicit operator Index(int value) => new(value);
		public static Index FromStart(int value) => new(value);
		public static Index FromEnd(int value) => new(~value);

		public int Value => _value < 0 ? ~_value : _value;
		public bool IsFromEnd => _value < 0;
		public int GetOffset(int length) => IsFromEnd ? length - Value : Value;
		public bool Equals(Index other) => _value == other._value;
		public override bool Equals(object? obj) => obj is Index i && i._value == _value;
		public override int GetHashCode() => _value;
		public override string ToString() => IsFromEnd ? "^" + Value : Value.ToString();
	}

	internal readonly struct Range : IEquatable<Range>
	{
		public Range(Index start, Index end)
		{
			Start = start;
			End = end;
		}

		public Index Start { get; }
		public Index End { get; }
		public static Range All => new(Index.Start, Index.End);
		public static Range StartAt(Index start) => new(start, Index.End);
		public static Range EndAt(Index end) => new(Index.Start, end);

		public (int Offset, int Length) GetOffsetAndLength(int length)
		{
			var start = Start.GetOffset(length);
			var end = End.GetOffset(length);
			if ((uint)end > (uint)length || (uint)start > (uint)end) throw new ArgumentOutOfRangeException(nameof(length));
			return (start, end - start);
		}

		public bool Equals(Range other) => Start.Equals(other.Start) && End.Equals(other.End);
		public override bool Equals(object? obj) => obj is Range r && Equals(r);
		public override int GetHashCode() => Start.GetHashCode() * 31 + End.GetHashCode();
		public override string ToString() => Start + ".." + End;
	}
}
#endif

namespace Sugiyama.Compat
{
	internal static class NetStandard
	{
#if NETSTANDARD2_0
		public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value)
		{
			key = pair.Key;
			value = pair.Value;
		}

		public static TValue? GetValueOrDefault<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> map, TKey key) =>
			map.TryGetValue(key, out var v) ? v : default;

		public static TValue? GetValueOrDefault<TKey, TValue>(this Dictionary<TKey, TValue> map, TKey key) where TKey : notnull =>
			map.TryGetValue(key, out var v) ? v : default;

		public static TValue GetValueOrDefault<TKey, TValue>(this Dictionary<TKey, TValue> map, TKey key, TValue fallback) where TKey : notnull =>
			map.TryGetValue(key, out var v) ? v : fallback;

		public static bool TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> map, TKey key, TValue value) where TKey : notnull
		{
			if (map.ContainsKey(key)) return false;
			map.Add(key, value);
			return true;
		}

		public static HashSet<T> ToHashSet<T>(this IEnumerable<T> source, IEqualityComparer<T>? comparer = null) => new(source, comparer);
#endif

		public static T? MaxBy<T, TKey>(this IEnumerable<T> source, Func<T, TKey> key)
		{
			var cmp = Comparer<TKey>.Default;
			T? best = default;
			TKey? bestKey = default;
			var any = false;
			foreach (var item in source)
			{
				var k = key(item);
				if (!any || cmp.Compare(k, bestKey!) > 0) { best = item; bestKey = k; any = true; }
			}
			return best;
		}

#if NETSTANDARD2_0
		public static bool Contains(this string s, char c) => s.IndexOf(c) >= 0;

		public static bool StartsWith(this string s, char c) => s.Length > 0 && s[0] == c;
		public static bool EndsWith(this string s, char c) => s.Length > 0 && s[s.Length - 1] == c;

		extension(string)
		{
			public static string Join(char separator, IEnumerable<string> values) => string.Join(separator.ToString(), values);

			public static string Join(char separator, params string[] values) => string.Join(separator.ToString(), values);
		}

		extension(Math)
		{
			public static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
			public static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
		}
#endif
	}
}
#endif
