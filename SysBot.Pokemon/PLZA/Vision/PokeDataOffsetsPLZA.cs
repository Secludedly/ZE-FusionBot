using System.Collections.Generic;

namespace SysBot.Pokemon;

/// <summary>
/// Memory offsets for Pokémon Legends: Z-A (v1.0.2)
/// </summary>
public class PokeDataOffsetsPLZA
{
    public const int BoxFormatSlotSize = 0x148;                          // 328 bytes (box/stored format)
    public const uint MenuOffset = 0x5F2E880;                            // 0=Overworld, 1=XMenu, 2=LinkPlay, 3=LinkTrade, 4=InBox
    public const string PLZAID = "0100F43008C44000";
    public const string PLZAGameVersion = "1.0.2";

    public IReadOnlyList<long> BoxStartPokemonPointer { get; } = [0x5F0C250, 0xB0, 0x978];            // Box 1 Slot 1 Pokemon data
    public IReadOnlyList<long> GameStatePointer { get; } = [0x5F59608, 0x58];                   // 0x01=Normal, 0x02=Trade animation
    public IReadOnlyList<long> IsConnectedPointer { get; } = [0x3CE1510, 0x08];                   // 0x01=Online, 0x00=Offline
    public IReadOnlyList<long> LinkCodeTradePointer { get; } = [0x3CC8C20, 0x24];                   // Current link code (4-byte int)
    public IReadOnlyList<long> LinkTradePartnerDataPointer { get; } = [0x3EFE058, 0x1D8, 0x30, 0xA0, 0x0]; // Base pointer for partner data
    public IReadOnlyList<long> LinkTradePartnerPokemonPointer { get; } = [0x5F0F2B0, 0x128, 0x30, 0x0];       // Partner's offered Pokemon data
    public IReadOnlyList<long> MyStatusPointer { get; } = [0x5F0C250, 0xA0, 0x40];             // Player name, TID, SID, gender
    public IReadOnlyList<long> TradePartnerBackupNIDPointer { get; } = [0x5F0F2B0, 0x108];                  // Backup NID when partner disconnects
    public IReadOnlyList<long> TradePartnerStatusPointer { get; } = [0x5F0F2B0, 0x134];                  // 0x02=Hovering, 0x03=Offering

    public const uint FallBackTradePartnerDataShift = 0x598;             // Fallback offset for partner data
    public const uint TradePartnerNIDShift = 0x30;              // NID field offset
    public const uint TradePartnerTIDShift = 0x74;              // TID field offset
}
