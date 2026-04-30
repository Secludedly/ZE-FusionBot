using PKHeX.Core;
using System;
using System.Buffers.Binary;

namespace SysBot.Pokemon;

public sealed class TradePartnerPLZA(TradePartnerStatusPLZA info)
{
    public const int MaxByteLengthStringObject = 26;
    public int Game { get; } = info.Game;
    public int Gender { get; } = info.Gender;
    public byte Language { get; } = (byte)info.Language;
    public string GenderString => TrainerDisplayHelper.GetGenderString(Gender);
    public string LanguageString => TrainerDisplayHelper.GetLanguageString(Language);
    public string SID7 { get; } = info.DisplaySID.ToString("D4");
    public string TID7 { get; } = info.DisplayTID.ToString("D6");
    public string TrainerName { get; } = info.OT;
}

// Bot's own MyStatus structure (save file, 48 bytes)
// Based on PKHeX MyStatus9a structure:
// 0x00: ID32 (TID+SID), 0x04: Game, 0x05: Gender, 0x07: Language, 0x10: OT Name
public sealed class TradeMyStatusPLZA
{
    public readonly byte[] Data = new byte[0x30];

    public uint DisplaySID => BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan(0)) / 1_000_000;

    public uint DisplayTID => BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan(0)) % 1_000_000;

    public int Game => Data[0x04];

    public int Gender => Data[0x05];

    public byte Language => Data[0x07];

    public string OT => StringConverter8.GetString(Data.AsSpan(0x10, 0x1A));
}

// Trade partner status structure (48 bytes)
// 0x00: ID32 (TID+SID), 0x04: Gender, 0x05: Language, 0x08: OT Name
public sealed class TradePartnerStatusPLZA
{
    public readonly byte[] Data = new byte[0x30];

    public uint DisplaySID => BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan(0)) / 1_000_000;

    public uint DisplayTID => BinaryPrimitives.ReadUInt32LittleEndian(Data.AsSpan(0)) % 1_000_000;

    public int Game => 52; // PLZA game version

    public int Gender => Data[0x04];

    public byte Language => Data[0x05];

    public string OT => StringConverter8.GetString(Data.AsSpan(0x08, 0x1A));
}

public static class TrainerDisplayHelper
{
    public static string GetGenderString(int gender) => gender switch
    {
        0 => "Male",
        1 => "Female",
        _ => $"Unknown ({gender})"
    };

    public static string GetLanguageString(int language)
    {
        byte langByte = (byte)language;

        if (Enum.IsDefined(typeof(LanguageID), langByte))
            return ((LanguageID)langByte).ToString();

        return $"Unknown ({language})";
    }
}
