using System.IO.Pipelines;
using System.Text;
using AwesomeAssertions;
using Mermaider.Models;

namespace Mermaider.Tests.Rendering;

public class MermaidRenderPipelineTests
{
	private const string Source = "graph TD\nA[Start] --> B[End]";

	[Test]
	public async Task String_stream_and_pipe_entry_points_share_the_same_sanitized_pipeline()
	{
		var options = new RenderOptions { SanitizeMode = SanitizeMode.Block };
		var expected = MermaidRenderer.RenderSvg(Source, options);

		using var stream = new MemoryStream();
		await MermaidRenderer.RenderSvgAsync(Source, stream, options);
		var streamSvg = Encoding.UTF8.GetString(stream.ToArray());

		using var pipeStream = new MemoryStream();
		var writer = PipeWriter.Create(pipeStream, new StreamPipeWriterOptions(leaveOpen: true));
		await MermaidRenderer.RenderSvgAsync(Source, writer, options);
		await writer.CompleteAsync();
		var pipeSvg = Encoding.UTF8.GetString(pipeStream.ToArray());

		streamSvg.Should().Be(expected);
		pipeSvg.Should().Be(expected);
		SvgSanitizer.SanitizeRendererOutput(expected).HasViolations.Should().BeFalse();
	}

	[Test]
	public async Task Parse_failures_use_MermaidParseException_before_any_destination_write()
	{
		using var stream = new MemoryStream();
		var act = async () => await MermaidRenderer.RenderSvgAsync("   ", stream);

		await act.Should().ThrowExactlyAsync<MermaidParseException>();
		stream.Length.Should().Be(0);
	}

	[Test]
	public async Task Unknown_sanitize_mode_is_rejected_before_any_destination_write()
	{
		using var stream = new MemoryStream();
		var options = new RenderOptions { SanitizeMode = (SanitizeMode)int.MaxValue };
		var act = async () => await MermaidRenderer.RenderSvgAsync(Source, stream, options);

		await act.Should().ThrowExactlyAsync<ArgumentOutOfRangeException>();
		stream.Length.Should().Be(0);
	}
}
