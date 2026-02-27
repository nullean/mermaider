using System.Text;
using Microsoft.Extensions.ObjectPool;

namespace Mermaider.Rendering;

internal static class SharedStringBuilderPool
{
	internal static readonly ObjectPool<StringBuilder> Instance =
		new DefaultObjectPoolProvider().CreateStringBuilderPool(initialCapacity: 4096, maximumRetainedCapacity: 64 * 1024);
}
