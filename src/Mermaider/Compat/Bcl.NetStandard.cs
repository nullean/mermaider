#if !NET8_0_OR_GREATER
// Stand-ins for the newer BCL members this library calls: netstandard2.0 / 2.1, and .NET 7 for the .NET 8 ones. Each block
// is guarded by the oldest target that lacks the member, so a target gets the real member whenever it has one. The general
// helpers shared with Sugiyama are in Sugiyama's NetStandard.cs. Everything here is in Mermaider.Compat, which friend
// assemblies do not import.
using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Mermaider.Compat;

internal static class BclShim
{
#if NETSTANDARD
		// .NET 6 / 7
		extension(ArgumentNullException)
		{
			public static void ThrowIfNull(object? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
			{
				if (argument is null) throw new ArgumentNullException(paramName);
			}
		}

		extension(char)
		{
			public static bool IsAsciiDigit(char c) => c is >= '0' and <= '9';
			public static bool IsAsciiLetter(char c) => (uint)((c | 0x20) - 'a') <= 'z' - 'a';
			public static bool IsAsciiLetterOrDigit(char c) => char.IsAsciiLetter(c) || char.IsAsciiDigit(c);
			public static bool IsAsciiHexDigit(char c) => char.IsAsciiDigit(c) || (uint)((c | 0x20) - 'a') <= 'f' - 'a';
		}

		extension(double)
		{
			public static bool TryParse(string? s, IFormatProvider? provider, out double result) =>
				double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, provider, out result);
		}
#endif

	// .NET 8
	extension(ArgumentException)
	{
		public static void ThrowIfNullOrWhiteSpace(string? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
		{
			if (argument is null)
				throw new ArgumentNullException(paramName);
			if (string.IsNullOrWhiteSpace(argument))
				throw new ArgumentException("The value cannot be an empty string or composed entirely of whitespace.", paramName);
		}
	}

	extension<TKey, TValue>(ReadOnlyDictionary<TKey, TValue>) where TKey : notnull
	{
		public static ReadOnlyDictionary<TKey, TValue> Empty => EmptyHolder<TKey, TValue>.Instance;
	}

#if NETSTANDARD
		// .NET 7
		extension(ReadOnlySpan<char> span)
		{
			public bool Contains(char c) => span.IndexOf(c) >= 0;

			public LineEnumerator EnumerateLines() => new(span);
		}
#endif

#if NETSTANDARD2_0
		// present in netstandard2.1 and later
		extension(double)
		{
			public static bool IsFinite(double d) => !double.IsNaN(d) && !double.IsInfinity(d);

			public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out double result) =>
				double.TryParse(s.ToString(), style, provider, out result);
		}

		extension(int)
		{
			public static bool TryParse(ReadOnlySpan<char> s, out int result) => int.TryParse(s.ToString(), out result);
		}

		extension(Enum)
		{
			public static TEnum Parse<TEnum>(string value, bool ignoreCase = false) where TEnum : struct, Enum =>
				(TEnum)Enum.Parse(typeof(TEnum), value, ignoreCase);
		}

		extension(System.Text.StringBuilder sb)
		{
			public System.Text.StringBuilder Append(ReadOnlySpan<char> value) => sb.Append(value.ToString());
		}

		extension(System.Text.Encoding encoding)
		{
			public string GetString(ReadOnlySpan<byte> bytes) => encoding.GetString(bytes.ToArray());
		}

		extension(string)
		{
			// the identity-typed string[] / IEnumerable<string> overloads in Sugiyama's NetStandard.cs win for those arguments
			public static string Join(char separator, params object?[] values) => string.Join(separator.ToString(), values);
		}

		extension(string value)
		{
			public string[] Split(char separator, StringSplitOptions options) => value.Split([separator], options);

			public bool Contains(char c, StringComparison comparison) => value.IndexOf(c.ToString(), comparison) >= 0;

			public string Replace(string oldValue, string newValue, StringComparison comparison)
			{
				if (comparison == StringComparison.Ordinal) return value.Replace(oldValue, newValue);
				if (oldValue.Length == 0) throw new ArgumentException("String cannot be of zero length.", nameof(oldValue));
				var sb = new System.Text.StringBuilder(value.Length);
				var pos = 0;
				int hit;
				while ((hit = value.IndexOf(oldValue, pos, comparison)) >= 0)
				{
					_ = sb.Append(value, pos, hit - pos).Append(newValue);
					pos = hit + oldValue.Length;
				}
				return sb.Append(value, pos, value.Length - pos).ToString();
			}
		}
#endif
}

#if NETSTANDARD
	/// <summary>Splits on \r\n, \n and \r; a trailing terminator does not yield an empty last line (as on .NET 8).</summary>
	internal ref struct LineEnumerator
	{
		private ReadOnlySpan<char> _rest;
		private bool _done;

		public LineEnumerator(ReadOnlySpan<char> text)
		{
			_rest = text;
			_done = false;
			Current = default;
		}

		public ReadOnlySpan<char> Current { get; private set; }

		public readonly LineEnumerator GetEnumerator() => this;

		public bool MoveNext()
		{
			if (_done) return false;
			var idx = _rest.IndexOfAny('\r', '\n');
			if (idx < 0)
			{
				_done = true;
				if (_rest.IsEmpty) return false;
				Current = _rest;
				_rest = default;
				return true;
			}
			Current = _rest[..idx];
			var skip = _rest[idx] == '\r' && idx + 1 < _rest.Length && _rest[idx + 1] == '\n' ? 2 : 1;
			_rest = _rest[(idx + skip)..];
			return true;
		}
	}

#endif

internal static class EmptyHolder<TKey, TValue> where TKey : notnull
{
	public static readonly ReadOnlyDictionary<TKey, TValue> Instance = new(new Dictionary<TKey, TValue>());
}
#endif
