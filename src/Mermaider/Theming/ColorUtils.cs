using System.Globalization;

namespace Mermaider.Theming;

/// <summary>Hex color manipulation for auto-deriving dark mode variants.</summary>
internal static class ColorUtils
{
	internal static string InvertLightness(string hex)
	{
		var (r, g, b) = ParseHex(hex);
		var (h, s, l) = RgbToHsl(r, g, b);
		var (r2, g2, b2) = HslToRgb(h, s, 1.0 - l);
		return $"#{r2:X2}{g2:X2}{b2:X2}";
	}

	private static (byte R, byte G, byte B) ParseHex(string hex)
	{
		var span = hex.AsSpan();
		if (span.Length > 0 && span[0] == '#')
			span = span[1..];

		if (span.Length == 3)
		{
			var r = byte.Parse(span[..1], NumberStyles.HexNumber);
			var g = byte.Parse(span[1..2], NumberStyles.HexNumber);
			var b = byte.Parse(span[2..3], NumberStyles.HexNumber);
			return ((byte)(r * 17), (byte)(g * 17), (byte)(b * 17));
		}

		if (span.Length >= 6)
		{
			var r = byte.Parse(span[..2], NumberStyles.HexNumber);
			var g = byte.Parse(span[2..4], NumberStyles.HexNumber);
			var b = byte.Parse(span[4..6], NumberStyles.HexNumber);
			return (r, g, b);
		}

		return (128, 128, 128);
	}

	private static (double H, double S, double L) RgbToHsl(byte r, byte g, byte b)
	{
		var rf = r / 255.0;
		var gf = g / 255.0;
		var bf = b / 255.0;

		var max = Math.Max(rf, Math.Max(gf, bf));
		var min = Math.Min(rf, Math.Min(gf, bf));
		var l = (max + min) / 2.0;

		if (max == min)
			return (0, 0, l);

		var d = max - min;
		var s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);

		double h;
		if (max == rf)
			h = ((gf - bf) / d + (gf < bf ? 6 : 0)) / 6.0;
		else if (max == gf)
			h = ((bf - rf) / d + 2) / 6.0;
		else
			h = ((rf - gf) / d + 4) / 6.0;

		return (h, s, l);
	}

	private static (byte R, byte G, byte B) HslToRgb(double h, double s, double l)
	{
		if (s == 0)
		{
			var v = (byte)Math.Round(l * 255);
			return (v, v, v);
		}

		var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
		var p = 2 * l - q;

		return (
			(byte)Math.Round(HueToRgb(p, q, h + 1.0 / 3.0) * 255),
			(byte)Math.Round(HueToRgb(p, q, h) * 255),
			(byte)Math.Round(HueToRgb(p, q, h - 1.0 / 3.0) * 255)
		);
	}

	private static double HueToRgb(double p, double q, double t)
	{
		if (t < 0) t += 1;
		if (t > 1) t -= 1;
		if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
		if (t < 1.0 / 2.0) return q;
		if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
		return p;
	}
}
