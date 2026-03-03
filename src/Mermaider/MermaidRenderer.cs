using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using Mermaider.Layout;
using Mermaider.Models;
using Mermaider.Parsing;
using Mermaider.Rendering;
using Mermaider.Theming;

namespace Mermaider;

/// <summary>
/// Renders Mermaid diagram text to SVG strings.
/// Framework-agnostic, no DOM required. Pure .NET.
/// </summary>
public static class MermaidRenderer
{
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
	/// Render Mermaid diagram text to a self-contained SVG string.
	/// </summary>
	/// <param name="text">Mermaid source text (e.g. "graph TD\n  A --&gt; B")</param>
	/// <param name="options">Optional rendering configuration (colors, font, spacing).</param>
	/// <returns>A self-contained SVG string.</returns>
	/// <exception cref="MermaidParseException">Thrown when the input cannot be parsed.</exception>
	public static string RenderSvg(string text, RenderOptions? options = null)
	{
		var (strict, sb) = RenderToBuilder(text, options);
		try
		{
			var svg = sb.ToString();
			if (strict?.Sanitize is { } sanitizeMode)
				svg = StrictModeSanitizer.Sanitize(svg, sanitizeMode);
			return svg;
		}
		finally
		{
			_ = sb.Clear();
			SharedStringBuilderPool.Instance.Return(sb);
		}
	}

	/// <summary>
	/// Render Mermaid diagram text and write the SVG to a <see cref="Stream"/>.
	/// Writes UTF-8 encoded content in chunks without materializing the full string.
	/// </summary>
	/// <param name="text">Mermaid source text.</param>
	/// <param name="destination">The stream to write the SVG to.</param>
	/// <param name="options">Optional rendering configuration.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <exception cref="MermaidParseException">Thrown when the input cannot be parsed.</exception>
	public static async Task RenderSvgAsync(string text, Stream destination, RenderOptions? options = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(destination);

		var (strict, sb) = RenderToBuilder(text, options);
		try
		{
			if (strict?.Sanitize is { } sanitizeMode)
			{
				var svg = StrictModeSanitizer.Sanitize(sb.ToString(), sanitizeMode);
				await destination.WriteAsync(Encoding.UTF8.GetBytes(svg), cancellationToken).ConfigureAwait(false);
				return;
			}
			await WriteBuilderToStreamAsync(sb, destination, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			_ = sb.Clear();
			SharedStringBuilderPool.Instance.Return(sb);
		}
	}

	/// <summary>
	/// Render Mermaid diagram text and write the SVG to a <see cref="PipeWriter"/>.
	/// Writes UTF-8 encoded content in chunks without materializing the full string.
	/// </summary>
	/// <param name="text">Mermaid source text.</param>
	/// <param name="destination">The pipe writer to write the SVG to.</param>
	/// <param name="options">Optional rendering configuration.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <exception cref="MermaidParseException">Thrown when the input cannot be parsed.</exception>
	public static async Task RenderSvgAsync(string text, PipeWriter destination, RenderOptions? options = null, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(destination);

		var (strict, sb) = RenderToBuilder(text, options);
		try
		{
			if (strict?.Sanitize is { } sanitizeMode)
			{
				var svg = StrictModeSanitizer.Sanitize(sb.ToString(), sanitizeMode);
				var bytes = Encoding.UTF8.GetBytes(svg);
				bytes.CopyTo(destination.GetMemory(bytes.Length).Span);
				destination.Advance(bytes.Length);
				_ = await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
				return;
			}
			await WriteBuilderToPipeWriterAsync(sb, destination, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			_ = sb.Clear();
			SharedStringBuilderPool.Instance.Return(sb);
		}
	}

	/// <summary>
	/// Parse Mermaid diagram text into a logical graph structure without rendering.
	/// </summary>
	/// <param name="text">Mermaid source text.</param>
	/// <returns>The parsed graph model.</returns>
	/// <exception cref="MermaidParseException">Thrown when the input cannot be parsed.</exception>
	public static MermaidGraph Parse(string text)
	{
		var lines = PreprocessLines(text);
		if (lines.Length == 0)
			throw new MermaidParseException("Empty mermaid diagram");

		var diagramType = DiagramDetector.Detect(text.AsSpan());

		return ParseInternal(lines, diagramType);
	}

	private static (StrictModeOptions? Strict, StringBuilder Builder) RenderToBuilder(string text, RenderOptions? options)
	{
		var colors = BuildColors(options);
		var font = options?.Font ?? LayoutDefaults.Font;
		var transparent = options?.Transparent ?? true;
		var strict = options?.Strict;
		var provider = options?.LayoutProvider ?? _layoutProvider;

		var lines = PreprocessLines(text);
		if (lines.Length == 0)
			throw new MermaidParseException("Empty mermaid diagram");

		if (strict is not null)
			StrictModeValidator.Validate(lines, strict);

		var diagramType = DiagramDetector.Detect(text.AsSpan());

		var sb = diagramType switch
		{
			DiagramType.Sequence => SequenceSvgRenderer.RenderToBuilder(
				SequenceLayout.Layout(SequenceParser.Parse(lines)),
				colors, font, transparent, strict),

			DiagramType.Class => ClassSvgRenderer.RenderToBuilder(
				provider.LayoutClass(ClassParser.Parse(lines)),
				colors, font, transparent, strict),

			DiagramType.Er => ErSvgRenderer.RenderToBuilder(
				provider.LayoutEr(ErParser.Parse(lines)),
				colors, font, transparent, strict),

			_ => SvgRenderer.RenderToBuilder(
				provider.LayoutFlowchart(ParseInternal(lines, diagramType), options, strict),
				colors, font, transparent, strict),
		};

		return (strict, sb);
	}

	private static async Task WriteBuilderToStreamAsync(StringBuilder sb, Stream stream, CancellationToken cancellationToken)
	{
		var encoder = Encoding.UTF8.GetEncoder();
		var buffer = ArrayPool<byte>.Shared.Rent(4096);
		try
		{
			foreach (var chunk in sb.GetChunks())
			{
				var charArray = ArrayPool<char>.Shared.Rent(chunk.Length);
				chunk.Span.CopyTo(charArray);
				var totalChars = chunk.Length;
				var charsConsumed = 0;
				while (charsConsumed < totalChars)
				{
					encoder.Convert(charArray.AsSpan(charsConsumed, totalChars - charsConsumed), buffer, flush: false, out var used, out var bytesWritten, out _);
					charsConsumed += used;
					if (bytesWritten > 0)
						await stream.WriteAsync(buffer.AsMemory(0, bytesWritten), cancellationToken).ConfigureAwait(false);
				}
				ArrayPool<char>.Shared.Return(charArray);
			}
			encoder.Convert([], buffer, flush: true, out _, out var finalBytes, out _);
			if (finalBytes > 0)
				await stream.WriteAsync(buffer.AsMemory(0, finalBytes), cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}

	private static async Task WriteBuilderToPipeWriterAsync(StringBuilder sb, PipeWriter writer, CancellationToken cancellationToken)
	{
		var encoder = Encoding.UTF8.GetEncoder();
		foreach (var chunk in sb.GetChunks())
		{
			var charArray = ArrayPool<char>.Shared.Rent(chunk.Length);
			chunk.Span.CopyTo(charArray);
			var totalChars = chunk.Length;
			var charsConsumed = 0;
			while (charsConsumed < totalChars)
			{
				var memory = writer.GetMemory(1024);
				encoder.Convert(charArray.AsSpan(charsConsumed, totalChars - charsConsumed), memory.Span, flush: false, out var used, out var bytesWritten, out _);
				charsConsumed += used;
				writer.Advance(bytesWritten);

				if (writer.UnflushedBytes >= 4096)
					_ = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
			}
			ArrayPool<char>.Shared.Return(charArray);
		}

		var finalMemory = writer.GetMemory(16);
		encoder.Convert([], finalMemory.Span, flush: true, out _, out var finalBytes, out _);
		if (finalBytes > 0)
			writer.Advance(finalBytes);
		_ = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
	}

	private static MermaidGraph ParseInternal(string[] lines, DiagramType diagramType) =>
		diagramType switch
		{
			DiagramType.Flowchart => FlowchartParser.Parse(lines),
			DiagramType.State => StateParser.Parse(lines),
			_ => throw new MermaidParseException($"Diagram type '{diagramType}' is not yet supported.")
		};

	private static DiagramColors BuildColors(RenderOptions? options) =>
		new()
		{
			Bg = options?.Bg ?? "#FFFFFF",
			Fg = options?.Fg ?? "#27272A",
			Line = options?.Line,
			Accent = options?.Accent,
			Muted = options?.Muted,
			Surface = options?.Surface,
			Border = options?.Border,
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
}
