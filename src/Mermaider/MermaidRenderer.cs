using System.IO.Pipelines;
using System.Text;
using Mermaider.Layout;
using Mermaider.Models;
using Mermaider.Parsing;
using Mermaider.Rendering;

namespace Mermaider;

/// <summary>
/// Renders Mermaid diagram text to sanitized SVG strings suitable for direct HTML embedding.
/// Framework-agnostic, no DOM required. Pure .NET.
/// </summary>
public static class MermaidRenderer
{
	/// <summary>Canonical safe empty SVG returned when generated output is not well-formed XML.</summary>
	public const string FallbackSvg = SvgDocuments.Empty;

#pragma warning disable IDE1006
	private static volatile IGraphLayoutProvider _layoutProvider = DefaultLayoutProvider.Instance;
#pragma warning restore IDE1006

	/// <summary>
	/// Replace the built-in layout engine with a custom provider (e.g. MSAGL).
	/// Install the <c>Mermaider.Layout.Msagl</c> NuGet package for the MSAGL provider.
	/// </summary>
	public static void SetLayoutProvider(IGraphLayoutProvider provider) =>
		_layoutProvider = provider ?? throw new ArgumentNullException(nameof(provider));

	/// <summary>Returns the currently active layout provider.</summary>
	public static IGraphLayoutProvider LayoutProvider => _layoutProvider;

	/// <summary>
	/// Render Mermaid diagram text to a self-contained, sanitized SVG string.
	/// </summary>
	/// <param name="text">Mermaid source text (e.g. "graph TD\n  A --&gt; B")</param>
	/// <param name="options">Optional rendering configuration (colors, font, spacing).</param>
	/// <returns>A self-contained SVG string.</returns>
	/// <exception cref="MermaidParseException">Thrown when the input cannot be parsed.</exception>
	/// <exception cref="MermaidResourceLimitException">Thrown when a resource limit is exceeded.</exception>
	/// <exception cref="MermaidSvgException">Thrown when block mode rejects generated SVG.</exception>
	public static string RenderSvg(string text, RenderOptions? options = null)
		=> MermaidRenderPipeline.Execute(text, options);

	/// <summary>
	/// Render and sanitize Mermaid diagram text, then write the UTF-8 SVG to a <see cref="Stream"/>.
	/// The <paramref name="cancellationToken"/> is now honored throughout the full parse + layout + render
	/// pipeline, not only for the final write. When <see cref="RenderOptions.Limits"/> includes a
	/// <see cref="ResourceLimits.RenderDeadline"/>, that deadline is combined with this token.
	/// </summary>
	/// <param name="text">Mermaid source text.</param>
	/// <param name="destination">The stream to write the SVG to.</param>
	/// <param name="options">Optional rendering configuration.</param>
	/// <param name="cancellationToken">Cancellation token — honored throughout the full pipeline.</param>
	/// <exception cref="MermaidParseException">Thrown when the input cannot be parsed.</exception>
	/// <exception cref="MermaidResourceLimitException">Thrown when a resource limit is exceeded.</exception>
	/// <exception cref="MermaidSvgException">Thrown when block mode rejects generated SVG.</exception>
	/// <exception cref="OperationCanceledException">Thrown when the token or deadline fires.</exception>
	public static async Task RenderSvgAsync(string text, Stream destination, RenderOptions? options = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(destination);

		// Execute on a thread-pool thread so the CancellationToken can interrupt CPU-bound work
		// (the pipeline checks the token at phase boundaries and in hot layout loops).
		var svg = await Task.Run(() => MermaidRenderPipeline.Execute(text, options, cancellationToken), cancellationToken).ConfigureAwait(false);
		await destination.WriteAsync(Encoding.UTF8.GetBytes(svg), cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Render and sanitize Mermaid diagram text, then write the UTF-8 SVG to a <see cref="PipeWriter"/>.
	/// The <paramref name="cancellationToken"/> is honored throughout the full parse + layout + render
	/// pipeline, not only for the final flush.
	/// </summary>
	/// <param name="text">Mermaid source text.</param>
	/// <param name="destination">The pipe writer to write the SVG to.</param>
	/// <param name="options">Optional rendering configuration.</param>
	/// <param name="cancellationToken">Cancellation token — honored throughout the full pipeline.</param>
	/// <exception cref="MermaidParseException">Thrown when the input cannot be parsed.</exception>
	/// <exception cref="MermaidResourceLimitException">Thrown when a resource limit is exceeded.</exception>
	/// <exception cref="MermaidSvgException">Thrown when block mode rejects generated SVG.</exception>
	/// <exception cref="OperationCanceledException">Thrown when the token or deadline fires.</exception>
	public static async Task RenderSvgAsync(string text, PipeWriter destination, RenderOptions? options = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(destination);

		var svg = await Task.Run(() => MermaidRenderPipeline.Execute(text, options, cancellationToken), cancellationToken).ConfigureAwait(false);
		var bytes = Encoding.UTF8.GetBytes(svg);
		bytes.CopyTo(destination.GetMemory(bytes.Length).Span);
		destination.Advance(bytes.Length);
		_ = await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Parse Mermaid diagram text into a logical graph structure without rendering.
	/// </summary>
	/// <param name="text">Mermaid source text.</param>
	/// <returns>The parsed graph model.</returns>
	/// <exception cref="MermaidParseException">Thrown when the input cannot be parsed.</exception>
	public static MermaidGraph Parse(string text)
	{
		var (cleaned, _) = DiagramPreprocessor.Process(text);

		var lines = PreprocessLines(cleaned);
		if (lines.Length == 0)
			throw new MermaidParseException("Empty mermaid diagram");

		var diagramType = DiagramDetector.Detect(cleaned.AsSpan());

		return ParseInternal(lines, diagramType);
	}

	internal static MermaidGraph ParseInternal(string[] lines, DiagramType diagramType) =>
		diagramType switch
		{
			DiagramType.Flowchart => FlowchartParser.Parse(lines),
			DiagramType.State => StateParser.Parse(lines),
			_ => throw new MermaidParseException($"Diagram type '{diagramType}' is not yet supported.")
		};

	internal static string[] PreprocessLines(string text)
	{
		var rawLines = text.Split('\n');
		var count = 0;
		for (var i = 0; i < rawLines.Length; i++)
		{
			var trimmed = rawLines[i].Trim();
			if (trimmed.Length > 0 && !trimmed.StartsWith("%%", StringComparison.Ordinal))
			{
				rawLines[count] = trimmed;
				count++;
			}
		}
		return rawLines.AsSpan(0, count).ToArray();
	}

	internal static string[] PreprocessLinesPreserveIndent(string text, AccessibilityInfo? accessibility = null)
	{
		var rawLines = text.Split('\n');
		var result = new List<string>(rawLines.Length);
		for (var i = 0; i < rawLines.Length; i++)
		{
			var trimmed = rawLines[i].TrimEnd();
			var stripped = trimmed.Trim();
			if (stripped.Length == 0 || stripped.StartsWith("%%", StringComparison.Ordinal))
				continue;
			if (accessibility?.HasContent == true && IsAccessibilityLine(stripped, ref i, rawLines))
				continue;
			result.Add(trimmed);
		}
		return result.ToArray();
	}

	private static bool IsAccessibilityLine(string stripped, ref int i, string[] rawLines)
	{
		if (stripped.StartsWith("accTitle", StringComparison.Ordinal) && stripped.Contains(':'))
			return true;
		if (stripped.StartsWith("accDescr", StringComparison.Ordinal) && stripped.Contains(':'))
			return true;
		if (stripped.StartsWith("accDescr", StringComparison.Ordinal) && stripped.Contains('{'))
		{
			i++;
			while (i < rawLines.Length)
			{
				var inner = rawLines[i].Trim();
				if (inner == "}" || inner.StartsWith('}'))
					break;
				i++;
			}
			return true;
		}
		return false;
	}
}
