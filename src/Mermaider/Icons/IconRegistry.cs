using System.Collections.Concurrent;

namespace Mermaider.Icons;

/// <summary>
/// Maps icon names to sanitized, embeddable SVG markup for architecture-diagram services and groups.
/// <para>
/// Ships a small built-in set — Mermaid's default architecture icons (<c>cloud</c>, <c>database</c>,
/// <c>disk</c>, <c>internet</c>, <c>server</c>), a <c>generic</c> fallback glyph, and a curated
/// selection of common AWS / GCP / Azure service glyphs plus the full Elastic set (see
/// <see cref="Names"/> for the exact list). These are original, simplified pictograms — not the
/// vendors' official trademarked logos — meant to render something reasonable out of the box.
/// Register real/licensed vendor logos via <see cref="Register"/> if you have the rights to use them.
/// </para>
/// </summary>
public static class IconRegistry
{
	/// <summary>The icon name used when a requested icon isn't registered.</summary>
	public const string FallbackName = "generic";

	private static readonly ConcurrentDictionary<string, string> Custom = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Registers (or overwrites) an icon under <paramref name="name"/>. Custom registrations take
	/// priority over built-in icons of the same name. The SVG is validated and sanitized before
	/// being stored: malformed markup, scripts, event handlers, and external references are rejected.
	/// </summary>
	/// <param name="name">The icon name, e.g. <c>"server"</c> or <c>"aws:ec2"</c>. Case-insensitive.</param>
	/// <param name="svg">Raw SVG markup for the icon (must have an <c>&lt;svg&gt;</c> root element).</param>
	/// <exception cref="MermaidParseException">Thrown when <paramref name="svg"/> is not well-formed SVG.</exception>
	public static void Register(string name, string svg)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentException.ThrowIfNullOrWhiteSpace(svg);
		Custom[name] = IconValidation.ValidateAndNormalize(name, svg);
	}

	/// <summary>Removes a previously registered custom icon. Built-in icons cannot be removed.</summary>
	/// <returns><c>true</c> if a custom icon with that name was found and removed.</returns>
	public static bool Unregister(string name) => Custom.TryRemove(name, out _);

	/// <summary>
	/// Resolves an icon name to sanitized SVG markup, falling back to the <see cref="FallbackName"/>
	/// glyph when <paramref name="name"/> is null, blank, or not registered.
	/// </summary>
	public static string Resolve(string? name)
	{
		if (!string.IsNullOrWhiteSpace(name) && TryGet(name, out var svg))
			return svg;
		return BuiltInIcons.Map[FallbackName];
	}

	/// <summary>Attempts to resolve an icon by name (custom registrations take priority over built-ins).</summary>
	public static bool TryGet(string name, out string svg)
	{
		if (Custom.TryGetValue(name, out var custom))
		{
			svg = custom;
			return true;
		}

		if (BuiltInIcons.Map.TryGetValue(name, out var builtin))
		{
			svg = builtin;
			return true;
		}

		svg = "";
		return false;
	}

	/// <summary>All icon names currently resolvable (built-in union custom registrations).</summary>
	public static IEnumerable<string> Names =>
		BuiltInIcons.Map.Keys.Concat(Custom.Keys).Distinct(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Looks up the light/dark gradient pair a built-in vendor icon wants painted on the whole
	/// service box (not just the icon glyph). Only built-in vendor icons (e.g. <c>aws:compute</c>)
	/// have an entry — default-pack icons and custom user registrations return <c>false</c> and
	/// render inside the plain themed node box instead.
	/// </summary>
	internal static bool TryGetBadgeGradient(string name, out (string Light, string Dark) gradient) =>
		BuiltInIcons.BadgeGradients.TryGetValue(name, out gradient);
}
