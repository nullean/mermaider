using Mermaider.Animate;

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

	/// <summary>
	/// Renders <paramref name="source"/> to a self-contained SVG string. A diagram with an <c>animate</c> block is animated (the
	/// SVG plays it with CSS keyframes); one without renders exactly as <c>MermaidRenderer.RenderSvg</c> does.
	/// </summary>
	/// <exception cref="ArgumentException">The options are not valid.</exception>
	/// <exception cref="MermaidParseException">The diagram cannot be parsed.</exception>
	public static string RenderSvg(string source, string? optionsJson) => MermaidAnimator.RenderSvg(source, NativeOptions.ParseAnimate(optionsJson));
}
