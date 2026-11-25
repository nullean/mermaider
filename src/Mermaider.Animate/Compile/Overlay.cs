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

	/// <summary>A token: a dot with a soft halo, and a filled label pill above it so a moving thing says what it is.</summary>
	internal static XElement Token(string color, bool hollow, double radius, string? label, out XElement? labelElement)
	{
		var token = new XElement(N("g"), new XAttribute("opacity", "0"));
		token.Add(new XElement(N("circle"), new XAttribute("r", Num.F(radius + 5)), new XAttribute("fill", color), new XAttribute("opacity", "0.18")));
		token.Add(
			new XElement(
				N("circle"),
				new XAttribute("r", Num.F(radius)),
				new XAttribute("fill", hollow ? "var(--bg)" : color),
				new XAttribute("stroke", hollow ? color : "var(--bg)"),
				new XAttribute("stroke-width", hollow ? "2.25" : "1.5")
			)
		);
		labelElement = label is null ? null : Label(label, color, -radius - 17, filled: !hollow);
		if (labelElement is not null)
			token.Add(labelElement);
		return token;
	}

	/// <summary>A pill with a short text: filled with the colour, or outlined in it for replies.</summary>
	internal static XElement Label(string text, string color, double dy, bool filled = true)
	{
		var width = (text.Length * 6.9) + 18;
		return new XElement(
			N("g"),
			new XElement(
				N("rect"),
				new XAttribute("x", Num.F(-width / 2)),
				new XAttribute("y", Num.F(dy - 10)),
				new XAttribute("width", Num.F(width)),
				new XAttribute("height", "20"),
				new XAttribute("rx", "10"),
				new XAttribute("fill", filled ? color : "var(--bg)"),
				new XAttribute("stroke", color),
				new XAttribute("stroke-width", "1.5")
			),
			new XElement(
				N("text"),
				new XAttribute("x", "0"),
				new XAttribute("y", Num.F(dy + 4)),
				new XAttribute("text-anchor", "middle"),
				new XAttribute("font-size", "11.5"),
				new XAttribute("font-weight", "650"),
				new XAttribute("fill", filled ? "#fff" : color),
				text
			)
		);
	}

	/// <summary>A badge pinned above a node's top edge.</summary>
	internal static XElement Badge(string text, string color, Box box)
	{
		var badge = Label(text, color, 0);
		// clear of the node's top edge, where a mark's check or cross sits
		badge.SetAttributeValue("transform", $"translate({Num.F(box.Center.X)} {Num.F(box.Y - 18)})");
		return new XElement(N("g"), new XAttribute("opacity", "0"), badge);
	}

	/// <summary>The check or cross a mark adds at a node's top-right corner.</summary>
	internal static XElement MarkIcon(Score.Role role, string color, Box box)
	{
		var x = box.Right - 2;
		var y = box.Y + 2;
		var glyph = role switch
		{
			Score.Role.Ok => $"M{Num.F(x - 4)},{Num.F(y)} l3,3 l5.5,-6",
			Score.Role.Fail => $"M{Num.F(x - 3.5)},{Num.F(y - 3.5)} l7,7 M{Num.F(x + 3.5)},{Num.F(y - 3.5)} l-7,7",
			Score.Role.Warn => $"M{Num.F(x)},{Num.F(y - 4.5)} l0,5 M{Num.F(x)},{Num.F(y + 3)} l0,1",
			_ => $"M{Num.F(x)},{Num.F(y - 1)} l0,5 M{Num.F(x)},{Num.F(y - 4.5)} l0,1",
		};
		return new XElement(
			N("g"),
			new XElement(
				N("circle"),
				new XAttribute("cx", Num.F(x)),
				new XAttribute("cy", Num.F(y)),
				new XAttribute("r", "9"),
				new XAttribute("fill", color),
				new XAttribute("stroke", "var(--bg)"),
				new XAttribute("stroke-width", "1.5")
			),
			new XElement(
				N("path"),
				new XAttribute("d", glyph),
				new XAttribute("stroke", "#fff"),
				new XAttribute("stroke-width", "2"),
				new XAttribute("stroke-linecap", "round"),
				new XAttribute("stroke-linejoin", "round"),
				new XAttribute("fill", "none")
			)
		);
	}

	/// <summary>One slot of a fill meter along the bottom inside of a node.</summary>
	internal static XElement Slot(Box box, int index, int slots, string color, bool track)
	{
		const double gap = 2;
		var inner = box.Width - 16;
		var width = (inner - ((slots - 1) * gap)) / slots;
		var x = box.X + 8 + (index * (width + gap));
		return new XElement(
			N("rect"),
			new XAttribute("x", Num.F(track ? box.X + 8 : x)),
			new XAttribute("y", Num.F(box.Bottom - 10)),
			new XAttribute("width", Num.F(track ? inner : width)),
			new XAttribute("height", "5"),
			new XAttribute("rx", "1.5"),
			new XAttribute("fill", color),
			new XAttribute("opacity", "0")
		);
	}

	/// <summary>A dash that runs around a node while it waits.</summary>
	internal static XElement HangRing(Box box, string color)
	{
		var b = box.Inflate(6);
		return new XElement(
			N("rect"),
			new XAttribute("x", Num.F(b.X)),
			new XAttribute("y", Num.F(b.Y)),
			new XAttribute("width", Num.F(b.Width)),
			new XAttribute("height", Num.F(b.Height)),
			new XAttribute("rx", "13"),
			new XAttribute("fill", "none"),
			new XAttribute("stroke", color),
			new XAttribute("stroke-width", "3"),
			new XAttribute("stroke-linecap", "round"),
			new XAttribute("pathLength", "1"),
			new XAttribute("stroke-dasharray", "0.16 0.84"),
			new XAttribute("opacity", "0")
		);
	}

	/// <summary>A bracket beside a span of messages, with its label.</summary>
	internal static XElement Bracket(double x, double y1, double y2, string text, string color) =>
		new(
			N("g"),
			new XAttribute("opacity", "0"),
			new XElement(
				N("path"),
				new XAttribute("d", $"M{Num.F(x)},{Num.F(y1)} h7 V{Num.F(y2)} h-7"),
				new XAttribute("fill", "none"),
				new XAttribute("stroke", color),
				new XAttribute("stroke-width", "2"),
				new XAttribute("stroke-linejoin", "round")
			),
			new XElement(
				N("text"),
				new XAttribute("x", Num.F(x + 13)),
				new XAttribute("y", Num.F(((y1 + y2) / 2) + 4)),
				new XAttribute("font-size", "12"),
				new XAttribute("font-weight", "700"),
				new XAttribute("fill", color),
				text
			)
		);

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

	/// <summary>The progress line under a caption: drawn from left to right over the step.</summary>
	internal static XElement Progress(double x1, double x2, double y) =>
		new(
			N("path"),
			new XAttribute("d", $"M{Num.F(x1)},{Num.F(y)} L{Num.F(x2)},{Num.F(y)}"),
			new XAttribute("stroke", "var(--accent, #3b82f6)"),
			new XAttribute("stroke-width", "2"),
			new XAttribute("stroke-opacity", "0.55"),
			new XAttribute("pathLength", "1"),
			new XAttribute("stroke-dasharray", "1 1"),
			new XAttribute("stroke-dashoffset", "1"),
			new XAttribute("fill", "none")
		);

	/// <summary>A step's caption at the top left, with its number: "2/3  The balancer fails over".</summary>
	internal static XElement Caption(string text, int number, int total, double x, double y)
	{
		var caption = new XElement(N("g"), new XAttribute("opacity", "0"));
		if (total > 1)
		{
			var counter = $"{number}/{total}";
			var width = (counter.Length * 7) + 16;
			caption.Add(
				new XElement(
					N("rect"),
					new XAttribute("x", Num.F(x)),
					new XAttribute("y", Num.F(y - 15)),
					new XAttribute("width", Num.F(width)),
					new XAttribute("height", "21"),
					new XAttribute("rx", "10.5"),
					new XAttribute("fill", "var(--_text, var(--fg))")
				)
			);
			caption.Add(
				new XElement(
					N("text"),
					new XAttribute("x", Num.F(x + (width / 2))),
					new XAttribute("y", Num.F(y)),
					new XAttribute("text-anchor", "middle"),
					new XAttribute("font-size", "11.5"),
					new XAttribute("font-weight", "700"),
					new XAttribute("fill", "var(--bg)"),
					counter
				)
			);
			x += width + 9;
		}
		caption.Add(
			new XElement(
				N("text"),
				new XAttribute("x", Num.F(x)),
				new XAttribute("y", Num.F(y + 0.5)),
				new XAttribute("font-size", "15"),
				new XAttribute("font-weight", "650"),
				new XAttribute("fill", "var(--_text, var(--fg))"),
				text
			)
		);
		return caption;
	}
}
