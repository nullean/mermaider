namespace Mermaider.Animate.Timeline;

/// <summary>A CSS cubic-bezier easing, evaluated here too so the frame sampler matches what the browser plays.</summary>
internal sealed record Easing(string Name, double X1, double Y1, double X2, double Y2)
{
	public static readonly Easing Linear = new("linear", 0, 0, 1, 1);

	/// <summary>Things arriving: fast out of the gate, settling gently.</summary>
	public static readonly Easing Enter = new("enter", 0.2, 0, 0, 1);

	/// <summary>Things leaving: gathering speed as they go.</summary>
	public static readonly Easing Exit = new("exit", 0.4, 0, 1, 1);

	/// <summary>Things moving from one place to another.</summary>
	public static readonly Easing Move = new("move", 0.4, 0, 0.2, 1);

	public bool IsLinear => X1 == 0 && Y1 == 0 && X2 == 1 && Y2 == 1;

	public string Css =>
		IsLinear
			? "linear"
			: $"cubic-bezier({X1.ToString(System.Globalization.CultureInfo.InvariantCulture)},{Y1.ToString(System.Globalization.CultureInfo.InvariantCulture)},{X2.ToString(System.Globalization.CultureInfo.InvariantCulture)},{Y2.ToString(System.Globalization.CultureInfo.InvariantCulture)})";

	/// <summary>Progress (0–1) at time fraction <paramref name="t"/> (0–1).</summary>
	public double Evaluate(double t)
	{
		if (t <= 0)
			return 0;
		if (t >= 1)
			return 1;
		if (IsLinear)
			return t;
		// solve x(u) = t for u by bisection (monotone in u), then return y(u)
		double lo = 0, hi = 1, u = t;
		for (var i = 0; i < 40; i++)
		{
			u = (lo + hi) / 2;
			var x = Bezier(u, X1, X2);
			if (Math.Abs(x - t) < 1e-7)
				break;
			if (x < t)
				lo = u;
			else
				hi = u;
		}
		return Bezier(u, Y1, Y2);
	}

	/// <summary>The time fraction at which progress reaches <paramref name="progress"/>.</summary>
	public double Inverse(double progress)
	{
		double lo = 0, hi = 1;
		for (var i = 0; i < 40; i++)
		{
			var mid = (lo + hi) / 2;
			if (Evaluate(mid) < progress)
				lo = mid;
			else
				hi = mid;
		}
		return (lo + hi) / 2;
	}

	private static double Bezier(double u, double p1, double p2)
	{
		var v = 1 - u;
		return (3 * v * v * u * p1) + (3 * v * u * u * p2) + (u * u * u);
	}
}

/// <summary>
/// Every duration, speed and rest an animation uses. A score never names one: the preset decides, so every animated
/// diagram on a site moves alike. All times are in milliseconds, all distances in SVG user units.
/// </summary>
internal sealed record MotionSpec
{
	public required string Name { get; init; }

	/// <summary>An element appearing (fade and rise).</summary>
	public double Enter { get; init; }

	/// <summary>An element leaving.</summary>
	public double Exit { get; init; }

	/// <summary>Focus, dimming and marks changing.</summary>
	public double Shift { get; init; }

	/// <summary>A camera move.</summary>
	public double Camera { get; init; }

	/// <summary>The distance a rising element travels while it fades in.</summary>
	public double Rise { get; init; }

	/// <summary>How fast a line draws, and the shortest and longest a draw may take.</summary>
	public double DrawSpeed { get; init; }

	public double DrawMin { get; init; }
	public double DrawMax { get; init; }

	/// <summary>How fast a token travels, and the shortest and longest a trip may take.</summary>
	public double TravelSpeed { get; init; }

	public double TravelMin { get; init; }
	public double TravelMax { get; init; }

	/// <summary>A pulse, up and back.</summary>
	public double Pulse { get; init; }

	public double PulseScale { get; init; }

	/// <summary>The gap between items of an <c>each</c> list.</summary>
	public double Stagger { get; init; }

	/// <summary>The gap between one statement and the next in a step.</summary>
	public double Beat { get; init; }

	/// <summary>How long a step holds after its last motion, at least.</summary>
	public double Hold { get; init; }

	/// <summary>Reading time per word of caption, and the least a captioned step stays up.</summary>
	public double ReadPerWord { get; init; }

	public double ReadMin { get; init; }

	/// <summary>Before the first step, and after the last before a loop fades out.</summary>
	public double Lead { get; init; }

	public double Rest { get; init; }

	/// <summary>The fade out and back in between loops.</summary>
	public double LoopFade { get; init; }

	/// <summary>The opacity of what a focus does not name.</summary>
	public double Dim { get; init; }

	/// <summary>The most a <c>focus … close</c> may zoom in.</summary>
	public double MaxZoom { get; init; }

	/// <summary>Stream dash travel per second.</summary>
	public double StreamSpeed { get; init; }

	/// <summary>How long a step with a stream lasts at least, so the flow is seen moving.</summary>
	public double StreamHold { get; init; }

	public static readonly MotionSpec Calm = new()
	{
		Name = "calm",
		Enter = 420,
		Exit = 300,
		Shift = 360,
		Camera = 720,
		Rise = 6,
		DrawSpeed = 320,
		DrawMin = 280,
		DrawMax = 900,
		TravelSpeed = 240,
		TravelMin = 480,
		TravelMax = 1500,
		Pulse = 560,
		PulseScale = 1.06,
		Stagger = 110,
		Beat = 160,
		Hold = 700,
		ReadPerWord = 240,
		ReadMin = 1300,
		Lead = 300,
		Rest = 1600,
		LoopFade = 450,
		Dim = 0.22,
		MaxZoom = 1.8,
		StreamSpeed = 46,
		StreamHold = 1800,
	};

	public static readonly MotionSpec Crisp = Calm with
	{
		Name = "crisp",
		Enter = 280,
		Exit = 200,
		Shift = 240,
		Camera = 480,
		DrawSpeed = 480,
		DrawMin = 200,
		DrawMax = 600,
		TravelSpeed = 380,
		TravelMin = 320,
		TravelMax = 1000,
		Pulse = 380,
		Stagger = 70,
		Beat = 100,
		Hold = 450,
		ReadPerWord = 200,
		ReadMin = 1000,
		Rest = 1100,
		LoopFade = 320,
		StreamSpeed = 70,
		StreamHold = 1300,
	};

	public static MotionSpec For(MotionPreset preset) => preset == MotionPreset.Crisp ? Crisp : Calm;

	public double DrawTime(double length) => Clamp(length / DrawSpeed * 1000, DrawMin, DrawMax);

	public double TravelTime(double length, Score.Speed speed)
	{
		var factor = speed switch
		{
			Score.Speed.Slow => 1.8,
			Score.Speed.Fast => 0.55,
			_ => 1,
		};
		return Clamp(length / TravelSpeed * 1000, TravelMin, TravelMax) * factor;
	}

	public double ReadTime(string? caption)
	{
		if (caption is null || caption.Trim().Length == 0)
			return 0;
		var words = caption.Split([' '], StringSplitOptions.RemoveEmptyEntries).Length;
		return Math.Max(ReadMin, words * ReadPerWord);
	}

	private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));
}
