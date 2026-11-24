using System.Runtime.InteropServices;
using System.Text;
using AwesomeAssertions;

namespace Mermaider.Native.Tests;

public unsafe class NativeExportsTests
{
	private const string Flowchart = "graph TD\n  A[Start] --> B{Ok?}\n  B -- yes --> C[Done]";

	private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text + "\0");

	private static string? Render(string source, string? options, out string? error)
	{
		var render = (delegate* unmanaged[Cdecl]<byte*, byte*, byte*>)&Exports.RenderSvg;
		var lastError = (delegate* unmanaged[Cdecl]<byte*>)&Exports.LastError;
		var free = (delegate* unmanaged[Cdecl]<byte*, void>)&Exports.Free;
		fixed (byte* text = Utf8(source))
			fixed (byte* json = options is null ? null : Utf8(options))
			{
				var svg = render(text, options is null ? null : json);
				var reason = lastError();
				error = reason == null ? null : Marshal.PtrToStringUTF8((nint)reason);
				if (svg == null)
					return null;
				var result = Marshal.PtrToStringUTF8((nint)svg);
				free(svg);
				return result;
			}
	}

	[Test]
	public void The_ABI_version_is_the_one_the_header_says()
	{
		var version = (delegate* unmanaged[Cdecl]<int>)&Exports.AbiVersion;
		version().Should().Be(NativeApi.AbiVersion).And.Be(1);
	}

	[Test]
	public void A_diagram_renders_with_the_colours_it_was_given()
	{
		var svg = Render(Flowchart, """{"bg":"#102030","fg":"#e0e8f0","accent":"#ff8800"}""", out var error);
		error.Should().BeNull();
		svg.Should().StartWith("<svg").And.Contain("data-id=\"A\"").And.Contain("#102030").And.Contain("#ff8800");
	}

	[Test]
	public void A_failure_is_null_with_a_reason_and_the_next_call_is_not_affected()
	{
		Render(Flowchart, """{"nope":1}""", out var error).Should().BeNull();
		error.Should().Contain("unknown option 'nope'");
		Render("not a diagram at all", null, out error).Should().BeNull();
		error.Should().NotBeNullOrEmpty();
		Render("graph LR\n  X-->Y", null, out error).Should().NotBeNull();
		error.Should().BeNull("a good call clears the reason");
	}

	[Test]
	public void Text_in_other_scripts_survives_the_trip()
	{
		var svg = Render("graph LR\n  A[日本語 ✓] --> B[naïve café]", null, out _);
		svg.Should().Contain("日本語 ✓").And.Contain("naïve café");
	}

	[Test]
	public void Every_thread_has_its_own_reason()
	{
		var errors = new string?[8];
		var threads = Enumerable
			.Range(0, errors.Length)
			.Select(i => new Thread(() =>
			{
				Render("graph TD\n  A-->B", $$"""{"nope{{i}}":1}""", out var error);
				errors[i] = error;
			}))
			.ToArray();
		foreach (var thread in threads)
			thread.Start();
		foreach (var thread in threads)
			thread.Join();
		for (var i = 0; i < errors.Length; i++)
			errors[i].Should().Contain($"nope{i}");
	}
}
