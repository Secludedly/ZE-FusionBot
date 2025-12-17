using System.Collections.Generic;

namespace SysBot.Pokemon;

/// <summary>
/// Memory offsets for Pokémon Legends: Z-A (v2.0.0)
/// </summary>
public class PokeDataOffsetsPLZA
{
    public const int BoxFormatSlotSize = 0x148;                          // 328 bytes (box/stored format)
    public const string PLZAID = "0100F43008C44000";
    public const string PLZAGameVersion = "2.0.0";

    public const uint OverworldOffset = 0x6107858;                        // 1=In Overworld, 0=Not in Overworld
    public const uint MenuOffset = 0x61289C0;                            // 0=Overworld, 1=XMenu, 2=LinkPlay, 3=LinkTrade, 4=InBox
    public const uint ConnectedOffset = 0x612E358;                       // 1=Online, 0=Offline

    public IReadOnlyList<long> BoxStartPokemonPointer { get; } = [0x6105710, 0xB0, 0x978];           // Box 1 Slot 1 Pokemon data
    public IReadOnlyList<long> LinkTradeCodeLengthPointer { get; } = [0x6131138, 0x52];                  // Length of stored link code
    public IReadOnlyList<long> LinkTradeCodePointer { get; } = [0x6131138, 0x30, 0x0];             // Stored link code value
    public IReadOnlyList<long> LinkTradePartnerDataPointer { get; } = [0x40F73D8, 0x1D8, 0x30, 0xA0, 0x0]; // Base pointer for partner data
    public IReadOnlyList<long> LinkTradePartnerPokemonPointer { get; } = [0x6108630, 0x128, 0x30, 0x0];     // Partner's offered Pokemon data
    public IReadOnlyList<long> MyStatusPointer { get; } = [0x6105710, 0xA0, 0x40];            // Player name, TID, SID, gender
    public IReadOnlyList<long> TradePartnerBackupNIDPointer { get; } = [0x6108630, 0x108];                 // Backup NID when partner disconnects
    public IReadOnlyList<long> TradePartnerStatusPointer { get; } = [0x6108630, 0x134];                 // 0x02=Hovering, 0x03=Offering

    public const uint FallBackTradePartnerDataShift = 0x598;             // Fallback offset for partner data
    public const uint TradePartnerNIDShift = 0x30;                       // NID field offset
    public const uint TradePartnerTIDShift = 0x74;                       // TID field offset
}
