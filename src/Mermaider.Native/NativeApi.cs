namespace Mermaider.Native;

/// <summary>
/// What the C exports do, as managed code a test can call: render Mermaid text to SVG with the options of a JSON
/// object (see <see cref="NativeOptions"/>).
/// </summary>
public static class NativeApi
{
	/// <summary>
	/// The version of the C interface: bumped when an export changes or a key of the options changes meaning, not when a
	/// key or a diagram type is added. A host checks it with <c>mermaider_abi_version</c> before it calls anything else.
	/// </summary>
	public const int AbiVersion = 1;

	/// <summary>Renders <paramref name="source"/> to a self-contained SVG string.</summary>
	/// <exception cref="ArgumentException">The options are not valid.</exception>
	/// <exception cref="MermaidParseException">The diagram cannot be parsed.</exception>
	public static string RenderSvg(string source, string? optionsJson) => MermaidRenderer.RenderSvg(source, NativeOptions.Parse(optionsJson));
}
