using PKHeX.Core;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static SysBot.Base.SwitchButton;
using static SysBot.Pokemon.PokeDataOffsetsPLZA;

namespace SysBot.Pokemon;

public abstract class PokeRoutineExecutor9PLZA : PokeRoutineExecutor<PA9>
{
    protected const int HidWaitTime = 46;

    protected const int KeyboardPressTime = 35;

    protected PokeRoutineExecutor9PLZA(PokeBotState Config) : base(Config)
    {
    }

    protected PokeDataOffsetsPLZA Offsets { get; } = new();

    public async Task CleanExit(CancellationToken token)
    {
        await SetScreen(ScreenState.On, token).ConfigureAwait(false);
        Log("Detaching controllers on routine exit.");
        await DetachController(token).ConfigureAwait(false);
    }

    public Task ClearTradePartnerNID(ulong offset, CancellationToken token)
    {
        var data = new byte[8];
        return SwitchConnection.WriteBytesAbsoluteAsync(data, offset, token);
    }

    public async Task CloseGame(PokeTradeHubConfig config, CancellationToken token)
    {
        var timing = config.Timings;

        await Click(B, 0_500, token).ConfigureAwait(false);
        await Click(HOME, 2_000 + timing.ExtraTimeReturnHome, token).ConfigureAwait(false);
        await Click(X, 1_000, token).ConfigureAwait(false);
        await Click(A, 5_000 + timing.ExtraTimeCloseGame, token).ConfigureAwait(false);
        Log("Closed out of the game!");
    }

    public async Task<SAV9ZA> GetFakeTrainerSAV(CancellationToken token)
    {
        var sav = new SAV9ZA();
        if (Offsets.MyStatusPointer[0] == 0)
            return sav;

        var myStatus = new TradeMyStatusPLZA();
        var read = await SwitchConnection.PointerPeek(myStatus.Data.Length, Offsets.MyStatusPointer, token).ConfigureAwait(false);
        read.CopyTo(myStatus.Data, 0);

        // Manually populate the SAV with data from our correctly-structured MyStatus
        sav.Language = myStatus.Language;
        sav.OT = myStatus.OT;
        sav.DisplayTID = myStatus.DisplayTID;
        sav.DisplaySID = myStatus.DisplaySID;
        sav.Gender = (byte)myStatus.Gender;
        sav.Version = (GameVersion)myStatus.Game;

        return sav;
    }

    /// <summary>
    /// Resolves a pointer chain to get the final memory address.
    /// PLZA uses two patterns:
    /// - Pokemon data: Dereferences all jumps
    /// - Struct fields: Dereferences all except last (which is a field offset)
    /// </summary>
    protected async Task<ulong> ResolvePointer(IReadOnlyList<long> jumps, CancellationToken token, bool derefAll = true)
    {
        var bytes = await SwitchConnection.ReadBytesMainAsync((ulong)jumps[0], 8, token).ConfigureAwait(false);
        var ptr = BitConverter.ToUInt64(bytes, 0);

        int lastIndex = derefAll ? jumps.Count : jumps.Count - 1;

        for (int i = 1; i < lastIndex; i++)
        {
            var addr = ptr + (ulong)jumps[i];
            bytes = await SwitchConnection.ReadBytesAbsoluteAsync(addr, 8, token).ConfigureAwait(false);
            ptr = BitConverter.ToUInt64(bytes, 0);
        }

        // For struct fields, add the last jump as an offset to the field
        if (!derefAll)
            ptr += (ulong)jumps[jumps.Count - 1];

        return ptr;
    }

    public async Task<TradePartnerStatusPLZA> GetTradePartnerStatus(IReadOnlyList<long> jumps, CancellationToken token)
    {
        var finalAddr = await ResolvePointer(jumps, token, derefAll: false).ConfigureAwait(false);
        var info = new TradePartnerStatusPLZA();
        var read = await SwitchConnection.ReadBytesAbsoluteAsync(finalAddr, info.Data.Length, token).ConfigureAwait(false);
        read.CopyTo(info.Data, 0);
        return info;
    }

    public async Task<ulong> GetTradePartnerNID(CancellationToken token)
    {
        var data = await SwitchConnection.PointerPeek(8, Offsets.TradePartnerBackupNIDPointer, token).ConfigureAwait(false);
        return BitConverter.ToUInt64(data, 0);
    }

    public async Task<SAV9ZA> IdentifyTrainer(CancellationToken token)
    {
        await VerifyBotbaseVersion(token).ConfigureAwait(false);

        string title = await SwitchConnection.GetTitleID(token).ConfigureAwait(false);
        if (title is not PLZAID)
            throw new Exception($"Incorrect Title ID: {title}. Expected PLZA title ID: {PLZAID}");

        var game_version = await SwitchConnection.GetGameInfo("version", token).ConfigureAwait(false);
        if (!game_version.SequenceEqual(PLZAGameVersion))
            throw new Exception($"Game version is not supported. Expected version {PLZAGameVersion}, current version is {game_version}.");

        var sav = await GetFakeTrainerSAV(token).ConfigureAwait(false);
        InitSaveData(sav);

        if (!IsValidTrainerData())
        {
            await CheckForRAMShiftingApps(token).ConfigureAwait(false);
            throw new Exception("Refer to the SysBot.NET wiki for more information.");
        }

        return sav;
    }

    public async Task InitializeHardware(IBotStateSettings settings, CancellationToken token)
    {
        Log("Detaching on startup.");
        await DetachController(token).ConfigureAwait(false);
        if (settings.ScreenOff)
        {
            Log("Turning off screen.");
            await SetScreen(ScreenState.Off, token).ConfigureAwait(false);
        }
        await SetController(ControllerType.ProController, token);
        Log("Setting PLZA-specific hid waits");
        await Connection.SendAsync(SwitchCommand.Configure(SwitchConfigureParameter.keySleepTime, KeyboardPressTime), token).ConfigureAwait(false);
        await Connection.SendAsync(SwitchCommand.Configure(SwitchConfigureParameter.pollRate, HidWaitTime), token).ConfigureAwait(false);
    }

    public async Task<bool> IsConnectedOnline(ulong offset, CancellationToken token)
    {
        var data = await SwitchConnection.ReadBytesAbsoluteAsync(offset, 1, token).ConfigureAwait(false);
        return data[0] == 1;
    }

    public async Task<bool> IsInBox(ulong offset, CancellationToken token)
    {
        var data = await SwitchConnection.ReadBytesAbsoluteAsync(offset, 1, token).ConfigureAwait(false);
        return data[0] == 0x14;
    }

    public async Task<bool> IsInPokePortal(ulong offset, CancellationToken token)
    {
        var data = await SwitchConnection.ReadBytesAbsoluteAsync(offset, 1, token).ConfigureAwait(false);
        return data[0] == 0x10;
    }

    public async Task<bool> IsOnOverworld(ulong offset, CancellationToken token)
    {
        var data = await SwitchConnection.ReadBytesAbsoluteAsync(offset, 1, token).ConfigureAwait(false);
        return data[0] == 0x00;
    }

    public async Task<bool> IsOnLinkCodeEntry(ulong offset, CancellationToken token)
    {
        var data = await SwitchConnection.ReadBytesAbsoluteAsync(offset, 1, token).ConfigureAwait(false);
        return data[0] == 0x01;
    }

    public async Task<bool> IsInTradeBox(ulong offset, CancellationToken token)
    {
        var data = await SwitchConnection.ReadBytesAbsoluteAsync(offset, 1, token).ConfigureAwait(false);
        return data[0] == 0x01;
    }

    public override Task<PA9> ReadBoxPokemon(int box, int slot, CancellationToken token)
    {
        var jumps = Offsets.BoxStartPokemonPointer.ToArray();
        return ReadPokemonPointer(jumps, BoxFormatSlotSize, token);
    }

    public async Task<bool> ReadIsChanged(uint offset, byte[] original, CancellationToken token)
    {
        var result = await Connection.ReadBytesAsync(offset, original.Length, token).ConfigureAwait(false);
        return !result.SequenceEqual(original);
    }

    public override Task<PA9> ReadPokemon(ulong offset, CancellationToken token) => ReadPokemon(offset, BoxFormatSlotSize, token);

    public override async Task<PA9> ReadPokemon(ulong offset, int size, CancellationToken token)
    {
        var data = await SwitchConnection.ReadBytesAbsoluteAsync(offset, size, token).ConfigureAwait(false);
        return new PA9(data);
    }

    public override async Task<PA9> ReadPokemonPointer(IEnumerable<long> jumps, int size, CancellationToken token)
    {
        var (valid, offset) = await ValidatePointerAll(jumps, token).ConfigureAwait(false);
        if (!valid)
            return new PA9();
        return await ReadPokemon(offset, token).ConfigureAwait(false);
    }

    public async Task ReOpenGame(PokeTradeHubConfig config, CancellationToken token)
    {
        Log("Error detected, restarting the game!");
        await CloseGame(config, token).ConfigureAwait(false);
        await StartGame(config, token).ConfigureAwait(false);
    }

    public async Task SetBoxPokemonAbsolute(ulong offset, PA9 pkm, CancellationToken token, ITrainerInfo? sav = null)
    {
        pkm.ResetPartyStats();

        if (sav != null)
        {
            if (pkm.TID16 == 0 && pkm.SID16 == 0)
            {
                pkm.TID16 = sav.TID16;
                pkm.SID16 = sav.SID16;
                pkm.OriginalTrainerName = sav.OT;
                pkm.OriginalTrainerGender = sav.Gender;
            }

            pkm.UpdateHandler(sav);

            if (IsFormArgumentTypeDatePair(pkm.Species, pkm.Form))
            {
                pkm.FormArgumentElapsed = pkm.FormArgumentMaximum = 0;
                pkm.FormArgumentRemain = (byte)GetFormArgument(pkm);
            }

            pkm.RefreshChecksum();
        }

        // PLZA uses party format (344 bytes) + 64 bytes padding per box slot
        var partyData = pkm.EncryptedPartyData;
        var boxData = new byte[partyData.Length + 0x40]; // Add 64-byte gap
        Array.Copy(partyData, boxData, partyData.Length);
        // Remaining 64 bytes stay as zeros (padding)

        await SwitchConnection.WriteBytesAbsoluteAsync(boxData, offset, token).ConfigureAwait(false);
    }

    // FormArgument helper methods
    private static bool IsFormArgumentTypeDatePair(ushort species, byte form)
    {
        return species switch
        {
            (int)Species.Furfrou when form != 0 => true,
            (int)Species.Hoopa when form == 1 => true,
            _ => false,
        };
    }

    private static uint GetFormArgument(PKM pk)
    {
        if (pk.Form == 0)
            return 0;
        return pk.Species switch
        {
            (int)Species.Furfrou => 5u, // Furfrou styled forms revert after 5 days
            // Hoopa no longer sets Form Argument for Unbound form in PLZA. Let it set 0.
            _ => 0u,
        };
    }

    public async Task StartGame(PokeTradeHubConfig config, CancellationToken token)
    {
        TimingSettings timing = config.Timings;
        int loadPro = timing.ProfileSelectionRequired ? timing.ExtraTimeLoadProfile : 0;

        await Click(A, 1_000 + loadPro, token).ConfigureAwait(false);

        if (timing.AvoidSystemUpdate)
        {
            await Task.Delay(0_500, token).ConfigureAwait(false);
            await Click(DUP, 0_600, token).ConfigureAwait(false);
            await Click(A, 1_000 + loadPro, token).ConfigureAwait(false);
        }

        if (timing.ProfileSelectionRequired)
        {
            await Click(A, 1_000, token).ConfigureAwait(false);
            await Click(A, 1_000, token).ConfigureAwait(false);
        }

        if (timing.CheckGameDelay)
        {
            await Task.Delay(2_000 + timing.ExtraTimeCheckGame, token).ConfigureAwait(false);
        }

        Log("Restarting the game!");

        await Task.Delay(15_000 + timing.ExtraTimeLoadGame, token).ConfigureAwait(false);

        for (int i = 0; i < 8; i++)
        {
            await Click(A, 1_000, token).ConfigureAwait(false);
        }

        int timer = 60_000;
        while (!await IsOnOverworldTitle(token).ConfigureAwait(false))
        {
            await Task.Delay(1_000, token).ConfigureAwait(false);
            timer -= 1_000;
            if (timer <= 0 && !timing.AvoidSystemUpdate)
            {
                Log("Still not in the game, initiating rescue protocol!");
                while (!await IsOnOverworldTitle(token).ConfigureAwait(false))
                {
                    await Click(A, 6_000, token).ConfigureAwait(false);
                }

                break;
            }
        }

        await Task.Delay(5_000 + timing.ExtraTimeLoadOverworld, token).ConfigureAwait(false);
        Log("Back in the overworld!");
    }

    protected virtual async Task EnterLinkCode(int code, PokeTradeHubConfig config, CancellationToken token)
    {
        // If code is 0, skip entering a code (blank code for random matchmaking)
        if (code == 0)
            return;

        if (config.UseKeyboard)
        {
            char[] codeChars = $"{code:00000000}".ToCharArray();
            HidKeyboardKey[] keysToPress = new HidKeyboardKey[codeChars.Length];
            for (int i = 0; i < codeChars.Length; ++i)
                keysToPress[i] = (HidKeyboardKey)Enum.Parse(typeof(HidKeyboardKey), (int)codeChars[i] >= (int)'A' && (int)codeChars[i] <= (int)'Z' ? $"{codeChars[i]}" : $"D{codeChars[i]}");

            await Connection.SendAsync(SwitchCommand.TypeMultipleKeys(keysToPress), token).ConfigureAwait(false);
            await Task.Delay((HidWaitTime * 8) + 0_200, token).ConfigureAwait(false);
        }
        else
        {
            foreach (var key in TradeUtil.GetPresses(code))
            {
                int delay = config.Timings.KeypressTime;
                await Click(key, delay, token).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> IsOnOverworldTitle(CancellationToken token)
    {
        var (valid, offset) = await ValidatePointerAll(Offsets.OverworldPointer, token).ConfigureAwait(false);
        if (!valid)
            return false;
        return await IsOnOverworld(offset, token).ConfigureAwait(false);
    }
}
