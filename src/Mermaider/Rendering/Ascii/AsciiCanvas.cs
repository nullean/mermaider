using System.Text;

namespace Mermaider.Rendering.Ascii;

/// <summary>
/// A fixed grid of characters with two layers. Lines live in a bitmask per cell — which of north, east,
/// south and west leave it — and glyphs live in a char array above them. Drawing a line into a cell that
/// already has one therefore joins the two rather than overwriting one with the other, which is what makes a
/// crossing come out as a cross and a line meeting a box come out as a tee, with no table of pairs to get
/// wrong.
/// </summary>
internal sealed class AsciiCanvas
{
	private const byte North = 1;
	private const byte East = 2;
	private const byte South = 4;
	private const byte West = 8;

	private static readonly char[] Rounded =
	[
		' ', '│', '─', '└', '│', '│', '┌', '├',
		'─', '┘', '─', '┴', '┐', '┤', '┬', '┼',
	];

	private static readonly char[] Plain =
	[
		' ', '|', '-', '+', '|', '|', '+', '+',
		'-', '+', '-', '+', '+', '+', '+', '+',
	];

	private readonly byte[] _lines;
	private readonly char[] _glyphs;
	private readonly bool _ascii;

	public AsciiCanvas(int width, int height, bool ascii)
	{
		Width = Math.Max(1, width);
		Height = Math.Max(1, height);
		_lines = new byte[Width * Height];
		_glyphs = new char[Width * Height];
		_ascii = ascii;
	}

	public int Width { get; }

	public int Height { get; }

	/// <summary>A character that is not a line: a label, an arrowhead, a tick.</summary>
	public void Glyph(int x, int y, char value)
	{
		if (Inside(x, y))
			_glyphs[(y * Width) + x] = value;
	}

	public void Text(int x, int y, string? value)
	{
		if (value is null)
			return;
		for (var i = 0; i < value.Length; i++)
			Glyph(x + i, y, value[i]);
	}

	/// <summary>Centres text in a span, truncating with an ellipsis rather than running over the edge.</summary>
	public void Centred(int x, int width, int y, string value)
	{
		var text = Fit(value, width, _ascii);
		Text(x + Math.Max(0, (width - text.Length) / 2), y, text);
	}

	/// <summary>
	/// Text cut to a width, with an ellipsis where it was cut. In plain mode the ellipsis is three dots and
	/// therefore three cells, which is the whole reason this takes the flag: a single character that is not
	/// ASCII would defeat the option somebody chose plain for.
	/// </summary>
	public static string Fit(string value, int width, bool ascii = false)
	{
		if (width <= 0)
			return string.Empty;
		if (value.Length <= width)
			return value;
		if (!ascii)
			return width <= 1 ? value[..1] : value[..(width - 1)] + "…";
		return width <= 3 ? value[..width] : value[..(width - 3)] + "...";
	}

	public void Horizontal(int from, int to, int y)
	{
		var (low, high) = from <= to ? (from, to) : (to, from);
		for (var x = low; x <= high; x++)
			Join(x, y, (byte)((x > low ? West : 0) | (x < high ? East : 0)));
	}

	public void Vertical(int x, int from, int to)
	{
		var (low, high) = from <= to ? (from, to) : (to, from);
		for (var y = low; y <= high; y++)
			Join(x, y, (byte)((y > low ? North : 0) | (y < high ? South : 0)));
	}

	/// <summary>A rectangle whose sides are lines, so anything arriving at one joins it.</summary>
	public void Box(int x, int y, int width, int height)
	{
		if (width < 2 || height < 2)
			return;
		var right = x + width - 1;
		var bottom = y + height - 1;
		Horizontal(x, right, y);
		Horizontal(x, right, bottom);
		Vertical(x, y, bottom);
		Vertical(right, y, bottom);
	}

	/// <summary>
	/// A frame drawn in glyphs rather than lines, so an edge crossing it is not joined to it, and only into
	/// cells that are still empty, so it never cuts through what it is drawn around.
	/// </summary>
	public void Frame(int x, int y, int width, int height, char horizontal, char vertical)
	{
		if (width < 2 || height < 2)
			return;
		var right = x + width - 1;
		var bottom = y + height - 1;
		for (var i = x; i <= right; i++)
		{
			Vacant(i, y, horizontal);
			Vacant(i, bottom, horizontal);
		}

		for (var i = y + 1; i < bottom; i++)
		{
			Vacant(x, i, vertical);
			Vacant(right, i, vertical);
		}
	}

	private void Vacant(int x, int y, char value)
	{
		if (Inside(x, y) && _glyphs[(y * Width) + x] == '\0' && _lines[(y * Width) + x] == 0)
			_glyphs[(y * Width) + x] = value;
	}

	public char Arrow(char direction) => (_ascii, direction) switch
	{
		(true, _) => direction,
		(false, '>') => '▶',
		(false, '<') => '◀',
		(false, 'v') => '▼',
		(false, '^') => '▲',
		_ => direction,
	};

	public override string ToString()
	{
		var glyphs = _ascii ? Plain : Rounded;
		var text = new StringBuilder((Width + 1) * Height);
		for (var y = 0; y < Height; y++)
		{
			var line = new char[Width];
			var last = -1;
			for (var x = 0; x < Width; x++)
			{
				var at = (y * Width) + x;
				var glyph = _glyphs[at] != '\0' ? _glyphs[at] : glyphs[_lines[at]];
				line[x] = glyph;
				if (glyph != ' ')
					last = x;
			}

			_ = text.Append(line, 0, last + 1).Append('\n');
		}

		return text.ToString().TrimEnd('\n') + "\n";
	}

	private void Join(int x, int y, byte directions)
	{
		if (Inside(x, y))
			_lines[(y * Width) + x] |= directions;
	}

	private bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
}
