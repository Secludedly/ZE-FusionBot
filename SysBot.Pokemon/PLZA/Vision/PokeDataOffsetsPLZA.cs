using System.Collections.Generic;

namespace SysBot.Pokemon;

/// <summary>
/// Memory offsets for Pokémon Legends: Z-A (v1.0.1)
///
/// Two pointer patterns:
/// - Pokemon data: All jumps are dereferenced
/// - Struct fields: Last jump is a field offset, not dereferenced
/// Pointer Research by hexbyt3 - https://github.com/hexbyt3/PLZAResearch
/// </summary>
public class PokeDataOffsetsPLZA
{
    public const int BoxFormatSlotSize = 0x198;

    public const string PLZAID = "0100F43008C44000";

    public const string PLZAGameVersion = "1.0.2";

    // ✅ VERIFIED - Box 1 Slot 1 Pokemon data
    // Successfully tested with Pokemon injection/reading
    public IReadOnlyList<long> BoxStartPokemonPointer { get; } = [0x5F0C250, 0xB0, 0x978];

    // ✅ VERIFIED - Online connection status check
    // Returns 1 when connected to internet, 0 when offline
    public IReadOnlyList<long> IsConnectedPointer { get; } = [0x3CE1510, 0x08];

    // ✅ VERIFIED - Current link code value in search box
    // Returns the 4-byte integer link code currently entered (0x0 if empty)
    public IReadOnlyList<long> LinkCodeTradePointer { get; } = [0x3CC8C20, 0x24];

    // ✅ UNVERIFIED - Trade partner NID (Nintendo ID)
    // Returns 8-byte ulong Nintendo ID of connected trade partner
    // Works correctly on first trade and all subsequent trades
    public IReadOnlyList<long> LinkTradePartnerNIDPointer { get; } = [0x3EFE058, 0x120, 0x38, 0x10, 0x38];

    // ✅ VERIFIED - Player trainer status/info
    // Contains player name, IDs, and trainer data
    public IReadOnlyList<long> MyStatusPointer { get; } = [0x5F0C250, 0xA0, 0x40];

    // ✅ VERIFIED - Overworld state check
    // Returns 0x00 when player is in overworld, 0x01 in menus/trades
    public IReadOnlyList<long> OverworldPointer { get; } = [0x3CC5990, 0x11];

    // ✅ VERIFIED - Game state/trade animation indicator
    // Returns: 0x01 = Normal (before trade, after animation completes)
    //          0x02 = Trade animation in progress
    // Used to actively detect when trade animations complete
    public IReadOnlyList<long> GameStatePointer { get; } = [0x5F59608, 0x58];

    // ✅ VERIFIED - Trade partner information
    // Contains trainer info for trade partner in multi-trade
    // Seems inconsistent at times, may need a more resilient pointer
    public IReadOnlyList<long> Trader1MyStatusPointer { get; } = [0x3EFE058, 0x1D8, 0x180, 0x80, 0x74];

    // ✅ VERIFIED - Trade box screen status indicator
    // Returns: 0x01 = In trade box screen with partner
    //          0x00 = Not in trade box (link code screen, searching, etc.)
    // Used to detect when we've entered the trade box and partner data is loaded
    public IReadOnlyList<long> TradeBoxStatusPointer { get; } = [0x41FE140, 0x108];

    // Not Needed - Trade partner offered Pokemon data
    // Points to the Pokemon data that the trade partner is offering
    // Used for Clone and Dump commands to read the offered Pokemon
    public IReadOnlyList<long> LinkTradePartnerPokemonPointer { get; } = [0x5F0E2B0, 0x128, 0x30];
}
