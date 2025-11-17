using PKHeX.Core;
using PKHeX.Core.Searching;
using SysBot.Base;
using SysBot.Base.Util;
using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using static SysBot.Base.SwitchButton;
using static SysBot.Pokemon.PokeDataOffsetsPLZA;
using static SysBot.Pokemon.TradeHub.SpecialRequests;

namespace SysBot.Pokemon;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public class PokeTradeBotPLZA(PokeTradeHub<PA9> Hub, PokeBotState Config) : PokeRoutineExecutor9PLZA(Config), ICountBot, ITradeBot
{
    public readonly TradeAbuseSettings AbuseSettings = Hub.Config.TradeAbuse;

    /// <summary>
    /// Folder to dump received trade data to.
    /// </summary>
    /// <remarks>If null, will skip dumping.</remarks>
    private readonly FolderSettings DumpSetting = Hub.Config.Folder;

    private readonly TradeSettings TradeSettings = Hub.Config.Trade;

    private uint DisplaySID;
    private uint DisplayTID;

    private string OT = string.Empty;
    private bool StartFromOverworld = true;
    private ulong? _cachedBoxOffset;
    private ulong TradePartnerStatusOffset;
    private bool _wasConnectedToPartner = false;

    public event EventHandler<Exception>? ConnectionError;

    public event EventHandler? ConnectionSuccess;

    public ICountSettings Counts => TradeSettings;

    /// <summary>
    /// Tracks failed synchronized starts to attempt to re-sync.
    /// </summary>
    public int FailedBarrier { get; private set; }

    /// <summary>
    /// Synchronized start for multiple bots.
    /// </summary>
    public bool ShouldWaitAtBarrier { get; private set; }

    #region Lifecycle & Main Loop

    public override Task HardStop()
    {
        UpdateBarrier(false);
        return CleanExit(CancellationToken.None);
    }

    public override async Task MainLoop(CancellationToken token)
    {
        try
        {
            // Ensure cache is clean on startup
            _cachedBoxOffset = null;
            _wasConnectedToPartner = false;

            Hub.Queues.Info.CleanStuckTrades();
            await InitializeHardware(Hub.Config.Trade, token).ConfigureAwait(false);

            Log("Connecting to console...");
            var sav = await IdentifyTrainer(token).ConfigureAwait(false);
            OT = sav.OT;
            DisplaySID = sav.DisplaySID;
            DisplayTID = sav.DisplayTID;
            RecentTrainerCache.SetRecentTrainer(sav);
            OnConnectionSuccess();

            StartFromOverworld = true;

            Log("Initializing bot...");
            if (!await CheckIfOnOverworld(token).ConfigureAwait(false))
            {
                if (!await RecoverToOverworld(token).ConfigureAwait(false))
                {
                    Log("Restarting game...");
                    await RestartGamePLZA(token).ConfigureAwait(false);
                    await Task.Delay(5_000, token).ConfigureAwait(false);

                    if (!await CheckIfOnOverworld(token).ConfigureAwait(false))
                    {
                        Log("Failed to start. Please restart the bot.");
                        throw new Exception("Unable to reach overworld. Bot cannot start trading.");
                    }
                }
            }

            Log("Bot ready. Waiting for trades...");
            await InnerLoop(sav, token).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            OnConnectionError(e);
            throw;
        }

        Log($"Ending {nameof(PokeTradeBotPLZA)} loop.");
        await HardStop().ConfigureAwait(false);
    }

    public override async Task RebootAndStop(CancellationToken t)
    {
        Hub.Queues.Info.CleanStuckTrades();
        await Task.Delay(2_000, t).ConfigureAwait(false);
        await ReOpenGame(Hub.Config, t).ConfigureAwait(false);
        _cachedBoxOffset = null; // Invalidate box offset cache after reboot
        await HardStop().ConfigureAwait(false);
        await Task.Delay(2_000, t).ConfigureAwait(false);
        if (!t.IsCancellationRequested)
        {
            Log("Restarting the main loop.");
            await MainLoop(t).ConfigureAwait(false);
        }
    }

    #endregion

    #region Enums

    protected enum TradePartnerWaitResult
    {
        Success,
        Timeout,
        KickedToMenu
    }

    protected enum LinkCodeEntryResult
    {
        Success,
        VerificationFailedMismatch
    }

    #endregion

    #region Trade Queue Management

    protected virtual (PokeTradeDetail<PA9>? detail, uint priority) GetTradeData(PokeRoutineType type)
    {
        string botName = Connection.Name;

        // First check the specific type's queue
        if (Hub.Queues.TryDequeue(type, out var detail, out var priority, botName))
        {
            return (detail, priority);
        }

        // If we're doing FlexTrade, also check the Batch queue
        if (type == PokeRoutineType.FlexTrade)
        {
            if (Hub.Queues.TryDequeue(PokeRoutineType.Batch, out detail, out priority, botName))
            {
                return (detail, priority);
            }
        }

        if (Hub.Queues.TryDequeueLedy(out detail))
        {
            return (detail, PokeTradePriorities.TierFree);
        }
        return (null, PokeTradePriorities.TierFree);
    }

    #endregion

    #region Trade Partner Detection

    // Upon connecting, their Nintendo ID will instantly update.
    protected virtual async Task<TradePartnerWaitResult> WaitForTradePartner(CancellationToken token)
    {
        Log("Waiting for trainer...");

        // Initial delay to let the game populate NID pointer in memory
        await Task.Delay(3_000, token).ConfigureAwait(false);

        int maxWaitMs = Hub.Config.Trade.TradeConfiguration.TradeWaitTime * 1_000;
        int elapsed = 3_000; // Already waited 3 seconds above

        while (elapsed < maxWaitMs)
        {
            // Safety check: verify we're still in a valid state (not kicked to menu/overworld)
            var gameState = await GetGameState(token).ConfigureAwait(false);
            if (gameState != 0x01 && gameState != 0x02)
            {
                Log("Connection interrupted. Restarting...");
                return TradePartnerWaitResult.KickedToMenu;
            }

            // Additional safety check every 10 seconds: verify link code is still valid
            if (elapsed % 10_000 < 500)
            {
                var currentCode = await GetCurrentLinkCode(token).ConfigureAwait(false);
                if (currentCode == 0)
                {
                    Log("Connection error. Restarting...");
                    return TradePartnerWaitResult.KickedToMenu;
                }
            }

            // Check if we've entered the trade box - this confirms a partner is connected
            if (await CheckIfInTradeBox(token).ConfigureAwait(false))
            {
                Log("Trade partner detected!");
                _wasConnectedToPartner = false; // Reset flag when successfully back to overworld
                return TradePartnerWaitResult.Success;
            }

            await Task.Delay(500, token).ConfigureAwait(false);
            elapsed += 500;
        }

        Log("Timed out waiting for trade partner.");
        return TradePartnerWaitResult.Timeout;
    }

    #endregion

    #region AutoOT Features

    private static void ApplyTrainerInfo(PA9 pokemon, TradePartnerStatusPLZA partner)
    {
        pokemon.OriginalTrainerGender = (byte)partner.Gender;
        pokemon.TrainerTID7 = (uint)Math.Abs(partner.DisplayTID);
        pokemon.TrainerSID7 = (uint)Math.Abs(partner.DisplaySID);
        pokemon.OriginalTrainerName = partner.OT;
    }

    private async Task<PA9> ApplyAutoOT(PA9 toSend, TradePartnerStatusPLZA tradePartner, SAV9ZA sav, CancellationToken token)
    {
        // Sanity check: if trade partner OT is empty, skip AutoOT
        if (string.IsNullOrWhiteSpace(tradePartner.OT))
        {
            return toSend;
        }

        if (toSend.Version == GameVersion.GO)
        {
            var goClone = toSend.Clone();
            goClone.OriginalTrainerName = tradePartner.OT;

            ClearOTTrash(goClone, tradePartner);

            if (!toSend.ChecksumValid)
                goClone.RefreshChecksum();

            var boxOffset = await GetBoxStartOffset(token).ConfigureAwait(false);
            await SetBoxPokemonAbsolute(boxOffset, goClone, token, sav).ConfigureAwait(false);
            return goClone;
        }

        if (toSend is IHomeTrack pk && pk.HasTracker)
        {
            return toSend;
        }

        if (toSend.Generation != toSend.Format)
        {
            return toSend;
        }

        bool isMysteryGift = toSend.FatefulEncounter;
        var cln = toSend.Clone();

        // Apply trainer info (OT, TID, SID, Gender)
        ApplyTrainerInfo(cln, tradePartner);

        if (!isMysteryGift)
        {
            // Validate language ID - if invalid, default to English (2)
            int language = tradePartner.Language;
            if (language < 1 || language > 12) // Valid language IDs are 1-12
                language = 2; // English
            cln.Language = language;
        }

        ClearOTTrash(cln, tradePartner);

        // Hard-code version to ZA since PLZA only has one game version
        cln.Version = GameVersion.ZA;

        // Set nickname to species name in the Pokemon's language using PKHeX's method
        // This properly handles generation-specific formatting and language-specific names
        if (!toSend.IsNicknamed)
            cln.ClearNickname();

        // Clear handler info - make it look like trade partner is OT and never traded it
        cln.CurrentHandler = 0; // 0 = OT is current handler

        if (toSend.IsShiny)
            cln.PID = (uint)((cln.TID16 ^ cln.SID16 ^ (cln.PID & 0xFFFF) ^ toSend.ShinyXor) << 16) | (cln.PID & 0xFFFF);

        cln.RefreshChecksum();

        var tradeSV = new LegalityAnalysis(cln);

        if (tradeSV.Valid)
        {
            // Don't pass sav - we've already set handler info and don't want UpdateHandler to overwrite it
            var boxOffset = await GetBoxStartOffset(token).ConfigureAwait(false);
            await SetBoxPokemonAbsolute(boxOffset, cln, token, null).ConfigureAwait(false);
            return cln;
        }
        else
        {
            if (toSend.Species != 0)
            {
                var boxOffset = await GetBoxStartOffset(token).ConfigureAwait(false);
                await SetBoxPokemonAbsolute(boxOffset, toSend, token, sav).ConfigureAwait(false);
            }
            return toSend;
        }
    }

    private static void ClearOTTrash(PA9 pokemon, TradePartnerStatusPLZA tradePartner)
    {
        Span<byte> trash = pokemon.OriginalTrainerTrash;
        trash.Clear();
        string name = tradePartner.OT;
        int maxLength = trash.Length / 2;
        int actualLength = Math.Min(name.Length, maxLength);
        for (int i = 0; i < actualLength; i++)
        {
            char value = name[i];
            trash[i * 2] = (byte)value;
            trash[(i * 2) + 1] = (byte)(value >> 8);
        }
        if (actualLength < maxLength)
        {
            trash[actualLength * 2] = 0x00;
            trash[(actualLength * 2) + 1] = 0x00;
        }
    }

    #endregion

    #region Trade Confirmation

    private async Task<PokeTradeResult> ConfirmAndStartTrading(PokeTradeDetail<PA9> detail, uint checksumBeforeTrade, CancellationToken token)
    {
        await Click(A, 3_000, token).ConfigureAwait(false);

        var boxOffset = await GetBoxStartOffset(token).ConfigureAwait(false);
        bool b1s1Changed = false;
        bool warningSent = false;
        int maxTime = Hub.Config.Trade.TradeConfiguration.MaxTradeConfirmTime;

        for (int i = 0; i < maxTime; i++)
        {
            await Click(A, 1_000, token).ConfigureAwait(false);

            // Send warning 10 seconds before timeout
            if (!warningSent && i == maxTime - 10 && maxTime >= 10)
            {
                detail.SendNotification(this, "Hey! Pick a Pokemon to trade or I am leaving!");
                warningSent = true;
            }

            // Check if we're still in trade box (partner disconnected if TradeBoxStatusPointer = 0x00)
            if (!await CheckIfInTradeBox(token).ConfigureAwait(false))
            {
                Log("No longer in trade box - partner declined and exited during offering stage.");
                detail.SendNotification(this, "Trade partner declined or disconnected.");
                return PokeTradeResult.NoTrainerFound;
            }

            if (!b1s1Changed)
            {
                var currentPokemon = await ReadPokemon(boxOffset, BoxFormatSlotSize, token).ConfigureAwait(false);
                var currentChecksum = currentPokemon.Checksum;

                if (currentChecksum != checksumBeforeTrade)
                    b1s1Changed = true;
            }

            if (b1s1Changed)
            {
                var currentGameState = await GetGameState(token).ConfigureAwait(false);
                if (currentGameState == 0x02)
                {
                    Log("Trade started! Waiting for completion...");
                    return PokeTradeResult.Success;
                }
            }
        }

        if (!b1s1Changed)
        {
            var finalPokemon = await ReadPokemon(boxOffset, BoxFormatSlotSize, token).ConfigureAwait(false);
            var finalChecksum = finalPokemon.Checksum;

            if (finalChecksum != checksumBeforeTrade)
                b1s1Changed = true;
        }

        if (b1s1Changed)
        {
            // B1S1 changed means BOTH players confirmed the trade
            // Give additional time for the game state to transition to trade animation (0x02)
            // This prevents disconnecting during an active trade that's about to start
            Log("Trade confirmed by both players. Waiting for trade animation to start...");

            int additionalWaitSeconds = 15; // Give 15 extra seconds for animation to start
            for (int i = 0; i < additionalWaitSeconds; i++)
            {
                var currentGameState = await GetGameState(token).ConfigureAwait(false);
                if (currentGameState == 0x02)
                {
                    Log("Trade started! Waiting for completion...");
                    return PokeTradeResult.Success;
                }

                // Check if we're still in trade box (partner disconnected if TradeBoxStatusPointer = 0x00)
                if (!await CheckIfInTradeBox(token).ConfigureAwait(false))
                {
                    Log("No longer in trade box - partner disconnected after confirmation but before animation.");
                    detail.SendNotification(this, "Trade partner disconnected.");
                    return PokeTradeResult.NoTrainerFound;
                }

                await Task.Delay(1_000, token).ConfigureAwait(false);
            }

            // If we still haven't entered trade animation after 15 seconds, something is wrong
            Log("Trade was confirmed but animation never started. Possible connection issue.");
        }

        return PokeTradeResult.TrainerTooSlow;
    }

    #endregion

    #region Online Connection & Portal

    private async Task<bool> ConnectAndEnterPortal(CancellationToken token)
    {
        if (!await CheckIfOnOverworld(token).ConfigureAwait(false))
            await RecoverToOverworld(token).ConfigureAwait(false);

        await Click(X, 3_000, token).ConfigureAwait(false); // Load Menu

        await Click(DUP, 1_000, token).ConfigureAwait(false);
        await Click(A, 2_000, token).ConfigureAwait(false);
        await Click(DRIGHT, 1_000, token).ConfigureAwait(false);
        await Click(DRIGHT, 1_000, token).ConfigureAwait(false);
        await Click(A, 1_000, token).ConfigureAwait(false);
        await Click(DRIGHT, 1_000, token).ConfigureAwait(false);

        bool wasAlreadyConnected = await CheckIfConnectedOnline(token).ConfigureAwait(false);

        if (wasAlreadyConnected)
        {
            await Click(A, 1_000, token).ConfigureAwait(false);
            await Click(A, 1_000, token).ConfigureAwait(false);
            await Task.Delay(1_000, token).ConfigureAwait(false);
        }
        else
        {
            await Click(A, 1_000, token).ConfigureAwait(false);

            int attempts = 0;
            while (!await CheckIfConnectedOnline(token).ConfigureAwait(false))
            {
                await Task.Delay(1_000, token).ConfigureAwait(false);
                if (++attempts > 30)
                {
                    Log("Failed to connect online.");
                    return false;
                }
            }
            await Task.Delay(8_000 + Hub.Config.Timings.ExtraTimeConnectOnline, token).ConfigureAwait(false);
            Log("Connected online.");

            await Click(A, 1_000, token).ConfigureAwait(false);
            await Click(A, 1_000, token).ConfigureAwait(false);
            await Task.Delay(3_000, token).ConfigureAwait(false);
        }

        return true;
    }

    #endregion

    #region Trade Queue Processing

    private async Task DoNothing(CancellationToken token)
    {
        Log("Waiting for trade requests...");
        while (!token.IsCancellationRequested && Config.NextRoutineType == PokeRoutineType.Idle)
            await Task.Delay(1_000, token).ConfigureAwait(false);
    }

    private async Task DoTrades(SAV9ZA sav, CancellationToken token)
    {
        var type = Config.CurrentRoutineType;
        int waitCounter = 0;
        while (!token.IsCancellationRequested && Config.NextRoutineType == type)
        {
            var (detail, priority) = GetTradeData(type);
            if (detail is null)
            {
                await WaitForQueueStep(waitCounter++, token).ConfigureAwait(false);
                continue;
            }
            waitCounter = 0;

            detail.IsProcessing = true;
            Log($"Processing trade request...");
            Hub.Config.Stream.StartTrade(this, detail, Hub);
            Hub.Queues.StartTrade(this, detail);

            await PerformTrade(sav, detail, type, priority, token).ConfigureAwait(false);
        }
    }

    #endregion

    #region Navigation and Recovery

    private async Task DisconnectFromTrade(CancellationToken token)
    {
        Log("Disconnecting from trade...");
        await Click(B, 0_500, token).ConfigureAwait(false);
        await Click(B, 0_500, token).ConfigureAwait(false);
        await Click(B, 0_500, token).ConfigureAwait(false);
        await Click(A, 1_000, token).ConfigureAwait(false);
    }

    private async Task ExitTradeToOverworld(bool unexpected, CancellationToken token)
    {
        if (unexpected)
            Log("Unexpected behavior, recovering to overworld.");

        // Wait 3 seconds after trade completes before attempting to disconnect
        await Task.Delay(3_000, token).ConfigureAwait(false);

        // Check if we're already at overworld
        if (await CheckIfOnOverworld(token).ConfigureAwait(false))
        {
            StartFromOverworld = true;
            _wasConnectedToPartner = false; // Reset flag when successfully back to overworld
            return;
        }

        Log("Exiting trade to overworld...");
        int timeoutSeconds = 30;
        int elapsed = 0;

        while (elapsed < timeoutSeconds)
        {
            // Check if we've reached overworld
            if (await CheckIfOnOverworld(token).ConfigureAwait(false))
            {
                Log("Returned to overworld.");
                StartFromOverworld = true;
                _wasConnectedToPartner = false; // Reset flag when successfully back to overworld
                return;
            }

            // B-A-B pattern: Press B, then A, then B again
            // This handles both "in box" state (A confirms exit) and normal menu states (B backs out)
            await Click(B, 1_000, token).ConfigureAwait(false);
            if (await CheckIfOnOverworld(token).ConfigureAwait(false))
            {
                Log("Returned to overworld.");
                StartFromOverworld = true;
                _wasConnectedToPartner = false;
                return;
            }

            await Click(A, 1_000, token).ConfigureAwait(false);
            if (await CheckIfOnOverworld(token).ConfigureAwait(false))
            {
                Log("Returned to overworld.");
                StartFromOverworld = true;
                _wasConnectedToPartner = false;
                return;
            }

            await Click(B, 1_000, token).ConfigureAwait(false);
            if (await CheckIfOnOverworld(token).ConfigureAwait(false))
            {
                Log("Returned to overworld.");
                StartFromOverworld = true;
                _wasConnectedToPartner = false;
                return;
            }

            elapsed += 3; // 3 seconds per B-A-B cycle
        }

        // Failed to return to overworld - restart the game
        Log("Failed to return to overworld after 30 seconds. Restarting game...");
        await RestartGamePLZA(token).ConfigureAwait(false);
        StartFromOverworld = true;
    }

    #endregion

    #region Game State & Data Access

    private async Task<TradePartnerStatusPLZA> GetTradePartnerFullInfo(CancellationToken token)
    {
        var baseAddr = await SwitchConnection.PointerAll(Offsets.LinkTradePartnerDataPointer, token).ConfigureAwait(false);
        var nidAddr = baseAddr + TradePartnerNIDShift;
        var tidAddr = baseAddr + TradePartnerTIDShift;

        // Read chunk starting from NID location - includes NID, TID at +0x44, and OT at +0x4C
        var chunk = await SwitchConnection.ReadBytesAbsoluteAsync(nidAddr, 0x69, token).ConfigureAwait(false);
        var nid = BitConverter.ToUInt64(chunk.AsSpan(0, 8));
        var dataIsLoaded = chunk[0x68] != 0;

        var trader_info = new TradePartnerStatusPLZA();

        if (dataIsLoaded)
        {
            var tid = chunk.AsSpan(0x44, 4).ToArray();
            var ot = chunk.AsSpan(0x4C, TradePartnerPLZA.MaxByteLengthStringObject).ToArray();
            tid.CopyTo(trader_info.Data, 0x00);
            ot.CopyTo(trader_info.Data, 0x08);

            // Read gender and language from TID location offset
            var genderLang = await SwitchConnection.ReadBytesAbsoluteAsync(tidAddr, 0x08, token).ConfigureAwait(false);
            trader_info.Data[0x04] = genderLang[0x04]; // Gender at TID base + 0x04
            trader_info.Data[0x05] = genderLang[0x05]; // Language at TID base + 0x05
        }
        else
        {
            // Data not at primary location, use fallback
            var fallbackTidAddr = tidAddr + FallBackTradePartnerDataShift;
            var fallbackChunk = await SwitchConnection.ReadBytesAbsoluteAsync(fallbackTidAddr, 34, token).ConfigureAwait(false);

            var tid = fallbackChunk.AsSpan(0, 4).ToArray();
            var ot = fallbackChunk.AsSpan(0x08, TradePartnerPLZA.MaxByteLengthStringObject).ToArray();
            tid.CopyTo(trader_info.Data, 0x00);
            ot.CopyTo(trader_info.Data, 0x08);

            // Read gender and language from fallback TID location
            var genderLang = await SwitchConnection.ReadBytesAbsoluteAsync(fallbackTidAddr, 0x08, token).ConfigureAwait(false);
            trader_info.Data[0x04] = genderLang[0x04]; // Gender at fallback TID + 0x04
            trader_info.Data[0x05] = genderLang[0x05]; // Language at fallback TID + 0x05
        }

        return trader_info;
    }

    private async Task<ulong> GetBoxStartOffset(CancellationToken token)
    {
        if (_cachedBoxOffset.HasValue)
            return _cachedBoxOffset.Value;

        // Get Box 1 Slot 1 address
        var finalOffset = await ResolvePointer(Offsets.BoxStartPokemonPointer, token).ConfigureAwait(false);
        _cachedBoxOffset = finalOffset;
        return finalOffset;
    }

    private async Task<bool> CheckIfOnOverworld(CancellationToken token)
    {
        var offset = await SwitchConnection.PointerAll(Offsets.OverworldPointer, token).ConfigureAwait(false);
        return await IsOnOverworld(offset, token).ConfigureAwait(false);
    }

    private async Task<bool> CheckIfConnectedOnline(CancellationToken token)
    {
        var offset = await SwitchConnection.PointerAll(Offsets.IsConnectedPointer, token).ConfigureAwait(false);
        return await IsConnectedOnline(offset, token).ConfigureAwait(false);
    }

    private async Task<byte> GetGameState(CancellationToken token)
    {
        var offset = await SwitchConnection.PointerAll(Offsets.GameStatePointer, token).ConfigureAwait(false);
        var data = await SwitchConnection.ReadBytesAbsoluteAsync(offset, 1, token).ConfigureAwait(false);
        return data[0];
    }

    private async Task<int> GetCurrentLinkCode(CancellationToken token)
    {
        var offset = await SwitchConnection.PointerAll(Offsets.LinkCodeTradePointer, token).ConfigureAwait(false);
        var data = await SwitchConnection.ReadBytesAbsoluteAsync(offset, 4, token).ConfigureAwait(false);
        return BitConverter.ToInt32(data, 0);
    }

    private async Task<bool> CheckIfInTradeBox(CancellationToken token)
    {
        var offset = await SwitchConnection.PointerAll(Offsets.TradeBoxStatusPointer, token).ConfigureAwait(false);
        var inBox = await IsInTradeBox(offset, token).ConfigureAwait(false);

        if (inBox)
        {
            // Set the offset for trade partner status monitoring
            var (valid, statusOffset) = await ValidatePointerAll(Offsets.TradePartnerStatusPointer, token).ConfigureAwait(false);
            if (valid)
                TradePartnerStatusOffset = statusOffset;
        }

        return inBox;
    }

    #endregion

    #region Trade Result Handling

    private void HandleAbortedTrade(PokeTradeDetail<PA9> detail, PokeRoutineType type, uint priority, PokeTradeResult result)
    {
        // Skip processing if we've already handled the notification (e.g., NoTrainerFound)
        if (result == PokeTradeResult.NoTrainerFound)
            return;

        detail.IsProcessing = false;
        if (result.ShouldAttemptRetry() && detail.Type != PokeTradeType.Random && !detail.IsRetry)
        {
            detail.IsRetry = true;
            Hub.Queues.Enqueue(type, detail, Math.Min(priority, PokeTradePriorities.Tier2));
            detail.SendNotification(this, "Oops! Something happened. I'll requeue you for another attempt.");
        }
        else
        {
            detail.SendNotification(this, $"Oops! Something happened. Canceling the trade: {result}.");
            detail.TradeCanceled(this, result);
        }
    }

    private async Task InnerLoop(SAV9ZA sav, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            Config.IterateNextRoutine();
            var task = Config.CurrentRoutineType switch
            {
                PokeRoutineType.Idle => DoNothing(token),
                _ => DoTrades(sav, token),
            };
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (SocketException e)
            {
                if (e.StackTrace != null)
                    Connection.LogError(e.StackTrace);
                var attempts = Hub.Config.Timings.ReconnectAttempts;
                var delay = Hub.Config.Timings.ExtraReconnectDelay;
                var protocol = Config.Connection.Protocol;
                if (!await TryReconnect(attempts, delay, protocol, token).ConfigureAwait(false))
                    return;

                // Invalidate cached pointers after reconnection - game state may have changed
                _cachedBoxOffset = null;
                Log("Reconnected - cached pointers invalidated.");
            }
        }
    }

    #endregion

    #region Events

    private void OnConnectionError(Exception ex)
    {
        ConnectionError?.Invoke(this, ex);
    }

    private void OnConnectionSuccess()
    {
        ConnectionSuccess?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Specialized Trade Types

    private async Task<PokeTradeResult> PerformBatchTrade(SAV9ZA sav, PokeTradeDetail<PA9> poke, CancellationToken token)
    {
        int completedTrades = 0;
        var startingDetail = poke;
        var originalTrainerID = startingDetail.Trainer.ID;

        var tradesToProcess = poke.BatchTrades ?? [poke.TradeData];
        var totalBatchTrades = tradesToProcess.Count;

        // Cache trade partner info after first successful connection
        TradePartnerStatusPLZA? cachedTradePartnerInfo = null;

        void SendCollectedPokemonAndCleanup()
        {
            var allReceived = BatchTracker.GetReceivedPokemon(originalTrainerID);
            if (allReceived.Count > 0)
            {
                poke.SendNotification(this, $"Sending you the {allReceived.Count} Pokémon you traded to me before the interruption.");

                Log($"Returning {allReceived.Count} Pokémon to trainer {originalTrainerID}.");

                // Send each Pokemon directly instead of calling TradeFinished
                for (int j = 0; j < allReceived.Count; j++)
                {
                    var pokemon = allReceived[j];
                    var speciesName = SpeciesName.GetSpeciesName(pokemon.Species, 2);
                    Log($"Returning: {speciesName}");

                    // Send the Pokemon directly to the notifier
                    poke.SendNotification(this, pokemon, $"Pokémon you traded to me: {speciesName}");
                    Thread.Sleep(500);
                }
            }
            else
            {
                Log($"No Pokémon found to return for trainer {originalTrainerID}.");
            }

            BatchTracker.ClearReceivedPokemon(originalTrainerID);
            BatchTracker.ReleaseBatch(originalTrainerID, startingDetail.UniqueTradeID);
            poke.IsProcessing = false;
            Hub.Queues.Info.Remove(new TradeEntry<PA9>(poke, originalTrainerID, PokeRoutineType.Batch, poke.Trainer.TrainerName, poke.UniqueTradeID));
        }

        for (int i = 0; i < totalBatchTrades; i++)
        {
            var currentTradeIndex = i;
            var toSend = tradesToProcess[currentTradeIndex];
            ulong boxOffset;

            poke.TradeData = toSend;
            poke.Notifier.UpdateBatchProgress(currentTradeIndex + 1, toSend, poke.UniqueTradeID);

            // For subsequent trades (after first), we've already prepared the Pokemon during the previous trade animation
            // No need to prepare here - just send notification
            if (currentTradeIndex > 0)
            {
                poke.SendNotification(this, $"**Ready!** You can now offer your Pokémon for trade {currentTradeIndex + 1}/{totalBatchTrades}.");
                await Task.Delay(2_000, token).ConfigureAwait(false);
            }

            // For first trade only - search for partner
            if (currentTradeIndex == 0)
            {
                await Click(A, 0_500, token).ConfigureAwait(false);
                await Click(A, 0_500, token).ConfigureAwait(false);

                WaitAtBarrierIfApplicable(token);
                await Click(A, 1_000, token).ConfigureAwait(false);

                poke.TradeSearching(this);
                var partnerWaitResult = await WaitForTradePartner(token).ConfigureAwait(false);

                if (token.IsCancellationRequested)
                {
                    StartFromOverworld = true;
                    await ExitTradeToOverworld(false, token).ConfigureAwait(false);
                    poke.SendNotification(this, "Canceling the batch trades. The routine has been interrupted.");
                    SendCollectedPokemonAndCleanup();
                    return PokeTradeResult.RoutineCancel;
                }

                if (partnerWaitResult == TradePartnerWaitResult.Timeout)
                {
                    // Partner never showed up - their fault, don't requeue
                    poke.IsProcessing = false;
                    poke.SendNotification(this, "No trading partner found. Canceling the batch trades.");
                    poke.TradeCanceled(this, PokeTradeResult.NoTrainerFound);
                    SendCollectedPokemonAndCleanup();

                    await RecoverToOverworld(token).ConfigureAwait(false);
                    return PokeTradeResult.NoTrainerFound;
                }

                if (partnerWaitResult == TradePartnerWaitResult.KickedToMenu)
                {
                    // Bot got kicked to menu - our fault, trigger requeue
                    Log("Connection error. Retrying...");
                    SendCollectedPokemonAndCleanup();
                    await RecoverToOverworld(token).ConfigureAwait(false);
                    return PokeTradeResult.RecoverStart;
                }

                Hub.Config.Stream.EndEnterCode(this);

                // Wait until we're in the trade box
                Log("Searching for trade partner...");
                int boxCheckAttempts = 0;
                while (!await CheckIfInTradeBox(token).ConfigureAwait(false))
                {
                    await Task.Delay(500, token).ConfigureAwait(false);
                    if (++boxCheckAttempts > 30) // 15 seconds max
                    {
                        Log("No trade partner found.");
                        return PokeTradeResult.NoTrainerFound;
                    }
                }

                // Wait for trade UI and partner data to load
                await Task.Delay(2_000, token).ConfigureAwait(false);

                // Now that data has loaded, read partner info
                var tradePartnerFullInfo = await GetTradePartnerFullInfo(token).ConfigureAwait(false);
                cachedTradePartnerInfo = tradePartnerFullInfo; // Cache for subsequent trades
                var tradePartner = new TradePartnerPLZA(tradePartnerFullInfo);

                var trainerNID = await GetTradePartnerNID(token).ConfigureAwait(false);

                Log($"[TradePartner] OT: {tradePartner.TrainerName}, TID: {tradePartner.TID7}, SID: {tradePartner.SID7}, Gender: {tradePartnerFullInfo.Gender}, Language: {tradePartnerFullInfo.Language}, NID: {trainerNID}");

                RecordUtil<PokeTradeBotPLZA>.Record($"Initiating\t{trainerNID:X16}\t{tradePartner.TrainerName}\t{poke.Trainer.TrainerName}\t{poke.Trainer.ID}\t{poke.ID}\t{toSend.EncryptionConstant:X8}");

                poke.SendNotification(this, $"Found trade partner: {tradePartner.TrainerName}. **TID**: {tradePartner.TID7} **SID**: {tradePartner.SID7}");

                var tradeCodeStorage = new TradeCodeStorage();
                var existingTradeDetails = tradeCodeStorage.GetTradeDetails(poke.Trainer.ID);

                bool shouldUpdateOT = existingTradeDetails?.OT != tradePartner.TrainerName;
                bool shouldUpdateTID = existingTradeDetails?.TID != int.Parse(tradePartner.TID7);
                bool shouldUpdateSID = existingTradeDetails?.SID != int.Parse(tradePartner.SID7);

                if (shouldUpdateOT || shouldUpdateTID || shouldUpdateSID)
                {
                    string? ot = shouldUpdateOT ? tradePartner.TrainerName : existingTradeDetails?.OT;
                    int? tid = shouldUpdateTID ? int.Parse(tradePartner.TID7) : existingTradeDetails?.TID;
                    int? sid = shouldUpdateSID ? int.Parse(tradePartner.SID7) : existingTradeDetails?.SID;

                    if (ot != null && tid.HasValue && sid.HasValue)
                    {
                        tradeCodeStorage.UpdateTradeDetails(poke.Trainer.ID, ot, tid.Value, sid.Value);
                    }
                }

                var partnerCheck = CheckPartnerReputation(this, poke, trainerNID, tradePartner.TrainerName, AbuseSettings, token);
                if (partnerCheck != PokeTradeResult.Success)
                {
                    poke.SendNotification(this, "Trade partner blocked. Canceling trades.");
                    SendCollectedPokemonAndCleanup();
                    await Click(A, 1_000, token).ConfigureAwait(false);
                    await ExitTradeToOverworld(false, token).ConfigureAwait(false);
                    return partnerCheck;
                }

                poke.SendNotification(this, $"Found trade partner: {tradePartner.TrainerName}. **TID**: {tradePartner.TID7} **SID**: {tradePartner.SID7}");

                // Apply AutoOT for first trade if needed
                if (Hub.Config.Legality.UseTradePartnerInfo && !poke.IgnoreAutoOT)
                {
                    toSend = await ApplyAutoOT(toSend, tradePartnerFullInfo, sav, token).ConfigureAwait(false);
                    poke.TradeData = toSend;
                    // Give game time to refresh trade offer display with AutoOT Pokemon
                    await Task.Delay(3_000, token).ConfigureAwait(false);
                }
            }

            if (currentTradeIndex == 0)
            {
                poke.SendNotification(this, $"Please offer your Pokémon for trade 1/{totalBatchTrades}.");
            }

            var offsetBeforeBatch = await GetBoxStartOffset(token).ConfigureAwait(false);
            var pokemonBeforeBatchTrade = await ReadPokemon(offsetBeforeBatch, BoxFormatSlotSize, token).ConfigureAwait(false);
            var checksumBeforeBatchTrade = pokemonBeforeBatchTrade.Checksum;

            Log($"Confirming trade {currentTradeIndex + 1}/{totalBatchTrades}.");
            var tradeResult = await ConfirmAndStartTrading(poke, checksumBeforeBatchTrade, token).ConfigureAwait(false);
            if (tradeResult != PokeTradeResult.Success)
            {
                poke.SendNotification(this, $"Trade failed for trade {currentTradeIndex + 1}/{totalBatchTrades}. Canceling remaining trades.");
                SendCollectedPokemonAndCleanup();
                if (tradeResult == PokeTradeResult.TrainerTooSlow)
                {
                    await DisconnectFromTrade(token).ConfigureAwait(false);
                }
                await ExitTradeToOverworld(false, token).ConfigureAwait(false);
                return tradeResult;
            }

            // Wait for trade to complete
            Log($"Confirming trade {currentTradeIndex + 1}/{totalBatchTrades}...");

            int maxBatchWaitSeconds = Hub.Config.Trade.TradeConfiguration.TradeWaitTime;
            int elapsedBatch = 0;
            bool batchTradeAnimationStarted = false;
            bool batchTradeCompleted = false;
            bool batchWarningSent = false;

            // First, wait for GameState to become 0x02 (trade animation in progress)
            while (elapsedBatch < maxBatchWaitSeconds && !batchTradeAnimationStarted)
            {
                await Task.Delay(1_000, token).ConfigureAwait(false);
                elapsedBatch++;

                // Send warning 10 seconds before timeout
                if (!batchWarningSent && elapsedBatch == maxBatchWaitSeconds - 10 && maxBatchWaitSeconds >= 10)
                {
                    poke.SendNotification(this, "Hey! Pick a Pokemon to trade or I am leaving!");
                    batchWarningSent = true;
                }

                var currentState = await GetGameState(token).ConfigureAwait(false);
                if (currentState == 0x02)
                {
                    batchTradeAnimationStarted = true;

                    // B1S1 has changed - immediately read and save the received Pokemon
                    boxOffset = await GetBoxStartOffset(token).ConfigureAwait(false);
                    var receivedPokemon = await ReadPokemon(boxOffset, BoxFormatSlotSize, token).ConfigureAwait(false);
                    Log($"Trade {currentTradeIndex + 1} confirmed - received {(Species)receivedPokemon.Species}");

                    // Immediately inject the next Pokemon if there is one
                    if (currentTradeIndex + 1 < totalBatchTrades)
                    {
                        var nextPokemon = tradesToProcess[currentTradeIndex + 1];

                        // Apply AutoOT if needed
                        if (Hub.Config.Legality.UseTradePartnerInfo && !poke.IgnoreAutoOT && cachedTradePartnerInfo != null)
                        {
                            nextPokemon = await ApplyAutoOT(nextPokemon, cachedTradePartnerInfo, sav, token);
                            tradesToProcess[currentTradeIndex + 1] = nextPokemon;
                        }
                        else
                        {
                            // No AutoOT - inject directly
                            await SetBoxPokemonAbsolute(boxOffset, nextPokemon, token, sav).ConfigureAwait(false);
                        }

                        Log($"Next Pokemon ({currentTradeIndex + 2}/{totalBatchTrades}) injected into B1S1");
                    }
                }
            }

            if (!batchTradeAnimationStarted)
            {
                Log($"Trade {currentTradeIndex + 1}/{totalBatchTrades} was not confirmed.");
                poke.SendNotification(this, $"Trade {currentTradeIndex + 1}/{totalBatchTrades} was not confirmed. Canceling remaining trades.");
                SendCollectedPokemonAndCleanup();
                await DisconnectFromTrade(token).ConfigureAwait(false);
                await ExitTradeToOverworld(false, token).ConfigureAwait(false);
                return PokeTradeResult.TrainerTooSlow;
            }

            // Now wait for GameState to return to 0x01 (trade animation complete)
            while (elapsedBatch < maxBatchWaitSeconds)
            {
                await Task.Delay(1_000, token).ConfigureAwait(false);
                elapsedBatch++;

                var currentState = await GetGameState(token).ConfigureAwait(false);

                if (currentState == 0x01) // Trade animation finished!
                {
                    batchTradeCompleted = true;
                    break;
                }
            }

            if (!batchTradeCompleted)
            {
                Log($"Trade {currentTradeIndex + 1}/{totalBatchTrades} timed out.");
                poke.SendNotification(this, $"Trade {currentTradeIndex + 1}/{totalBatchTrades} timed out. Canceling remaining trades.");
                SendCollectedPokemonAndCleanup();
                await DisconnectFromTrade(token).ConfigureAwait(false);
                await ExitTradeToOverworld(false, token).ConfigureAwait(false);
                return PokeTradeResult.TrainerTooSlow;
            }

            if (token.IsCancellationRequested)
            {
                StartFromOverworld = true;
                poke.SendNotification(this, "Canceling batch trades.");
                SendCollectedPokemonAndCleanup();
                await ExitTradeToOverworld(false, token).ConfigureAwait(false);
                return PokeTradeResult.RoutineCancel;
            }

            // Read received Pokemon immediately after trade completes, before injecting next Pokemon
            boxOffset = await GetBoxStartOffset(token).ConfigureAwait(false);
            var received = await ReadPokemon(boxOffset, BoxFormatSlotSize, token).ConfigureAwait(false);
            var checksumAfterBatchTrade = received.Checksum;

            if (checksumBeforeBatchTrade == checksumAfterBatchTrade)
            {
                Log($"Batch trade {currentTradeIndex + 1}/{totalBatchTrades} was canceled or did not occur.");
                poke.SendNotification(this, $"Trade {currentTradeIndex + 1}/{totalBatchTrades} was canceled. Canceling remaining trades.");
                SendCollectedPokemonAndCleanup();
                await DisconnectFromTrade(token).ConfigureAwait(false);
                await ExitTradeToOverworld(false, token).ConfigureAwait(false);
                return PokeTradeResult.TrainerTooSlow;
            }

            Log($"Trade {currentTradeIndex + 1}/{totalBatchTrades} complete! Received {(Species)received.Species}.");

            // NOW prepare and inject the next Pokemon (after we've read what we received)
            if (currentTradeIndex + 1 < totalBatchTrades)
            {
                Log($"Preparing next Pokémon ({currentTradeIndex + 2}/{totalBatchTrades})...");

                var nextTradeIndex = currentTradeIndex + 1;
                var nextToSend = tradesToProcess[nextTradeIndex];

                if (nextToSend.Species != 0)
                {
                    if (Hub.Config.Legality.UseTradePartnerInfo && !poke.IgnoreAutoOT && cachedTradePartnerInfo != null)
                    {
                        nextToSend = await ApplyAutoOT(nextToSend, cachedTradePartnerInfo, sav, token);
                        tradesToProcess[nextTradeIndex] = nextToSend; // Update the list
                    }
                    else
                    {
                        // AutoOT not applied, inject directly
                        var nextBoxOffset = await GetBoxStartOffset(token).ConfigureAwait(false);
                        await SetBoxPokemonAbsolute(nextBoxOffset, nextToSend, token, sav).ConfigureAwait(false);
                    }
                }

                Log($"Next Pokémon prepared and injected!");
            }

            UpdateCountsAndExport(poke, received, toSend);

            // Get the trainer NID and name for logging
            var logTrainerNID = currentTradeIndex == 0 ? await GetTradePartnerNID(token).ConfigureAwait(false) : 0;
            var logPartner = cachedTradePartnerInfo != null ? new TradePartnerPLZA(cachedTradePartnerInfo) : null;
            LogSuccessfulTrades(poke, logTrainerNID, logPartner?.TrainerName ?? "Unknown");

            BatchTracker.AddReceivedPokemon(originalTrainerID, received);
            completedTrades = currentTradeIndex + 1;

            if (completedTrades == totalBatchTrades)
            {
                // Get all collected Pokemon before cleaning anything up
                var allReceived = BatchTracker.GetReceivedPokemon(originalTrainerID);

                // First send notification that trades are complete
                poke.SendNotification(this, "All batch trades completed! Thank you for trading!");

                // Send back all received Pokemon if ReturnPKMs is enabled
                if (Hub.Config.Discord.ReturnPKMs && allReceived.Count > 0)
                {
                    poke.SendNotification(this, $"Here are the {allReceived.Count} Pokémon you traded to me:");

                    // Send each Pokemon directly instead of calling TradeFinished
                    for (int j = 0; j < allReceived.Count; j++)
                    {
                        var pokemon = allReceived[j];
                        var speciesName = SpeciesName.GetSpeciesName(pokemon.Species, 2);

                        // Send the Pokemon directly to the notifier
                        poke.SendNotification(this, pokemon, $"Pokémon you traded to me: {speciesName}");
                        await Task.Delay(500, token).ConfigureAwait(false);
                    }
                }

                // Now call TradeFinished ONCE for the entire batch with the last received Pokemon
                // This signals that the entire batch trade transaction is complete
                if (allReceived.Count > 0)
                {
                    poke.TradeFinished(this, allReceived[^1]);
                }
                else
                {
                    poke.TradeFinished(this, received);
                }

                // Mark the batch as fully completed and clean up
                Hub.Queues.CompleteTrade(this, startingDetail);
                BatchTracker.ClearReceivedPokemon(originalTrainerID);

                // Exit the trade state
                await ExitTradeToOverworld(false, token).ConfigureAwait(false);
                poke.IsProcessing = false;
                break;
            }

            // Next trade is already prepared - give game a moment to refresh the UI
            if (currentTradeIndex + 1 < totalBatchTrades)
            {
                Log($"Ready for next trade ({currentTradeIndex + 2}/{totalBatchTrades})...");
                await Task.Delay(2_000, token).ConfigureAwait(false);
            }
        }

        // Ensure we exit properly even if the loop breaks unexpectedly
        await ExitTradeToOverworld(false, token).ConfigureAwait(false);
        poke.IsProcessing = false;
        return PokeTradeResult.Success;
    }

    #endregion

    #region Core Trade Logic

    private async Task PerformTrade(SAV9ZA sav, PokeTradeDetail<PA9> detail, PokeRoutineType type, uint priority, CancellationToken token)
    {
        PokeTradeResult result;
        try
        {
            // All trades go through PerformLinkCodeTrade which will handle both regular and batch trades
            result = await PerformLinkCodeTrade(sav, detail, token).ConfigureAwait(false);

            if (result != PokeTradeResult.Success)
            {
                if (detail.Type == PokeTradeType.Batch)
                    await HandleAbortedBatchTrade(detail, type, priority, result, token).ConfigureAwait(false);
                else
                    HandleAbortedTrade(detail, type, priority, result);
            }
        }
        catch (SocketException socket)
        {
            Log(socket.Message);
            result = PokeTradeResult.ExceptionConnection;
            if (detail.Type == PokeTradeType.Batch)
                await HandleAbortedBatchTrade(detail, type, priority, result, token).ConfigureAwait(false);
            else
                HandleAbortedTrade(detail, type, priority, result);
            throw;
        }
        catch (Exception e)
        {
            Log(e.Message);
            result = PokeTradeResult.ExceptionInternal;
            if (detail.Type == PokeTradeType.Batch)
                await HandleAbortedBatchTrade(detail, type, priority, result, token).ConfigureAwait(false);
            else
                HandleAbortedTrade(detail, type, priority, result);
        }
    }

    private async Task<PokeTradeResult> PerformLinkCodeTrade(SAV9ZA sav, PokeTradeDetail<PA9> poke, CancellationToken token)
    {
        // Check if trade was canceled by user
        if (poke.IsCanceled)
        {
            Log($"Trade for {poke.Trainer.TrainerName} was canceled by user.");
            poke.TradeCanceled(this, PokeTradeResult.UserCanceled);
            return PokeTradeResult.UserCanceled;
        }

        // Update Barrier Settings
        UpdateBarrier(poke.IsSynchronized);
        poke.TradeInitialize(this);
        Hub.Config.Stream.EndEnterCode(this);

        // Handle connection and portal entry FIRST
        if (!await EnsureConnectedAndInPortal(token).ConfigureAwait(false))
        {
            return PokeTradeResult.RecoverStart;
        }

        // Enter Link Trade and code
        var result = await EnterLinkTradeAndCode(poke, poke.Code, token).ConfigureAwait(false);

        if (result == LinkCodeEntryResult.VerificationFailedMismatch)
        {
            // Code didn't match - something went wrong, restart game
            Log("Code verification failed. Restarting game...");
            await RestartGamePLZA(token).ConfigureAwait(false);
            return PokeTradeResult.RecoverStart;
        }

        // Inject Pokemon AFTER code verification succeeds and BEFORE searching
        var toSend = poke.TradeData;
        if (toSend.Species != 0)
        {
            Log("Preparing Pokemon for trade...");
            var offset = await GetBoxStartOffset(token).ConfigureAwait(false);
            await SetBoxPokemonAbsolute(offset, toSend, token, sav).ConfigureAwait(false);
        }

        StartFromOverworld = false;

        // Route to appropriate trade handling based on trade type
        if (poke.Type == PokeTradeType.Batch)
            return await PerformBatchTrade(sav, poke, token).ConfigureAwait(false);

        return await PerformNonBatchTrade(sav, poke, token).ConfigureAwait(false);
    }

    private async Task<bool> EnsureConnectedAndInPortal(CancellationToken token)
    {
        if (StartFromOverworld)
        {
            if (!await CheckIfOnOverworld(token).ConfigureAwait(false))
            {
                await RecoverToOverworld(token).ConfigureAwait(false);
            }

            if (!await ConnectAndEnterPortal(token).ConfigureAwait(false))
            {
                Log("Connection error. Restarting...");
                await RecoverToOverworld(token).ConfigureAwait(false);
                return false;
            }
        }
        else if (!await CheckIfConnectedOnline(token).ConfigureAwait(false))
        {
            await RecoverToOverworld(token).ConfigureAwait(false);
            if (!await ConnectAndEnterPortal(token).ConfigureAwait(false))
            {
                Log("Connection failed. Restarting...");
                await RecoverToOverworld(token).ConfigureAwait(false);
                return false;
            }
        }

        return true;
    }

    private async Task<LinkCodeEntryResult> EnterLinkTradeAndCode(PokeTradeDetail<PA9> poke, int code, CancellationToken token)
    {
        // Loading code entry
        if (poke.Type != PokeTradeType.Random)
        {
            Hub.Config.Stream.StartEnterCode(this);
        }
        // Read current code to determine if we need to clear
        var currentCode = await GetCurrentLinkCode(token).ConfigureAwait(false);

        // If there's a non-zero code, clear it
        if (currentCode != 0)
        {
            var formattedCode = $"{currentCode:00000000}";
            var digitCount = formattedCode.Length;
            await Task.Delay(1_000, token).ConfigureAwait(false);

            for (int i = 0; i < digitCount; i++)
                await Click(B, 0, token).ConfigureAwait(false);

            await Task.Delay(1_000, token).ConfigureAwait(false);
        }

        // Enter the new code
        await EnterLinkCode(code, Hub.Config, token).ConfigureAwait(false);
        await Click(PLUS, 2_000, token).ConfigureAwait(false);

        // Verify the code was entered correctly (memory updates immediately after PLUS)
        var verifyCode = await GetCurrentLinkCode(token).ConfigureAwait(false);

        if (verifyCode != code)
        {
            return LinkCodeEntryResult.VerificationFailedMismatch;
        }

        return LinkCodeEntryResult.Success;
    }

    private async Task<PokeTradeResult> PerformNonBatchTrade(SAV9ZA sav, PokeTradeDetail<PA9> poke, CancellationToken token)
    {
        var toSend = poke.TradeData;

        await Click(A, 0_500, token).ConfigureAwait(false);
        await Click(A, 0_500, token).ConfigureAwait(false);

        WaitAtBarrierIfApplicable(token);
        await Click(A, 1_000, token).ConfigureAwait(false);

        poke.TradeSearching(this);
        var partnerWaitResult = await WaitForTradePartner(token).ConfigureAwait(false);

        if (token.IsCancellationRequested)
        {
            StartFromOverworld = true;
            await ExitTradeToOverworld(false, token).ConfigureAwait(false);
            return PokeTradeResult.RoutineCancel;
        }

        if (partnerWaitResult == TradePartnerWaitResult.Timeout)
        {
            // Partner never showed up - their fault, don't requeue
            poke.IsProcessing = false;
            poke.SendNotification(this, "No trading partner found. Canceling the trade.");
            poke.TradeCanceled(this, PokeTradeResult.NoTrainerFound);

            await RecoverToOverworld(token).ConfigureAwait(false);
            return PokeTradeResult.NoTrainerFound;
        }

        if (partnerWaitResult == TradePartnerWaitResult.KickedToMenu)
        {
            // Bot got kicked to menu - our fault, trigger requeue
            Log("Connection error. Retrying...");
            await RecoverToOverworld(token).ConfigureAwait(false);
            return PokeTradeResult.RecoverStart;
        }

        Hub.Config.Stream.EndEnterCode(this);

        // Wait until we're in the trade box
        Log("Searching for trade partner...");
        int boxCheckAttempts = 0;
        while (!await CheckIfInTradeBox(token).ConfigureAwait(false))
        {
            await Task.Delay(500, token).ConfigureAwait(false);
            if (++boxCheckAttempts > 30) // 15 seconds max
            {
                Log("No trade partner found.");
                return PokeTradeResult.NoTrainerFound;
            }
        }

        // Wait for trade UI and partner data to load
        await Task.Delay(5_000, token).ConfigureAwait(false);

        // Now that data has loaded, read partner info
        var tradePartnerFullInfo = await GetTradePartnerFullInfo(token).ConfigureAwait(false);
        var tradePartner = new TradePartnerPLZA(tradePartnerFullInfo);

        var trainerNID = await GetTradePartnerNID(token).ConfigureAwait(false);

        Log($"[TradePartner] OT: {tradePartner.TrainerName}, TID: {tradePartner.TID7}, SID: {tradePartner.SID7}, Gender: {tradePartnerFullInfo.Gender}, Language: {tradePartnerFullInfo.Language}, NID: {trainerNID}");

        RecordUtil<PokeTradeBotPLZA>.Record($"Initiating\t{trainerNID:X16}\t{tradePartner.TrainerName}\t{poke.Trainer.TrainerName}\t{poke.Trainer.ID}\t{poke.ID}\t{toSend.EncryptionConstant:X8}");
        poke.SendNotification(this, $"Found trade partner: {tradePartner.TrainerName}. **TID**: {tradePartner.TID7} **SID**: {tradePartner.SID7} Waiting for a Pokémon...");

        var tradeCodeStorage = new TradeCodeStorage();
        var existingTradeDetails = tradeCodeStorage.GetTradeDetails(poke.Trainer.ID);

        bool shouldUpdateOT = existingTradeDetails?.OT != tradePartner.TrainerName;
        bool shouldUpdateTID = existingTradeDetails?.TID != int.Parse(tradePartner.TID7);
        bool shouldUpdateSID = existingTradeDetails?.SID != int.Parse(tradePartner.SID7);

        if (shouldUpdateOT || shouldUpdateTID || shouldUpdateSID)
        {
            string? ot = shouldUpdateOT ? tradePartner.TrainerName : existingTradeDetails?.OT;
            int? tid = shouldUpdateTID ? int.Parse(tradePartner.TID7) : existingTradeDetails?.TID;
            int? sid = shouldUpdateSID ? int.Parse(tradePartner.SID7) : existingTradeDetails?.SID;

            if (ot != null && tid.HasValue && sid.HasValue)
            {
                tradeCodeStorage.UpdateTradeDetails(poke.Trainer.ID, ot, tid.Value, sid.Value);
            }
        }

        var partnerCheck = CheckPartnerReputation(this, poke, trainerNID, tradePartner.TrainerName, AbuseSettings, token);
        if (partnerCheck != PokeTradeResult.Success)
        {
            await Click(A, 1_000, token).ConfigureAwait(false);
            await ExitTradeToOverworld(false, token).ConfigureAwait(false);
            return partnerCheck;
        }

        // Read the offered Pokemon for Clone/Dump trades
        PA9? offered = null;
        if (poke.Type == PokeTradeType.Clone || poke.Type == PokeTradeType.Dump)
        {
            offered = await ReadUntilPresentPointer(Offsets.LinkTradePartnerPokemonPointer, 3_000, 0_500, BoxFormatSlotSize, token).ConfigureAwait(false);
            if (offered == null || offered.Species == 0)
            {
                poke.SendNotification(this, "Failed to read offered Pokémon. Exiting trade.");
                await ExitTradeToOverworld(true, token).ConfigureAwait(false);
                return PokeTradeResult.TrainerRequestBad;
            }
        }

        if (poke.Type == PokeTradeType.Clone)
        {
            var (result, clone) = await ProcessCloneTradeAsync(poke, sav, offered!, token).ConfigureAwait(false);
            if (result != PokeTradeResult.Success)
            {
                await ExitTradeToOverworld(false, token).ConfigureAwait(false);
                return result;
            }

            // Trade them back their cloned Pokemon
            toSend = clone!;
        }

        if (poke.Type == PokeTradeType.Dump)
        {
            var result = await ProcessDumpTradeAsync(poke, token).ConfigureAwait(false);
            await ExitTradeToOverworld(false, token).ConfigureAwait(false);
            return result;
        }

        if (Hub.Config.Legality.UseTradePartnerInfo && !poke.IgnoreAutoOT)
        {
            toSend = await ApplyAutoOT(toSend, tradePartnerFullInfo, sav, token);
            // Give game time to refresh trade offer display with AutoOT Pokemon
            await Task.Delay(3_000, token).ConfigureAwait(false);
        }

        SpecialTradeType itemReq = SpecialTradeType.None;
        if (poke.Type == PokeTradeType.Seed)
        {
            poke.SendNotification(this, "Seed trades are temporarily unavailable. Please request a specific Pokemon instead.");
            await ExitTradeToOverworld(true, token).ConfigureAwait(false);
            return PokeTradeResult.TrainerRequestBad;
        }

        if (itemReq == SpecialTradeType.WonderCard)
            poke.SendNotification(this, "Distribution success!");
        else if (itemReq != SpecialTradeType.None && itemReq != SpecialTradeType.Shinify)
            poke.SendNotification(this, "Special request successful!");
        else if (itemReq == SpecialTradeType.Shinify)
            poke.SendNotification(this, "Shinify success! Thanks for being part of the community!");

        var offsetBefore = await GetBoxStartOffset(token).ConfigureAwait(false);
        var pokemonBeforeTrade = await ReadPokemon(offsetBefore, BoxFormatSlotSize, token).ConfigureAwait(false);
        var checksumBeforeTrade = pokemonBeforeTrade.Checksum;

        var tradeResult = await ConfirmAndStartTrading(poke, checksumBeforeTrade, token).ConfigureAwait(false);
        if (tradeResult != PokeTradeResult.Success)
        {
            if (tradeResult == PokeTradeResult.TrainerTooSlow)
            {
                await DisconnectFromTrade(token).ConfigureAwait(false);
            }
            await ExitTradeToOverworld(false, token).ConfigureAwait(false);
            return tradeResult;
        }

        if (token.IsCancellationRequested)
        {
            StartFromOverworld = true;
            await ExitTradeToOverworld(false, token).ConfigureAwait(false);
            return PokeTradeResult.RoutineCancel;
        }

        Log("Confirming trade...");

        int maxWaitSeconds = Hub.Config.Trade.TradeConfiguration.TradeWaitTime;
        int elapsed = 0;
        bool tradeAnimationStarted = false;
        bool tradeCompleted = false;
        bool warningSent = false;

        // First, wait for GameState to become 0x02 (trade animation in progress)
        while (elapsed < maxWaitSeconds && !tradeAnimationStarted)
        {
            await Task.Delay(1_000, token).ConfigureAwait(false);
            elapsed++;

            // Send warning 10 seconds before timeout
            if (!warningSent && elapsed == maxWaitSeconds - 10 && maxWaitSeconds >= 10)
            {
                poke.SendNotification(this, "Hey! Pick a Pokemon to trade or I am leaving!");
                warningSent = true;
            }

            var currentState = await GetGameState(token).ConfigureAwait(false);
            if (currentState == 0x02)
            {
                tradeAnimationStarted = true;
            }
        }

        if (!tradeAnimationStarted)
        {
            Log("Trade was not confirmed.");
            await DisconnectFromTrade(token).ConfigureAwait(false);
            await ExitTradeToOverworld(false, token).ConfigureAwait(false);
            return PokeTradeResult.TrainerTooSlow;
        }

        // Now wait for GameState to return to 0x01 (trade animation complete)
        while (elapsed < maxWaitSeconds)
        {
            await Task.Delay(1_000, token).ConfigureAwait(false);
            elapsed++;

            var currentState = await GetGameState(token).ConfigureAwait(false);
            if (currentState == 0x01)
            {
                tradeCompleted = true;
                break;
            }
        }

        if (!tradeCompleted)
        {
            Log("Trade timed out.");
            await DisconnectFromTrade(token).ConfigureAwait(false);
            await ExitTradeToOverworld(false, token).ConfigureAwait(false);
            return PokeTradeResult.TrainerTooSlow;
        }

        // CRITICAL: Verify that Box 1 Slot 1 actually changed (trade occurred)
        var offset2 = await GetBoxStartOffset(token).ConfigureAwait(false);
        var received = await ReadPokemon(offset2, BoxFormatSlotSize, token).ConfigureAwait(false);
        var checksumAfterTrade = received.Checksum;

        if (checksumBeforeTrade == checksumAfterTrade)
        {
            Log("Trade was canceled.");
            poke.SendNotification(this, "Trade was canceled. Please try again.");
            await DisconnectFromTrade(token).ConfigureAwait(false);
            await ExitTradeToOverworld(false, token).ConfigureAwait(false);
            return PokeTradeResult.TrainerTooSlow;
        }

        Log($"Trade complete! Received {(Species)received.Species}.");

        poke.TradeFinished(this, received);
        UpdateCountsAndExport(poke, received, toSend);
        LogSuccessfulTrades(poke, trainerNID, tradePartner.TrainerName);

        await ExitTradeToOverworld(false, token).ConfigureAwait(false);
        return PokeTradeResult.Success;
    }

    private async Task HandleAbortedBatchTrade(PokeTradeDetail<PA9> detail, PokeRoutineType type, uint priority, PokeTradeResult result, CancellationToken token)
    {
        detail.IsProcessing = false;

        // Always remove from UsersInQueue on abort
        Hub.Queues.Info.Remove(new TradeEntry<PA9>(detail, detail.Trainer.ID, type, detail.Trainer.TrainerName, detail.UniqueTradeID));

        if (detail.TotalBatchTrades > 1)
        {
            // Release the batch claim on failure
            BatchTracker.ReleaseBatch(detail.Trainer.ID, detail.UniqueTradeID);

            if (result.ShouldAttemptRetry() && detail.Type != PokeTradeType.Random && !detail.IsRetry)
            {
                detail.IsRetry = true;
                Hub.Queues.Enqueue(type, detail, Math.Min(priority, PokeTradePriorities.Tier2));
                detail.SendNotification(this, "Oops! Something happened during your batch trade. I'll requeue you for another attempt.");
            }
            else
            {
                detail.SendNotification(this, $"Batch trade failed: {result}");
                detail.TradeCanceled(this, result);
                await ExitTradeToOverworld(false, token).ConfigureAwait(false);
            }
        }
        else
        {
            HandleAbortedTrade(detail, type, priority, result);
        }
    }

    private async Task<bool> RecoverToOverworld(CancellationToken token)
    {
        if (await CheckIfOnOverworld(token).ConfigureAwait(false))
            return true;

        Log("Recovering...");

        await Click(B, 1_500, token).ConfigureAwait(false);
        if (await CheckIfOnOverworld(token).ConfigureAwait(false))
            return true;

        await Click(A, 1_500, token).ConfigureAwait(false);
        if (await CheckIfOnOverworld(token).ConfigureAwait(false))
            return true;

        var attempts = 0;
        while (!await CheckIfOnOverworld(token).ConfigureAwait(false))
        {
            attempts++;
            if (attempts >= 30)
                break;

            await Click(B, 1_000, token).ConfigureAwait(false);
            if (await CheckIfOnOverworld(token).ConfigureAwait(false))
                break;

            await Click(B, 1_000, token).ConfigureAwait(false);
            if (await CheckIfOnOverworld(token).ConfigureAwait(false))
                break;
        }

        if (!await CheckIfOnOverworld(token).ConfigureAwait(false))
        {
            Log("Restarting game...");
            await RestartGamePLZA(token).ConfigureAwait(false);
        }
        await Task.Delay(1_000, token).ConfigureAwait(false);

        StartFromOverworld = true;
        return true;
    }

    private async Task RestartGamePLZA(CancellationToken token)
    {
        await ReOpenGame(Hub.Config, token).ConfigureAwait(false);
        _cachedBoxOffset = null; // Invalidate box offset cache after restart

        // If we were connected to a partner before restart, prevent soft ban
        if (_wasConnectedToPartner)
        {
            Log("Preventing trade soft ban - connecting with random partner to clear trade state...");
            await PreventTradeSoftBan(token).ConfigureAwait(false);
            _wasConnectedToPartner = false; // Reset the flag after recovery
        }
    }

    /// <summary>
    /// Prevents trade soft ban after restarting during an active trade connection.
    ///
    /// When the bot restarts AFTER successfully connecting to a trade partner (verified via NID or TradeBoxStatusPointer),
    /// the game may impose a soft ban if we attempt to trade again without clearing the previous connection state.
    ///
    /// This method connects to a random partner (no code) and immediately disconnects using B+A to signal
    /// to the game servers that the previous trade session has ended, preventing the soft ban.
    /// </summary>
    private async Task PreventTradeSoftBan(CancellationToken token)
    {
        await Task.Delay(5_000, token).ConfigureAwait(false);

        if (!await CheckIfOnOverworld(token).ConfigureAwait(false))
        {
            Log("Not on overworld after restart, attempting recovery...");
            await RecoverToOverworld(token).ConfigureAwait(false);
        }

        Log("Connecting online to prevent trade soft ban...");
        await Click(X, 3_000, token).ConfigureAwait(false);
        await Click(DUP, 1_000, token).ConfigureAwait(false);
        await Click(A, 2_000, token).ConfigureAwait(false);
        await Click(DRIGHT, 1_000, token).ConfigureAwait(false);
        await Click(DRIGHT, 1_000, token).ConfigureAwait(false);
        await Click(A, 1_000, token).ConfigureAwait(false);
        await Click(DRIGHT, 1_000, token).ConfigureAwait(false);
        await Click(A, 1_000, token).ConfigureAwait(false);

        int attempts = 0;
        while (!await CheckIfConnectedOnline(token).ConfigureAwait(false))
        {
            await Task.Delay(1_000, token).ConfigureAwait(false);
            if (++attempts > 30)
            {
                Log("Failed to connect online during soft ban prevention.");
                await RecoverToOverworld(token).ConfigureAwait(false);
                return;
            }
        }
        await Task.Delay(8_000 + Hub.Config.Timings.ExtraTimeConnectOnline, token).ConfigureAwait(false);
        Log("Connected online for soft ban prevention.");

        await Click(A, 1_000, token).ConfigureAwait(false);
        await Click(A, 1_000, token).ConfigureAwait(false);
        await Task.Delay(3_000, token).ConfigureAwait(false);

        Log("Connecting with random partner to clear previous trade session...");
        await Click(PLUS, 2_000, token).ConfigureAwait(false);

        Log("Waiting for random partner to connect...");
        await Task.Delay(3_000, token).ConfigureAwait(false);

        int waitAttempts = 0;
        bool connected = false;
        while (waitAttempts < 30 && !connected)
        {
            var nid = await GetTradePartnerNID(token).ConfigureAwait(false);
            if (nid != 0)
            {
                Log("Random partner connected via NID. Disconnecting to complete soft ban prevention...");
                connected = true;
                break;
            }

            if (await CheckIfInTradeBox(token).ConfigureAwait(false))
            {
                Log("Random partner connected via TradeBox. Disconnecting to complete soft ban prevention...");
                connected = true;
                break;
            }

            await Task.Delay(1_000, token).ConfigureAwait(false);
            waitAttempts++;
        }

        if (!connected)
        {
            Log("No random partner found within 30s timeout. Soft ban may not be fully prevented. Continuing...");
            await RecoverToOverworld(token).ConfigureAwait(false);
            return;
        }

        Log("Disconnecting from random partner (B to cancel, A to confirm)...");
        await Click(B, 1_000, token).ConfigureAwait(false);
        await Click(A, 1_000, token).ConfigureAwait(false);

        Log("Waiting for partner disconnect confirmation...");
        int disconnectAttempts = 0;
        bool partnerDisconnected = false;
        while (disconnectAttempts < 10 && !partnerDisconnected)
        {
            await Task.Delay(500, token).ConfigureAwait(false);
            var currentNid = await GetTradePartnerNID(token).ConfigureAwait(false);
            if (currentNid == 0)
            {
                Log("Partner disconnected (NID = 0). Exiting to overworld...");
                partnerDisconnected = true;
                break;
            }
            disconnectAttempts++;
        }

        if (!partnerDisconnected)
        {
            Log("Partner did not disconnect within timeout. Forcing exit...");
        }

        Log("Spamming B to return to overworld...");
        for (int i = 0; i < 15; i++)
        {
            await Click(B, 1_000, token).ConfigureAwait(false);

            if (await CheckIfOnOverworld(token).ConfigureAwait(false))
            {
                Log("Soft ban prevention complete. Successfully returned to overworld.");
                StartFromOverworld = true;
                return;
            }
        }

        Log("Failed to return to overworld after B spam. Performing full recovery...");
        await RecoverToOverworld(token).ConfigureAwait(false);
        StartFromOverworld = true;
    }

    #endregion

    #region Multi-Bot Synchronization

    /// <summary>
    /// Checks if the barrier needs to get updated to consider this bot.
    /// If it should be considered, it adds it to the barrier if it is not already added.
    /// If it should not be considered, it removes it from the barrier if not already removed.
    /// </summary>
    private void UpdateBarrier(bool shouldWait)
    {
        if (ShouldWaitAtBarrier == shouldWait)
            return; // no change required

        ShouldWaitAtBarrier = shouldWait;
        if (shouldWait)
        {
            Hub.BotSync.Barrier.AddParticipant();
            Log($"Joined the Barrier. Count: {Hub.BotSync.Barrier.ParticipantCount}");
        }
        else
        {
            Hub.BotSync.Barrier.RemoveParticipant();
            Log($"Left the Barrier. Count: {Hub.BotSync.Barrier.ParticipantCount}");
        }
    }

    private void UpdateCountsAndExport(PokeTradeDetail<PA9> poke, PA9 received, PA9 toSend)
    {
        var counts = TradeSettings;
        if (poke.Type == PokeTradeType.Random)
            counts.CountStatsSettings.AddCompletedDistribution();
        else if (poke.Type == PokeTradeType.Clone)
            counts.CountStatsSettings.AddCompletedClones();
        else if (poke.Type == PokeTradeType.FixOT)
            counts.CountStatsSettings.AddCompletedFixOTs();
        else
            counts.CountStatsSettings.AddCompletedTrade();

        if (DumpSetting.Dump && !string.IsNullOrEmpty(DumpSetting.DumpFolder))
        {
            var subfolder = poke.Type.ToString().ToLower();
            var service = poke.Notifier.GetType().ToString().ToLower();
            var tradedFolder = service.Contains("twitch") ? Path.Combine("traded", "twitch") : service.Contains("discord") ? Path.Combine("traded", "discord") : "traded";
            DumpPokemon(DumpSetting.DumpFolder, subfolder, received); // received by bot
            if (poke.Type is PokeTradeType.Specific or PokeTradeType.Clone)
                DumpPokemon(DumpSetting.DumpFolder, tradedFolder, toSend); // sent to partner
        }
    }

    #region Clone & Dump Features

    private async Task<bool> CheckCloneChangedOffer(CancellationToken token)
    {
        // Watch their status to indicate they canceled, then offered a new Pokémon.
        var hovering = await ReadUntilChanged(TradePartnerStatusOffset, [0x2], 25_000, 1_000, true, true, token).ConfigureAwait(false);
        if (!hovering)
        {
            Log("Trade partner did not change their initial offer.");
            return false;
        }
        var offering = await ReadUntilChanged(TradePartnerStatusOffset, [0x3], 25_000, 1_000, true, true, token).ConfigureAwait(false);
        if (!offering)
        {
            return false;
        }
        return true;
    }

    private async Task<(PokeTradeResult Result, PA9? ClonedPokemon)> ProcessCloneTradeAsync(PokeTradeDetail<PA9> poke, SAV9ZA sav, PA9 offered, CancellationToken token)
    {
        if (Hub.Config.Discord.ReturnPKMs)
            poke.SendNotification(this, offered, "Here's what you showed me!");

        var la = new LegalityAnalysis(offered);
        if (!la.Valid)
        {
            Log($"Clone request (from {poke.Trainer.TrainerName}) has detected an invalid Pokémon: {GameInfo.GetStrings("en").Species[offered.Species]}.");
            if (DumpSetting.Dump)
                DumpPokemon(DumpSetting.DumpFolder, "hacked", offered);

            var report = la.Report();
            Log(report);
            poke.SendNotification(this, "This Pokémon is not legal per PKHeX's legality checks. I am forbidden from cloning this. Exiting trade.");
            poke.SendNotification(this, report);

            return (PokeTradeResult.IllegalTrade, null);
        }

        var clone = offered.Clone();
        if (Hub.Config.Legality.ResetHOMETracker)
            clone.Tracker = 0;

        poke.SendNotification(this, $"**Cloned your {GameInfo.GetStrings("en").Species[clone.Species]}!**\nNow press B to cancel your offer and trade me a Pokémon you don't want.");
        Log($"Cloned a {(Species)clone.Species}. Waiting for user to change their Pokémon...");

        if (!await CheckCloneChangedOffer(token).ConfigureAwait(false))
        {
            // They get one more chance.
            poke.SendNotification(this, "**HEY CHANGE IT NOW OR I AM LEAVING!!!**");
            if (!await CheckCloneChangedOffer(token).ConfigureAwait(false))
            {
                Log("Trade partner did not change their Pokémon.");
                return (PokeTradeResult.TrainerTooSlow, null);
            }
        }

        // If we got to here, we can read their offered Pokémon.
        var pk2 = await ReadUntilPresentPointer(Offsets.LinkTradePartnerPokemonPointer, 5_000, 1_000, BoxFormatSlotSize, token).ConfigureAwait(false);
        if (pk2 is null || SearchUtil.HashByDetails(pk2) == SearchUtil.HashByDetails(offered))
        {
            Log("Trade partner did not change their Pokémon.");
            return (PokeTradeResult.TrainerTooSlow, null);
        }

        var boxOffset = await GetBoxStartOffset(token).ConfigureAwait(false);
        await SetBoxPokemonAbsolute(boxOffset, clone, token, sav).ConfigureAwait(false);

        return (PokeTradeResult.Success, clone);
    }

    private async Task<PokeTradeResult> ProcessDumpTradeAsync(PokeTradeDetail<PA9> detail, CancellationToken token)
    {
        int ctr = 0;
        var maxDumps = Hub.Config.Trade.TradeConfiguration.MaxDumpsPerTrade;
        var time = TimeSpan.FromSeconds(Hub.Config.Trade.TradeConfiguration.MaxDumpTradeTime);
        var start = DateTime.Now;

        // Tell the user what to do
        detail.SendNotification(this, $"Now showing your Pokémon! You can show me up to {maxDumps} Pokémon. Keep changing Pokémon to dump more!");

        var pkprev = new PA9();
        var warnedAboutTime = false;

        while (ctr < maxDumps && DateTime.Now - start < time)
        {
            // Warn user when they're running low on time
            var elapsed = DateTime.Now - start;
            if (!warnedAboutTime && elapsed.TotalSeconds > time.TotalSeconds - 15)
            {
                detail.SendNotification(this, "Only 15 seconds remaining! Show your last Pokémon or press B to exit.");
                warnedAboutTime = true;
            }

            // Check if we're still in the trade box
            var (valid, offset) = await ValidatePointerAll(Offsets.TradeBoxStatusPointer, token).ConfigureAwait(false);
            if (!valid || !await IsInTradeBox(offset, token).ConfigureAwait(false))
            {
                Log("User exited trade box.");
                break;
            }

            // Check if partner is actually offering (0x3) vs just hovering (0x2)
            var status = await SwitchConnection.ReadBytesAbsoluteAsync(TradePartnerStatusOffset, 1, token).ConfigureAwait(false);
            if (status[0] != 0x3)
            {
                await Task.Delay(0_050, token).ConfigureAwait(false);
                continue;
            }

            // Wait for the user to show us a Pokemon - needs to be different from the previous one
            var pk = await ReadUntilPresentPointer(Offsets.LinkTradePartnerPokemonPointer, 3_000, 0_050, BoxFormatSlotSize, token).ConfigureAwait(false);
            if (pk == null || pk.Species == 0 || !pk.ChecksumValid)
            {
                await Task.Delay(0_050, token).ConfigureAwait(false);
                continue;
            }

            // Check if this is the same Pokemon as before
            if (SearchUtil.HashByDetails(pk) == SearchUtil.HashByDetails(pkprev))
            {
                Log($"User is showing the same Pokémon as before. Waiting for a different one...");
                await Task.Delay(0_500, token).ConfigureAwait(false);
                continue;
            }

            // Heal and refresh checksum to ensure valid data
            pk.Heal();
            pk.RefreshChecksum();

            // Save the new Pokemon for comparison next round
            pkprev = pk;

            // Dump the Pokemon to file if dumping is enabled
            if (DumpSetting.Dump)
            {
                var subfolder = detail.Type.ToString().ToLower();
                DumpPokemon(DumpSetting.DumpFolder, subfolder, pk);
            }

            var la = new LegalityAnalysis(pk);
            var verbose = $"```{la.Report(true)}```";
            Log($"Shown Pokémon is: {(la.Valid ? "Valid" : "Invalid")}.");

            ctr++;
            var msg = Hub.Config.Trade.TradeConfiguration.DumpTradeLegalityCheck ? verbose : $"File {ctr}";

            // Include trainer data for people requesting with their own trainer data
            var ot = pk.OriginalTrainerName;
            var ot_gender = pk.OriginalTrainerGender == 0 ? "Male" : "Female";
            var tid = pk.GetDisplayTID().ToString(pk.GetTrainerIDFormat().GetTrainerIDFormatStringTID());
            var sid = pk.GetDisplaySID().ToString(pk.GetTrainerIDFormat().GetTrainerIDFormatStringSID());
            msg += $"\n**Trainer Data**\n```OT: {ot}\nOTGender: {ot_gender}\nTID: {tid}\nSID: {sid}```";

            // Extra information for shiny eggs
            var eggstring = pk.IsEgg ? "Egg " : string.Empty;
            msg += pk.IsShiny ? $"\n**This Pokémon {eggstring}is shiny!**" : string.Empty;

            // Send the Pokemon file back to the user via Discord
            detail.SendNotification(this, pk, msg);

            // Tell user their progress
            var remaining = maxDumps - ctr;
            if (remaining > 0)
                detail.SendNotification(this, $"Received! You can show me {remaining} more. Show a different Pokémon to continue, or press B to exit.");
            else
                detail.SendNotification(this, "That's the maximum! Press B to exit the trade.");
        }

        var timeElapsed = DateTime.Now - start;
        Log($"Ended Dump loop after processing {ctr} Pokémon in {timeElapsed.TotalSeconds:F1} seconds.");

        if (ctr == 0)
            return PokeTradeResult.TrainerTooSlow;

        TradeSettings.CountStatsSettings.AddCompletedDumps();
        detail.Notifier.SendNotification(this, detail, $"Dumped {ctr} Pokémon. Press B to exit!");
        detail.Notifier.TradeFinished(this, detail, detail.TradeData); // blank PA9
        return PokeTradeResult.Success;
    }

    #endregion

    private void WaitAtBarrierIfApplicable(CancellationToken token)
    {
        if (!ShouldWaitAtBarrier)
            return;
        var opt = Hub.Config.Distribution.SynchronizeBots;
        if (opt == BotSyncOption.NoSync)
            return;

        var timeoutAfter = Hub.Config.Distribution.SynchronizeTimeout;
        if (FailedBarrier == 1) // failed last iteration
            timeoutAfter *= 2; // try to re-sync in the event things are too slow.

        var result = Hub.BotSync.Barrier.SignalAndWait(TimeSpan.FromSeconds(timeoutAfter), token);

        if (result)
        {
            FailedBarrier = 0;
            return;
        }

        FailedBarrier++;
        Log($"Barrier sync timed out after {timeoutAfter} seconds. Continuing.");
    }

    private Task WaitForQueueStep(int waitCounter, CancellationToken token)
    {
        if (waitCounter == 0)
        {
            // Updates the assets.
            Hub.Config.Stream.IdleAssets(this);
            Log("Waiting for trade requests...");
        }

        return Task.Delay(1_000, token);
    }

    #endregion
}
