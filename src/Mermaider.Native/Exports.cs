using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Mermaider.Native;

/// <summary>
/// The C interface (see <c>mermaider.h</c> in the release assets). All text is UTF-8 and NUL-terminated. An export never
/// lets an exception cross into the host: it returns null (or a non-zero status) and the reason is available from
/// <c>mermaider_last_error</c> on the same thread. Memory this library returns (the SVG) is the host's to give back with
/// <c>mermaider_free</c>; the error text is the library's and valid until the next call on that thread.
/// </summary>
public static unsafe class Exports
{
	[ThreadStatic]
	private static byte* ErrorText;

	/// <summary>The version of the C interface the host can rely on.</summary>
	[UnmanagedCallersOnly(EntryPoint = "mermaider_abi_version", CallConvs = [typeof(CallConvCdecl)])]
	public static int AbiVersion() => NativeApi.AbiVersion;

	/// <summary>
	/// Renders <paramref name="source"/> (Mermaid text) to SVG with <paramref name="optionsJson"/> (a JSON object, or null for
	/// the defaults). Returns the SVG, to free with <c>mermaider_free</c>; null on failure, with the reason in
	/// <c>mermaider_last_error</c>.
	/// </summary>
	[UnmanagedCallersOnly(EntryPoint = "mermaider_render_svg", CallConvs = [typeof(CallConvCdecl)])]
	public static byte* RenderSvg(byte* source, byte* optionsJson)
	{
		try
		{
			if (source == null)
				throw new ArgumentException("no diagram text");
			var text = Marshal.PtrToStringUTF8((nint)source) ?? "";
			var options = optionsJson == null ? null : Marshal.PtrToStringUTF8((nint)optionsJson);
			var svg = NativeApi.RenderSvg(text, options);
			SetError(null);
			return Copy(svg);
		}
		catch (Exception e)
		{
			SetError(e.Message);
			return null;
		}
	}

	/// <summary>The reason the last call on this thread failed; owned by the library, valid until the next call.</summary>
	[UnmanagedCallersOnly(EntryPoint = "mermaider_last_error", CallConvs = [typeof(CallConvCdecl)])]
	public static byte* LastError() => ErrorText;

	/// <summary>Gives back memory the library returned.</summary>
	[UnmanagedCallersOnly(EntryPoint = "mermaider_free", CallConvs = [typeof(CallConvCdecl)])]
	public static void Free(byte* memory) => NativeMemory.Free(memory);

	private static void SetError(string? message)
	{
		if (ErrorText != null)
			NativeMemory.Free(ErrorText);
		ErrorText = message == null ? null : Copy(message);
	}

	private static byte* Copy(string text)
	{
		var bytes = Encoding.UTF8.GetByteCount(text);
		var memory = (byte*)NativeMemory.Alloc((nuint)bytes + 1);
		Encoding.UTF8.GetBytes(text, new Span<byte>(memory, bytes));
		memory[bytes] = 0;
		return memory;
	}
}
