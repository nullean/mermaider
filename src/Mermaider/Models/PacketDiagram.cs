namespace Mermaider.Models;

/// <summary>Packet / packet-beta bit-field diagram.</summary>
public sealed record PacketDiagram
{
	public string? Title { get; init; }
	public required IReadOnlyList<PacketField> Fields { get; init; }
}

/// <summary>Inclusive bit range with a label (start == end for a single bit).</summary>
public sealed record PacketField(int Start, int End, string Label);
