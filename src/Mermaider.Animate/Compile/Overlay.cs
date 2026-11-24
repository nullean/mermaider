using System.Xml.Linq;
using Mermaider.Animate.Scene;

namespace Mermaider.Animate.Compile;

/// <summary>The role colours, read from the render options or the default theme, and only ever a validated hex.</summary>
internal sealed record RoleColors(string Ok, string Fail, string Warn, string Info)
{
	internal static RoleColors From(RenderOptions? options)
	{
		var theme = Themes.Default;
		return new RoleColors(
			Pick(options?.Success, theme.Success, "#59a14f"),
			Pick(options?.Failure, theme.Failure, "#e15759"),
			Pick(options?.Warning, theme.Warning, "#edc948"),
			Pick(options?.Info, theme.Info, "#4e79a7")
		);
	}

	internal string For(Score.Role role) => role switch
	{
		Score.Role.Ok => Ok,
		Score.Role.Fail => Fail,
		Score.Role.Warn => Warn,
		_ => Info,
	};

	private static string Pick(string? preferred, string? theme, string fallback) =>
		IsHex(preferred) ? preferred!.Trim() : IsHex(theme) ? theme!.Trim() : fallback;

	private static bool IsHex(string? value)
	{
		if (value is null)
			return false;
		var v = value.Trim();
		if (v.Length is not (4 or 7 or 9) || v[0] != '#')
			return false;
		for (var i = 1; i < v.Length; i++)
			if (!Uri.IsHexDigit(v[i]))
				return false;
		return true;
	}
}

/// <summary>Builds the overlay's elements: tokens, rings, marks, arrowheads, stream dashes and captions.</summary>
internal static class Overlay
{
	private static XName N(string name) => SvgScene.Ns + name;

	internal static XElement Group(params object[] content) => new(N("g"), content);

	internal static XElement Token(string fill, string stroke, double radius, string? label, out XElement? labelElement)
	{
		var token = new XElement(N("g"), new XAttribute("opacity", "0"));
		token.Add(
			new XElement(
				N("circle"),
				new XAttribute("r", Num.F(radius)),
				new XAttribute("fill", fill),
				new XAttribute("stroke", stroke),
				new XAttribute("stroke-width", "2")
			)
		);
		labelElement = label is null ? null : Label(label, stroke == "var(--bg)" ? fill : stroke, -radius - 14);
		if (labelElement is not null)
			token.Add(labelElement);
		return token;
	}

	/// <summary>A small pill above a token, so a moving thing can say what it is.</summary>
	internal static XElement Label(string text, string color, double dy)
	{
		var width = (text.Length * 6.4) + 14;
		return new XElement(
			N("g"),
			new XElement(
				N("rect"),
				new XAttribute("x", Num.F(-width / 2)),
				new XAttribute("y", Num.F(dy - 9)),
				new XAttribute("width", Num.F(width)),
				new XAttribute("height", "18"),
				new XAttribute("rx", "9"),
				new XAttribute("fill", "var(--bg)"),
				new XAttribute("stroke", color),
				new XAttribute("stroke-width", "1.25")
			),
			new XElement(
				N("text"),
				new XAttribute("x", "0"),
				new XAttribute("y", Num.F(dy + 4)),
				new XAttribute("text-anchor", "middle"),
				new XAttribute("font-size", "11"),
				new XAttribute("font-weight", "500"),
				new XAttribute("fill", "var(--_text)"),
				text
			)
		);
	}

	internal static XElement Ring(Box box, string color, double width, double inflate, string? fill = null, double fillOpacity = 0)
	{
		var b = box.Inflate(inflate);
		return new XElement(
			N("rect"),
			new XAttribute("x", Num.F(b.X)),
			new XAttribute("y", Num.F(b.Y)),
			new XAttribute("width", Num.F(b.Width)),
			new XAttribute("height", Num.F(b.Height)),
			new XAttribute("rx", "11"),
			new XAttribute("fill", fill ?? "none"),
			new XAttribute("fill-opacity", Num.F3(fillOpacity)),
			new XAttribute("stroke", color),
			new XAttribute("stroke-width", Num.F(width)),
			new XAttribute("opacity", "0")
		);
	}

	internal static XElement Stroke(Polyline path, string color, double width, string? dash = null, bool round = false)
	{
		var element = new XElement(
			N("path"),
			new XAttribute("d", path.ToPathData()),
			new XAttribute("fill", "none"),
			new XAttribute("stroke", color),
			new XAttribute("stroke-width", Num.F(width)),
			new XAttribute("opacity", "0")
		);
		if (dash is not null)
			element.SetAttributeValue("stroke-dasharray", dash);
		if (round)
			element.SetAttributeValue("stroke-linecap", "round");
		return element;
	}

	internal static XElement Cross(Vec at, string color)
	{
		const double s = 5;
		var d =
			$"M{Num.F(at.X - s)},{Num.F(at.Y - s)} L{Num.F(at.X + s)},{Num.F(at.Y + s)} M{Num.F(at.X + s)},{Num.F(at.Y - s)} L{Num.F(at.X - s)},{Num.F(at.Y + s)}";
		return new XElement(
			N("g"),
			new XAttribute("opacity", "0"),
			new XElement(
				N("circle"),
				new XAttribute("cx", Num.F(at.X)),
				new XAttribute("cy", Num.F(at.Y)),
				new XAttribute("r", "10"),
				new XAttribute("fill", "var(--bg)"),
				new XAttribute("stroke", color),
				new XAttribute("stroke-width", "1.5")
			),
			new XElement(
				N("path"),
				new XAttribute("d", d),
				new XAttribute("stroke", color),
				new XAttribute("stroke-width", "2"),
				new XAttribute("stroke-linecap", "round")
			)
		);
	}

	/// <summary>A copy of a marker's shape placed at a path's end, so it can arrive after the line instead of with it.</summary>
	internal static XElement Arrowhead(XElement marker, Vec at, double angle)
	{
		var refX = Num.Parse((string?)marker.Attribute("refX") ?? "0");
		var refY = Num.Parse((string?)marker.Attribute("refY") ?? "0");
		var inner = new XElement(
			N("g"),
			new XAttribute("transform", $"translate({Num.F(-refX)} {Num.F(-refY)})"),
			marker.Elements().Select(e => new XElement(e))
		);
		return new XElement(N("g"), new XAttribute("transform", $"translate({Num.F(at.X)} {Num.F(at.Y)}) rotate({Num.F(angle)})"), inner);
	}

	internal static XElement Caption(string text, double x, double y) =>
		new(
			N("text"),
			new XAttribute("x", Num.F(x)),
			new XAttribute("y", Num.F(y)),
			new XAttribute("text-anchor", "middle"),
			new XAttribute("font-size", "13"),
			new XAttribute("font-weight", "500"),
			new XAttribute("fill", "var(--_text-sec, var(--fg))"),
			new XAttribute("opacity", "0"),
			text
		);
}
