using System.Text.Json;
using Mermaider.Models;
using Mermaider.Theming;

namespace Mermaider.Native;

/// <summary>
/// The options a host passes to <c>mermaider_render_svg</c>, as a JSON object, turned into <see cref="RenderOptions"/>.
/// Strict on purpose: an unknown key, or a value of the wrong type, is an error the host sees, so a typo is not a
/// silently unthemed diagram. Read with <see cref="JsonDocument"/>, which needs no reflection (ahead-of-time safe).
/// </summary>
/// <remarks>
/// Keys: <c>theme</c> (a built-in theme name, the base for the colours), then colours that override it
/// (<c>bg fg line accent muted surface border default success failure warning info</c>), <c>dataPalette</c> (an array
/// of colours), <c>font monoFont fontSize</c> (strings), <c>style</c> (<c>quiet blueprint tonal</c>), <c>gradient</c>
/// <c>transparent</c> <c>roundedEdges</c> (booleans), <c>tint</c> <c>padding</c> <c>nodeSpacing</c> <c>layerSpacing</c>
/// (numbers) and <c>elevation</c> (0 to 2).
/// </remarks>
internal static class NativeOptions
{
	public static RenderOptions Parse(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
			return Apply(new RenderOptions(), null);

		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(json);
		}
		catch (JsonException e)
		{
			throw new ArgumentException($"options are not valid JSON: {e.Message}");
		}

		using (document)
		{
			if (document.RootElement.ValueKind != JsonValueKind.Object)
				throw new ArgumentException("options must be a JSON object");
			return Apply(new RenderOptions(), document.RootElement);
		}
	}

	private static RenderOptions Apply(RenderOptions options, JsonElement? root)
	{
		if (root is not { } o)
			return options with { Bg = Themes.Default.Bg, Fg = Themes.Default.Fg };

		var colors = Themes.Default;
		if (o.TryGetProperty("theme", out var theme))
		{
			var name = Text(theme, "theme");
			if (!Themes.BuiltIn.TryGetValue(name, out colors))
				throw new ArgumentException($"unknown theme '{name}'");
		}

		options = options with
		{
			Bg = colors.Bg,
			Fg = colors.Fg,
			Line = colors.Line,
			Accent = colors.Accent,
			Muted = colors.Muted,
			Surface = colors.Surface,
			Border = colors.Border,
			DataPalette = colors.DataPalette,
			Default = colors.Default,
			Success = colors.Success,
			Failure = colors.Failure,
			Warning = colors.Warning,
			Info = colors.Info,
		};

		foreach (var property in o.EnumerateObject())
		{
			var value = property.Value;
			options = property.Name switch
			{
				"theme" => options,
				"bg" => options with { Bg = Text(value, "bg") },
				"fg" => options with { Fg = Text(value, "fg") },
				"line" => options with { Line = Text(value, "line") },
				"accent" => options with { Accent = Text(value, "accent") },
				"muted" => options with { Muted = Text(value, "muted") },
				"surface" => options with { Surface = Text(value, "surface") },
				"border" => options with { Border = Text(value, "border") },
				"default" => options with { Default = Text(value, "default") },
				"success" => options with { Success = Text(value, "success") },
				"failure" => options with { Failure = Text(value, "failure") },
				"warning" => options with { Warning = Text(value, "warning") },
				"info" => options with { Info = Text(value, "info") },
				"dataPalette" => options with { DataPalette = Texts(value, "dataPalette") },
				"font" => options with { Font = Text(value, "font") },
				"monoFont" => options with { MonoFont = Text(value, "monoFont") },
				"fontSize" => options with { FontSize = Text(value, "fontSize") },
				"style" => options with { Style = Style(Text(value, "style")) },
				"gradient" => options with { Gradient = Flag(value, "gradient") },
				"transparent" => options with { Transparent = Flag(value, "transparent") },
				"roundedEdges" => options with { RoundedEdges = Flag(value, "roundedEdges") },
				"tint" => options with { Tint = Number(value, "tint") },
				"padding" => options with { Padding = Number(value, "padding") },
				"nodeSpacing" => options with { NodeSpacing = Number(value, "nodeSpacing") },
				"layerSpacing" => options with { LayerSpacing = Number(value, "layerSpacing") },
				"elevation" => options with { Elevation = (int)Number(value, "elevation") },
				_ => throw new ArgumentException($"unknown option '{property.Name}'"),
			};
		}

		return options;
	}

	private static string Text(JsonElement value, string key) =>
		value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
			? text
			: throw new ArgumentException($"option '{key}' must be a non-empty string");

	private static string[] Texts(JsonElement value, string key)
	{
		if (value.ValueKind != JsonValueKind.Array)
			throw new ArgumentException($"option '{key}' must be an array of strings");
		return value.EnumerateArray().Select(item => Text(item, key)).ToArray();
	}

	private static bool Flag(JsonElement value, string key) =>
		value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : throw new ArgumentException($"option '{key}' must be true or false");

	private static double Number(JsonElement value, string key) =>
		value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : throw new ArgumentException($"option '{key}' must be a number");

	private static DiagramStyle Style(string name) => name.ToLowerInvariant() switch
	{
		"quiet" => DiagramStyle.Quiet,
		"blueprint" => DiagramStyle.Blueprint,
		"tonal" => DiagramStyle.Tonal,
		_ => throw new ArgumentException($"unknown style '{name}' (quiet, blueprint or tonal)"),
	};
}
