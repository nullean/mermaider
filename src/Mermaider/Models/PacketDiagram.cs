namespace Mermaider.Models;

/// <summary>Packet / packet-beta bit-field diagram.</summary>
public sealed record PacketDiagram
{
	/// <summary>Inclusive maximum bit index accepted for fields (bits 0..MaxBitIndex).</summary>
	public const int MaxBitIndex = 4095;

	public string? Title { get; init; }
	public required IReadOnlyList<PacketField> Fields { get; init; }
}

/// <summary>Inclusive bit range with a label (start == end for a single bit).</summary>
public sealed record PacketField(int Start, int End, string Label);
