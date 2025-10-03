using PKHeX.Core;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static SysBot.Base.SwitchButton;
using static SysBot.Pokemon.PokeDataOffsetsSV;

namespace SysBot.Pokemon;

public abstract class PokeRoutineExecutor9SV : PokeRoutineExecutor<PK9>
{
    protected const int HidWaitTime = 46;
    protected const int KeyboardPressTime = 35;
    protected PokeDataOffsetsSV Offsets { get; } = new();
    protected PokeRoutineExecutor9SV(PokeBotState Config) : base(Config)
    {
    }

    public override Task<PK9> ReadPokemon(ulong offset, CancellationToken token) => ReadPokemon(offset, BoxFormatSlotSize, token);

    public override async Task<PK9> ReadPokemon(ulong offset, int size, CancellationToken token)
    {
        var data = await SwitchConnection.ReadBytesAbsoluteAsync(offset, size, token).ConfigureAwait(false);
        return new PK9(data);
    }

    public override async Task<PK9> ReadPokemonPointer(IEnumerable<long> jumps, int size, CancellationToken token)
    {
        var (valid, offset) = await ValidatePointerAll(jumps, token).ConfigureAwait(false);
        if (!valid)
            return new PK9();
        return await ReadPokemon(offset, token).ConfigureAwait(false);
    }

    public async Task<bool> ReadIsChanged(uint offset, byte[] original, CancellationToken token)
    {
        var result = await Connection.ReadBytesAsync(offset, original.Length, token).ConfigureAwait(false);
        return !result.SequenceEqual(original);
    }

    public override Task<PK9> ReadBoxPokemon(int box, int slot, CancellationToken token)
    {
        // Shouldn't be reading anything but box1slot1 here. Slots are not consecutive.
        var jumps = Offsets.BoxStartPokemonPointer.ToArray();
        return ReadPokemonPointer(jumps, BoxFormatSlotSize, token);
    }

    public Task SetBoxPokemonAbsolute(ulong offset, PK9 pkm, CancellationToken token, ITrainerInfo? sav = null)
    {
        if (sav != null)
        {
            // Update PKM to the current save's handler data
            pkm.UpdateHandler(sav);
            pkm.RefreshChecksum();
        }

        pkm.ResetPartyStats();
        return SwitchConnection.WriteBytesAbsoluteAsync(pkm.EncryptedBoxData, offset, token);
    }

    public Task SetCurrentBox(byte box, CancellationToken token)
    {
        return SwitchConnection.PointerPoke([box], Offsets.CurrentBoxPointer, token);
    }

    public async Task<byte> GetCurrentBox(CancellationToken token)
    {
        var data = await SwitchConnection.PointerPeek(1, Offsets.CurrentBoxPointer, token).ConfigureAwait(false);
        return data[0];
    }

    public async Task<SAV9SV> IdentifyTrainer(CancellationToken token)
    {
        // Check if botbase is on the correct version or later.
        await VerifyBotbaseVersion(token).ConfigureAwait(false);

        // Check title so we can warn if mode is incorrect.
        string title = await SwitchConnection.GetTitleID(token).ConfigureAwait(false);
        if (title is not (ScarletID or VioletID))
            throw new Exception($"{title} is not a valid SV title. Is your mode correct?");

        // Verify the game version.
        var game_version = await SwitchConnection.GetGameInfo("version", token).ConfigureAwait(false);
        if (!game_version.SequenceEqual(SVGameVersion))
            throw new Exception($"Game version is not supported. Expected version {SVGameVersion}, and current game version is {game_version}.");

        var sav = await GetFakeTrainerSAV(token).ConfigureAwait(false);
        InitSaveData(sav);

        if (!IsValidTrainerData())
        {
            await CheckForRAMShiftingApps(token).ConfigureAwait(false);
            throw new Exception("Refer to the SysBot.NET wiki (https://github.com/kwsch/SysBot.NET/wiki/Troubleshooting) for more information.");
        }

        if (await GetTextSpeed(token).ConfigureAwait(false) < TextSpeedOption.Fast)
            throw new Exception("Text speed should be set to FAST. Fix this for correct operation.");

        return sav;
    }

    public async Task<SAV9SV> GetFakeTrainerSAV(CancellationToken token)
    {
        var sav = new SAV9SV();
        var info = sav.MyStatus;
        var read = await SwitchConnection.PointerPeek(info.Data.Length, Offsets.MyStatusPointer, token).ConfigureAwait(false);

        byte[] dataBytes = new byte[info.Data.Length];
        Array.Copy(read, dataBytes, info.Data.Length);
        dataBytes.CopyTo(info.Data);

        return sav;
    }


    public async Task<TradeMyStatus> GetTradePartnerMyStatus(IReadOnlyList<long> pointer, CancellationToken token)
    {
        var info = new TradeMyStatus();
        var read = await SwitchConnection.PointerPeek(info.Data.Length, pointer, token).ConfigureAwait(false);
        read.CopyTo(info.Data, 0);
        return info;
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
        Log($"Setting SV-specific hid waits");
        await Connection.SendAsync(SwitchCommand.Configure(SwitchConfigureParameter.keySleepTime, KeyboardPressTime), token).ConfigureAwait(false);
        await Connection.SendAsync(SwitchCommand.Configure(SwitchConfigureParameter.pollRate, HidWaitTime), token).ConfigureAwait(false);
    }

    public async Task CleanExit(CancellationToken token)
    {
        await SetScreen(ScreenState.On, token).ConfigureAwait(false);
        Log("Detaching controllers on routine exit.");
        await DetachController(token).ConfigureAwait(false);
    }

    protected virtual async Task EnterLinkCode(int code, PokeTradeHubConfig config, CancellationToken token)
    {
        if (config.UseKeyboard)
        {
            // Enter link code using keyboard
            char[] codeChars = $"{code:00000000}".ToCharArray();
            HidKeyboardKey[] keysToPress = new HidKeyboardKey[codeChars.Length];
            for (int i = 0; i < codeChars.Length; ++i)
                keysToPress[i] = (HidKeyboardKey)Enum.Parse(typeof(HidKeyboardKey), (int)codeChars[i] >= (int)'A' && (int)codeChars[i] <= (int)'Z' ? $"{codeChars[i]}" : $"D{codeChars[i]}");

            await Connection.SendAsync(SwitchCommand.TypeMultipleKeys(keysToPress), token).ConfigureAwait(false);
            await Task.Delay((HidWaitTime * 8) + 0_200, token).ConfigureAwait(false);
            // Confirm Code outside of this method (allow synchronization)
        }
        else
        {
            // Enter link code using directional arrows
            var keys = TradeUtil.GetPresses(code);
            foreach (var key in keys)
            {
                int delay = config.Timings.KeypressTime;
                await Click(key, delay, token).ConfigureAwait(false);
            }
        }
    }

    public async Task ReOpenGame(PokeTradeHubConfig config, CancellationToken token)
    {
        Log("Error detected, restarting the game!!");
        await CloseGame(config, token).ConfigureAwait(false);
        await StartGame(config, token).ConfigureAwait(false);
    }

    public async Task CloseGame(PokeTradeHubConfig config, CancellationToken token)
    {
        var timing = config.Timings;
        // Close out of the game
        await Click(B, 0_500, token).ConfigureAwait(false);
        await Click(HOME, 2_000 + timing.ExtraTimeReturnHome, token).ConfigureAwait(false);
        await Click(X, 1_000, token).ConfigureAwait(false);
        await Click(A, 5_000 + timing.ExtraTimeCloseGame, token).ConfigureAwait(false);
        Log("Closed out of the game!");
    }

    public async Task StartGame(PokeTradeHubConfig config, CancellationToken token)
    {
        // Open game.
        var timing = config.Timings;
        var loadPro = timing.ProfileSelectionRequired ? timing.ExtraTimeLoadProfile : 0;

        await Click(A, 1_000 + loadPro, token).ConfigureAwait(false); // Initial "A" Press to start the Game + a delay if needed for profiles to load

        // Menus here can go in the order: Update Prompt -> Profile -> DLC check -> Unable to use DLC.
        //  The user can optionally turn on the setting if they know of a breaking system update incoming.
        if (timing.AvoidSystemUpdate)
        {
            await Click(DUP, 0_600, token).ConfigureAwait(false);
            await Click(A, 1_000 + timing.ExtraTimeLoadProfile, token).ConfigureAwait(false);
        }

        // Only send extra Presses if we need to
        if (timing.ProfileSelectionRequired)
        {
            await Click(A, 1_000, token).ConfigureAwait(false); // Now we are on the Profile Screen
            await Click(A, 1_000, token).ConfigureAwait(false); // Select the profile
        }

        // Digital game copies take longer to load
        if (timing.CheckGameDelay)
        {
            await Task.Delay(2_000 + timing.ExtraTimeCheckGame, token).ConfigureAwait(false);
        }

        await Click(DUP, 0_600, token).ConfigureAwait(false);
        await Click(A, 0_600, token).ConfigureAwait(false);

        Log("Restarting the game!");

        // Switch Logo and game load screen
        await Task.Delay(12_000 + timing.ExtraTimeLoadGame, token).ConfigureAwait(false);

        for (int i = 0; i < 8; i++)
            await Click(A, 1_000, token).ConfigureAwait(false);

        var timer = 60_000;
        while (!await IsOnOverworldTitle(token).ConfigureAwait(false))
        {
            await Task.Delay(1_000, token).ConfigureAwait(false);
            timer -= 1_000;
            // We haven't made it back to overworld after a minute, so press A every 6 seconds hoping to restart the game.
            // Don't risk it if hub is set to avoid updates.
            if (timer <= 0 && !timing.AvoidSystemUpdate)
            {
                Log("Still not in the game, initiating rescue protocol!");
                while (!await IsOnOverworldTitle(token).ConfigureAwait(false))
                    await Click(A, 6_000, token).ConfigureAwait(false);
                break;
            }
        }

        await Task.Delay(2_000 + timing.ExtraTimeLoadOverworld, token).ConfigureAwait(false);
        Log("Back in the overworld!");
    }

    public async Task<bool> IsConnectedOnline(ulong offset, CancellationToken token)
    {
        var data = await SwitchConnection.ReadBytesAbsoluteAsync(offset, 1, token).ConfigureAwait(false);
        return data[0] == 1;
    }

    public async Task<ulong> GetTradePartnerNID(ulong offset, CancellationToken token)
    {
        var data = await SwitchConnection.ReadBytesAbsoluteAsync(offset, 8, token).ConfigureAwait(false);
        return BitConverter.ToUInt64(data, 0);
    }

    public Task ClearTradePartnerNID(ulong offset, CancellationToken token)
    {
        var data = new byte[8];
        return SwitchConnection.WriteBytesAbsoluteAsync(data, offset, token);
    }

    public async Task<bool> IsOnOverworld(ulong offset, CancellationToken token)
    {
        var data = await SwitchConnection.ReadBytesAbsoluteAsync(offset, 1, token).ConfigureAwait(false);
        return data[0] == 0x11;
    }

    // Only used to check if we made it off the title screen; the pointer isn't viable until a few seconds after clicking A.
    private async Task<bool> IsOnOverworldTitle(CancellationToken token)
    {
        var (valid, offset) = await ValidatePointerAll(Offsets.OverworldPointer, token).ConfigureAwait(false);
        if (!valid)
            return false;
        return await IsOnOverworld(offset, token).ConfigureAwait(false);
    }

    // 0x10 if fully loaded into Poké Portal.
    public async Task<bool> IsInPokePortal(ulong offset, CancellationToken token)
    {
        var data = await SwitchConnection.ReadBytesAbsoluteAsync(offset, 1, token).ConfigureAwait(false);
        return data[0] == 0x10;
    }

    // 0x14 in a box and during trades, trade evolutions, and move learning.
    public async Task<bool> IsInBox(ulong offset, CancellationToken token)
    {
        var data = await SwitchConnection.ReadBytesAbsoluteAsync(offset, 1, token).ConfigureAwait(false);
        return data[0] == 0x14;
    }

    public async Task<TextSpeedOption> GetTextSpeed(CancellationToken token)
    {
        var data = await SwitchConnection.PointerPeek(1, Offsets.ConfigPointer, token).ConfigureAwait(false);
        return (TextSpeedOption)(data[0] & 3);
    }
}
