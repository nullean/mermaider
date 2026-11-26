// Compiler-support types netstandard2.0 / 2.1 lack; internal, so they never leak into the public surface.
#if NETSTANDARD
namespace System.Runtime.CompilerServices
{
	internal static class IsExternalInit;

	[AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
	internal sealed class CompilerFeatureRequiredAttribute(string featureName) : Attribute
	{
		public string FeatureName { get; } = featureName;
		public bool IsOptional { get; init; }
	}

	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
	internal sealed class RequiredMemberAttribute : Attribute;
}

namespace System.Diagnostics.CodeAnalysis
{
	[AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
	internal sealed class SetsRequiredMembersAttribute : Attribute;
}
#endif

#if NETSTANDARD2_0
namespace System
{
	internal readonly struct Index(int value, bool fromEnd = false)
	{
		public int GetOffset(int length) => fromEnd ? length - value : value;

		public static implicit operator Index(int value) => new(value);
	}

	internal readonly struct Range(Index start, Index end)
	{
		public Index Start { get; } = start;
		public Index End { get; } = end;

		public static Range StartAt(Index start) => new(start, new Index(0, fromEnd: true));
		public static Range EndAt(Index end) => new(new Index(0), end);
		public static Range All => new(new Index(0), new Index(0, fromEnd: true));
	}
}

namespace Mermaider.Animate
{
	internal static class StringShim
	{
		public static bool StartsWith(this string value, char c) => value.Length > 0 && value[0] == c;
	}
}
#endif
