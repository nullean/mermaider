using System;

namespace Mermaider;

/// <summary>
/// Thrown when Mermaid diagram text cannot be parsed.
/// This includes syntax errors and regex timeout (ReDoS protection).
/// </summary>
public class MermaidParseException : Exception
{
	public MermaidParseException(string message) : base(message)
	{ }

	public MermaidParseException(string message, Exception innerException) : base(message, innerException)
	{ }

	/// <summary>A syntax error in one statement: names the problem, what would have been valid, and the offending text.</summary>
	/// <param name="problem">What is wrong, e.g. <c>arrow '--&gt;' has no target node</c>.</param>
	/// <param name="sourceLine">The offending statement, trimmed.</param>
	/// <param name="expected">What the grammar accepts there, e.g. <c>a node id</c>.</param>
	/// <param name="line">1-based line in the original text, when known.</param>
	/// <param name="column">1-based column within <paramref name="sourceLine"/>, when known.</param>
	public MermaidParseException(string problem, string sourceLine, string? expected = null, int? line = null, int? column = null)
		: base(Format(problem, sourceLine, expected, line, column))
	{
		Problem = problem;
		SourceLine = sourceLine;
		Expected = expected;
		Line = line;
		Column = column;
	}

	/// <summary>What is wrong, without the location or source excerpt.</summary>
	public string? Problem { get; }

	/// <summary>The offending statement, trimmed.</summary>
	public string? SourceLine { get; }

	/// <summary>What would have been valid at that point.</summary>
	public string? Expected { get; }

	/// <summary>1-based line in the original text, when it could be located.</summary>
	public int? Line { get; }

	/// <summary>1-based column within <see cref="SourceLine"/>, when known.</summary>
	public int? Column { get; }

	internal MermaidParseException WithLine(int line) => new(Problem!, SourceLine!, Expected, line, Column);

	private static string Format(string problem, string sourceLine, string? expected, int? line, int? column)
	{
		var sb = new System.Text.StringBuilder();
		sb.Append("Parse error");
		if (line is not null)
			sb.Append(" on line ").Append(line.Value);
		sb.Append(": ").Append(problem);
		sb.Append("\n  ").Append(sourceLine);
		if (column is > 0)
			sb.Append("\n  ").Append(' ', column.Value - 1).Append('^');
		if (expected is not null)
			sb.Append("\nExpecting ").Append(expected).Append('.');
		return sb.ToString();
	}
}
