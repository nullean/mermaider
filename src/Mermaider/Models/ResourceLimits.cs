namespace Mermaider.Models;

/// <summary>
/// Resource limits applied to each render call to bound CPU, memory, stack, and
/// output-size consumption. All limits are <b>on by default</b> with generous values
/// sized well above any realistic diagram — raise individual limits for legitimate
/// large inputs, or set <see cref="Unlimited"/> for trusted server-side calls.
/// </summary>
/// <remarks>
/// When a limit is exceeded, <see cref="MermaidResourceLimitException"/> is thrown
/// (a subtype of <see cref="MermaidParseException"/>).
/// Defensive guarantee: the built-in Sugiyama layout engine is documented for
/// &lt;50-node graphs; limits prevent super-linear cost from adversarial inputs while
/// still accommodating substantial real-world diagrams.
/// </remarks>
public sealed record ResourceLimits
{
	/// <summary>
	/// Maximum characters of raw diagram source accepted before any parsing.
	/// Guards against memory exhaustion from oversized input. Default: 512 KB.
	/// </summary>
	public int MaxInputLength { get; init; } = 512 * 1024;

	/// <summary>
	/// Maximum number of non-blank, non-comment lines after preprocessing.
	/// Bounds aggregate regex-timeout budget (per-regex 2 s × many lines).
	/// Default: 10,000.
	/// </summary>
	public int MaxLines { get; init; } = 10_000;

	/// <summary>
	/// Maximum characters per line after preprocessing.
	/// Complements per-regex ReDoS timeouts for very long single lines.
	/// Default: 8,000.
	/// </summary>
	public int MaxLineLength { get; init; } = 8_000;

	/// <summary>
	/// Maximum total parsed elements (nodes + edges, or sets + unions, rows, participants,
	/// etc.) across the entire diagram. Enforced after parsing, before layout.
	/// Default: 5,000.
	/// </summary>
	public int MaxElements { get; init; } = 5_000;

	/// <summary>
	/// Maximum total node count <em>after</em> the Sugiyama layout engine inserts virtual
	/// nodes for multi-layer-spanning edges. Guards against virtual-node amplification
	/// attacks where crafted long-span edges blow up the node count by O(E × L).
	/// Default: 20,000.
	/// </summary>
	public int MaxNodesAfterLayout { get; init; } = 20_000;

	/// <summary>
	/// Maximum recursion depth for tree-walking renderers (mindmap, treeView, treemap).
	/// Guards against stack exhaustion from deeply nested input.
	/// Default: 64.
	/// </summary>
	public int MaxRecursionDepth { get; init; } = 64;

	/// <summary>
	/// Maximum length (in characters) of the generated SVG string.
	/// Guards against output-amplification attacks where small input maps to huge SVG.
	/// Default: 8 MB.
	/// </summary>
	public int MaxOutputLength { get; init; } = 8 * 1024 * 1024;

	/// <summary>
	/// Time budget for the full parse + layout + render pipeline.
	/// The budget is cooperative: it is checked at phase boundaries and at hot-loop
	/// checkpoints inside the Sugiyama layout engine. It does <b>not</b> interrupt
	/// arbitrary external code (e.g. MSAGL or user-supplied layout providers).
	/// <c>null</c> disables the deadline. Default: 5 s.
	/// </summary>
	public TimeSpan? RenderDeadline { get; init; } = TimeSpan.FromSeconds(5);

	/// <summary>
	/// Time source used when scheduling the <see cref="RenderDeadline"/> timer.
	/// Defaults to <see cref="TimeProvider.System"/> (real wall-clock time).
	/// Override with a fake/test <see cref="TimeProvider"/> to control deadline
	/// firing in tests without relying on wall-clock delays.
	/// </summary>
	public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

	/// <summary>
	/// Default limits — on-by-default with generous values suitable for untrusted input.
	/// </summary>
	public static readonly ResourceLimits Default = new();

	/// <summary>
	/// Disables all resource limits. Use only for trusted, internally generated input
	/// where resource exhaustion is not a concern.
	/// </summary>
	public static readonly ResourceLimits Unlimited = new()
	{
		MaxInputLength = int.MaxValue,
		MaxLines = int.MaxValue,
		MaxLineLength = int.MaxValue,
		MaxElements = int.MaxValue,
		MaxNodesAfterLayout = int.MaxValue,
		MaxRecursionDepth = int.MaxValue,
		MaxOutputLength = int.MaxValue,
		RenderDeadline = null,
	};
}
