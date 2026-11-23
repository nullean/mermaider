#pragma warning disable IDE0028
namespace Sugiyama.Internal;

/// <summary>Constructors that exist on modern .NET but not on netstandard2.0.</summary>
internal static class Sets
{
	public static HashSet<T> New<T>(int capacity) =>
#if NETSTANDARD2_0
		[];
#else
		new(capacity);
#endif
}
