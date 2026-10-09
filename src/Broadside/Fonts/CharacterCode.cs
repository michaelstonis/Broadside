namespace Broadside.Fonts;

/// <summary>One character code read from a string shown in a composite font: its value, its length in bytes, and whether it is valid.</summary>
/// <param name="Value">The code's bytes as a big-endian number, such as 0x8140 for the bytes 81 40.</param>
/// <param name="Length">How many bytes of the string the code takes, 1 to 4; 0 only for an empty string.</param>
/// <param name="IsValid">
/// Whether the code lies in one of the CMap's codespace ranges. An invalid code selects CID 0, and its length is that of the
/// codespace range that best matches its first bytes.
/// </param>
/// <remarks>ISO 32000-2 §9.7.6.2 and §9.7.6.3; Adobe TN 5014 §5.2 (codespace ranges).</remarks>
public readonly record struct CharacterCode(uint Value, int Length, bool IsValid);
