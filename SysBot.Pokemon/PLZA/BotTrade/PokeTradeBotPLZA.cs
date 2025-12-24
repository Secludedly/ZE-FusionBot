using PKHeX.Core;
using PKHeX.Core.Searching;
using SysBot.Base;
using SysBot.Base.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public event Action<int>? TradeProgressChanged;
    private uint DisplaySID;
    private uint DisplayTID;

    private string OT = string.Empty;
    private bool StartFromOverworld = true;
    private ulong? _cachedBoxOffset;
    private ulong TradePartnerStatusOffset;
    private bool _wasConnectedToPartner = false;
    private int _consecutiveConnectionFailures = 0; // Track consecutive online connection failures for soft ban detection

    public event EventHandler<Exception>? ConnectionError;

    public event EventHandler? ConnectionSuccess;

    // Progress bar states
    private enum TradeState
    {
        Idle,               // No trade active
        Starting,           // Command received
        EnteringCode,       // Link code input
        WaitingForPartner,  // Searching
        PartnerFound,       // Partner detected
        Confirming,         // Confirming trade
        Trading,            // Trade animation running
        Completed,          // Trade done successfully
        Failed              // Trade aborted / error
    }

    private TradeState _tradeState = TradeState.Idle;
    private int _lastProgress = -1;

    private void SetTradeState(TradeState newState)
    {
        if (_tradeState == newState)
            return;

        _tradeState = newState;

        int progress = newState switch
        {
            TradeState.Idle => 0,
            TradeState.Starting => 5,
            TradeState.EnteringCode => 15,
            TradeState.WaitingForPartner => 30,
            TradeState.PartnerFound => 45,
            TradeState.Confirming => 65,
            TradeState.Trading => 85,
            TradeState.Completed => 100,
            TradeState.Failed => 0,
            _ => _lastProgress
        };

        // never regress unless explicitly resetting to Idle
        if (progress < _lastProgress && newState != TradeState.Idle)
            return;

        _lastProgress = progress;
        TradeProgressChanged?.Invoke(progress);
    }

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
            _consecutiveConnectionFailures = 0;

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
        Log("Waiting to connect to user before initializing trade process...");
        SetTradeState(TradeState.WaitingForPartner);

        // Initial delay to let the game populate NID pointer in memory
        await Task.Delay(2_000, token).ConfigureAwait(false);

        int maxWaitMs = Hub.Config.Trade.TradeConfiguration.TradeWaitTime * 1_000;
        int elapsed = 2_000; // Already waited 3 seconds above

        while (elapsed < maxWaitMs)
        {
            // Check if we've entered the trade box - this confirms a partner is connected
            if (!await IsOnMenu(MenuState.InBox, token).ConfigureAwait(false))
            {
                // Check if we got kicked back to overworld/menu
                var menuState = await GetMenuState(token).ConfigureAwait(false);
                if (menuState == MenuState.Overworld || menuState == MenuState.XMenu)
                {
                    Log("Connection interrupted. Restarting...");
                    return TradePartnerWaitResult.KickedToMenu;
                }

                await Task.Delay(100, token).ConfigureAwait(false);
                elapsed += 100;
                continue;
            }

            // We're in the box - wait a moment then validate the status pointer
            await Task.Delay(500, token).ConfigureAwait(false);
            elapsed += 500;


            // Set the offset for trade partner status monitoring
            var (valid, statusOffset) = await ValidatePointerAll(Offsets.TradePartnerStatusPointer, token).ConfigureAwait(false);
            if (!valid)
                continue; // Keep trying until pointer is valid

            Log("Trade partner detected!");
            SetTradeState(TradeState.PartnerFound);
            _wasConnectedToPartner = true;
            TradePartnerStatusOffset = statusOffset;
            return TradePartnerWaitResult.Success;
        }

        Log("Timed out waiting for trade partner.");
        SetTradeState(TradeState.Failed);
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
        var boxOffset = await GetBoxStartOffset(token).ConfigureAwait(false);
        var oldEC = await SwitchConnection.ReadBytesAbsoluteAsync(boxOffset, 8, token).ConfigureAwait(false);

        await Click(A, 3_000, token).ConfigureAwait(false);

        bool warningSent = false;
        int maxTime = Hub.Config.Trade.TradeConfiguration.MaxTradeConfirmTime;

        for (int i = 0; i < maxTime; i++)
        {
            // Check if we're still in trade box (partner disconnected if not in InBox menu state)
            if (!await IsOnMenu(MenuState.InBox, token).ConfigureAwait(false))
            {
                Log("No longer in trade box - partner declined and exited during offering stage.");
                SetTradeState(TradeState.Failed);
                detail.SendNotification(this, "Trade partner declined or disconnected.");
                return PokeTradeResult.NoTrainerFound;
            }

            await Click(A, 1_000, token).ConfigureAwait(false);

            // Send warning 10 seconds before timeout
            if (!warningSent && i == maxTime - 10 && maxTime >= 10)
            {
                detail.SendNotification(this, "Hey! Pick a Pokemon to trade or I am leaving!");
                warningSent = true;
            }

            var newEC = await SwitchConnection.ReadBytesAbsoluteAsync(boxOffset, 8, token).ConfigureAwait(false);
            if (!newEC.SequenceEqual(oldEC))
            {
                Log("Trade started!");
                SetTradeState(TradeState.Trading);
                return PokeTradeResult.Success;
            }
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
            _consecutiveConnectionFailures = 0;
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
                    _consecutiveConnectionFailures++;
                    Log($"Failed to connect online. Consecutive failures: {_consecutiveConnectionFailures}");

                    if (_consecutiveConnectionFailures >= 3)
                    {
                        Log("Soft ban detected (3 consecutive connection failures). Waiting 30 minutes...");
                        await Task.Delay(30 * 60 * 1000, token).ConfigureAwait(false);
                        Log("30 minute wait complete. Resuming operations.");
                        _consecutiveConnectionFailures = 0;
                    }

                    return false;
                }
            }
            await Task.Delay(8_000 + Hub.Config.Timings.ExtraTimeConnectOnline, token).ConfigureAwait(false);
            Log("Connected online.");
            _consecutiveConnectionFailures = 0;

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
        Log("Waiting for a user to begin trading...");
        SetTradeState(TradeState.Idle);
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
            Log($"Entering X-Menu and selecting Link Trade...");
            SetTradeState(TradeState.Idle);
            SetTradeState(TradeState.Starting);
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
        SetTradeState(TradeState.Failed);

        // Check if we're still in the trade box (connected) or kicked to menu
        var menuState = await GetMenuState(token).ConfigureAwait(false);

        if (menuState == MenuState.InBox)
        {
            // Still in trade box - press B+A to disconnect
            await Click(B, 0_500, token).ConfigureAwait(false);
            await Click(A, 1_000, token).ConfigureAwait(false);
        }
        else
        {
            // Already kicked to menu - only press B to navigate back
            await Click(B, 0_500, token).ConfigureAwait(false);
        }
    }

    private async Task ExitTradeToOverworld(bool unexpected, CancellationToken token)
    {
        if (unexpected)
            Log("Unexpected behavior, recovering to overworld.");
        SetTradeState(TradeState.Failed);

        if (await CheckIfOnOverworld(token).ConfigureAwait(false))
        {
            StartFromOverworld = true;
            _wasConnectedToPartner = false; // Reset flag when successfully back to overworld
            return;
        }

        // Use MenuState to determine whether to disconnect or navigate back
        int timeoutSeconds = 30;
        int elapsedExit = 0;

        // If we're in the Box or searching for a Link Trade, we need to use the BAB approach, otherwise we can just mash B.
        var remainMs = 120_000;
        while (await GetMenuState(token).ConfigureAwait(false) >= MenuState.LinkTrade)
        {
            if (remainMs < 0)
            {
                StartFromOverworld = true;
                _wasConnectedToPartner = false; // Reset flag when successfully back to overworld
                return;
            }

            await Click(B, 1_000, token).ConfigureAwait(false);
            if (await GetMenuState(token).ConfigureAwait(false) < MenuState.LinkTrade)
                break;

            var box = await IsOnMenu(MenuState.InBox, token).ConfigureAwait(false);
            await Click(box ? A : B, 1_000, token).ConfigureAwait(false);
            if (await GetMenuState(token).ConfigureAwait(false) < MenuState.LinkTrade)
                break;

            await Click(B, 1_000, token).ConfigureAwait(false);
            if (await GetMenuState(token).ConfigureAwait(false) < MenuState.LinkTrade)
                break;
            remainMs -= 3_000;
        }

        // From here, we should be able to press B to get to overworld.
        while (!await CheckIfOnOverworld(token).ConfigureAwait(false))
            await Click(B, 0_200, token).ConfigureAwait(false);

        Log("Returned to overworld.");
        SetTradeState(TradeState.Failed);
        StartFromOverworld = true;
        _wasConnectedToPartner = false;
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
        return await IsOnMenu(MenuState.Overworld, token).ConfigureAwait(false);
    }

    private async Task<bool> CheckIfConnectedOnline(CancellationToken token)
    {
        // Use the direct main memory offset for faster and more reliable connection checks
        return await IsConnected(token).ConfigureAwait(false);
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

    private async Task<PokeTradeResult> PerformBatchTrade(
    SAV9ZA sav,
    PokeTradeDetail<PA9> poke,
    CancellationToken token)
    {
        int completedTrades = 0;
        var startingDetail = poke;
        var originalTrainerID = startingDetail.Trainer.ID;

        var tradesToProcess = poke.BatchTrades ?? new List<PA9> { poke.TradeData };
        int totalBatchTrades = tradesToProcess.Count;

        TradePartnerStatusPLZA? cachedTradePartnerInfo = null;

        void CleanupBatch(bool sendBackPokemon)
        {
            var allReceived = BatchTracker.GetReceivedPokemon(originalTrainerID);

            if (sendBackPokemon && allReceived.Count > 0)
            {
                poke.SendNotification(this,
                    $"Sending you the {allReceived.Count} Pokémon you traded to me.");

                foreach (var mon in allReceived)
                {
                    var name = SpeciesName.GetSpeciesName(mon.Species, 2);
                    poke.SendNotification(this, mon, $"Pokémon you traded to me: {name}");
                    Thread.Sleep(500);
                }
            }

            BatchTracker.ClearReceivedPokemon(originalTrainerID);
            BatchTracker.ReleaseBatch(originalTrainerID, startingDetail.UniqueTradeID);

            poke.IsProcessing = false;
            Hub.Queues.Info.Remove(
                new TradeEntry<PA9>(
                    poke,
                    originalTrainerID,
                    PokeRoutineType.Batch,
                    poke.Trainer.TrainerName,
                    poke.UniqueTradeID));
        }

        try
        {
            var retryCounts = new Dictionary<int, int>();
            for (int i = 0; i < totalBatchTrades; i++)
            {
                poke.TradeData = tradesToProcess[i];
                poke.Notifier.UpdateBatchProgress(i + 1, poke.TradeData, poke.UniqueTradeID);

                if (i > 0)
                {
                    poke.SendNotification(this,
                        $"**Ready!** Offer Pokémon {i + 1}/{totalBatchTrades}.");
                    await Task.Delay(2_000, token).ConfigureAwait(false);
                }

                // ================= FIRST TRADE ONLY =================
                if (i == 0)
                {
                    await Click(A, 500, token).ConfigureAwait(false);
                    await Click(A, 500, token).ConfigureAwait(false);

                    WaitAtBarrierIfApplicable(token);
                    await Click(A, 1_000, token).ConfigureAwait(false);

                    poke.TradeSearching(this);
                    var waitResult = await WaitForTradePartner(token).ConfigureAwait(false);

                    if (token.IsCancellationRequested)
                        return PokeTradeResult.RoutineCancel;

                    if (waitResult == TradePartnerWaitResult.Timeout)
                        return PokeTradeResult.NoTrainerFound;

                    if (waitResult == TradePartnerWaitResult.KickedToMenu)
                        return PokeTradeResult.RecoverStart;

                    Hub.Config.Stream.EndEnterCode(this);

                    int attempts = 0;
                    while (!await IsOnMenu(MenuState.InBox, token).ConfigureAwait(false))
                    {
                        if (++attempts > 30)
                            return PokeTradeResult.NoTrainerFound;

                        await Task.Delay(500, token).ConfigureAwait(false);
                    }

                    await Task.Delay(2_000, token).ConfigureAwait(false);

                    cachedTradePartnerInfo = await GetTradePartnerFullInfo(token).ConfigureAwait(false);
                    var partner = new TradePartnerPLZA(cachedTradePartnerInfo);
                    var trainerNID = await GetTradePartnerNID(token).ConfigureAwait(false);

                    Log($"[TradePartner] OT: {partner.TrainerName} | TID: {partner.TID7} | SID: {partner.SID7} | Gender: {TrainerDisplayHelper.GetGenderString(partner.Gender)} | Language: {TrainerDisplayHelper.GetLanguageString(partner.Language)} | NID: {trainerNID}");

                    var partnerCheck = CheckPartnerReputation(
                    this,
                    poke,
                    trainerNID,
                    partner.TrainerName,
                    AbuseSettings,
                    token);

                    if (partnerCheck != PokeTradeResult.Success)
                    {
                        poke.SendNotification(this, "Trade partner blocked. Canceling batch trades.");
                        SetTradeState(TradeState.Failed);
                        return partnerCheck;
                    }


                    poke.SendNotification(this,
                        $"Found trade partner: {partner.TrainerName}. " +
                        $"**TID**: {partner.TID7} **SID**: {partner.SID7}");

                    if (Hub.Config.Legality.UseTradePartnerInfo && !poke.IgnoreAutoOT)
                    {
                        tradesToProcess[0] = await ApplyAutoOT(
                            tradesToProcess[0],
                            cachedTradePartnerInfo,
                            sav,
                            token).ConfigureAwait(false);

                        poke.TradeData = tradesToProcess[0];
                        await Task.Delay(3_000, token).ConfigureAwait(false);
                    }
                }

                poke.SendNotification(this,
                    $"Please offer Pokémon {i + 1}/{totalBatchTrades}.");

                ulong boxOffset = await GetBoxStartOffset(token).ConfigureAwait(false);
                var beforeTrade = await ReadPokemon(
                    boxOffset,
                    BoxFormatSlotSize,
                    token).ConfigureAwait(false);

                var offered = await WaitForBatchOfferAsync(
                    i,
                    totalBatchTrades,
                    token).ConfigureAwait(false);

                if (offered == null)
                    return PokeTradeResult.TrainerTooSlow;

                if (Hub.Config.Trade.TradeConfiguration.DisallowTradeEvolve &&
                    TradeEvolutions.WillTradeEvolve(
                        offered.Species,
                        offered.Form,
                        offered.HeldItem,
                        poke.TradeData.Species))
                    return PokeTradeResult.TradeEvolveNotAllowed;

                SetTradeState(TradeState.Confirming);

                var tradeResult = await ConfirmAndStartTrading(
                    poke,
                    beforeTrade.Checksum,
                    token).ConfigureAwait(false);

                if (tradeResult == PokeTradeResult.TrainerTooSlow)
                {
                    if (!retryCounts.ContainsKey(i))
                        retryCounts[i] = 0;

                    retryCounts[i]++;

                    if (retryCounts[i] == 1)
                    {
                        Log($"Trade animation detected for trade {i + 1}/{totalBatchTrades}. Waiting before continuing...");
                    }
                    else
                    {
                        Log($"Trainer slow on entering trade {i + 1}/{totalBatchTrades}, retrying...");
                    }

                    await Task.Delay(2_000, token).ConfigureAwait(false);
                    i--; // retry same trade index
                    continue;
                }

                if (tradeResult != PokeTradeResult.Success)
                    return tradeResult;

                boxOffset = await GetBoxStartOffset(token).ConfigureAwait(false);
                var received = await ReadPokemon(
                    boxOffset,
                    BoxFormatSlotSize,
                    token).ConfigureAwait(false);

                if (received == null || received.Species == 0)
                    return PokeTradeResult.TrainerTooSlow;

                BatchTracker.AddReceivedPokemon(originalTrainerID, received);
                UpdateCountsAndExport(poke, received, poke.TradeData);

                completedTrades++;

                // Log animation wait message after each successful trade except the last
                if (i + 1 < totalBatchTrades)
                {
                    Log($"Waiting for trade animation to finish before continuing to trade {i + 2}...");
                }

                // Inject next Pokémon during animation
                if (i + 1 < totalBatchTrades)
                {
                    var next = tradesToProcess[i + 1];

                    if (Hub.Config.Legality.UseTradePartnerInfo &&
                        !poke.IgnoreAutoOT &&
                        cachedTradePartnerInfo != null)
                    {
                        next = await ApplyAutoOT(
                            next,
                            cachedTradePartnerInfo,
                            sav,
                            token).ConfigureAwait(false);

                        tradesToProcess[i + 1] = next;
                    }

                    await SetBoxPokemonAbsolute(
                        await GetBoxStartOffset(token).ConfigureAwait(false),
                        next,
                        token,
                        sav).ConfigureAwait(false);
                }
            }

            poke.SendNotification(this,
                "All batch trades completed! Thank you for trading!");

            var allReceived = BatchTracker.GetReceivedPokemon(originalTrainerID);
            if (allReceived.Count > 0)
                poke.TradeFinished(this, allReceived[^1]);

            Hub.Queues.CompleteTrade(this, startingDetail);
            return PokeTradeResult.Success;
        }
        finally
        {
            CleanupBatch(Hub.Config.Discord.ReturnPKMs);
            await ExitTradeToOverworld(false, token).ConfigureAwait(false);
        }
    }

    private async Task<PA9?> WaitForBatchOfferAsync(
        int tradeIndex,
        int totalTrades,
        CancellationToken token)
    {
        var start = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(45);

        while (!token.IsCancellationRequested)
        {
            var mon = await ReadUntilPresentPointer(
                Offsets.LinkTradePartnerPokemonPointer,
                1_000,
                300,
                BoxFormatSlotSize,
                token).ConfigureAwait(false);

            if (mon?.Species > 0 && mon.ChecksumValid)
                return mon;

            if (!await IsOnMenu(MenuState.InBox, token).ConfigureAwait(false))
                return null;

            if (DateTime.UtcNow - start > timeout)
            {
                Log($"Trade {tradeIndex + 1}/{totalTrades} timed out.");
                return null;
            }

            await Task.Delay(250, token).ConfigureAwait(false);
        }

        return null;
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
            SetTradeState(TradeState.Failed);
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
            SetTradeState(TradeState.Failed);
            await RestartGamePLZA(token).ConfigureAwait(false);
            return PokeTradeResult.RecoverStart;
        }

        // Inject Pokemon AFTER code verification succeeds and BEFORE searching
        var toSend = poke.TradeData;
        if (toSend.Species != 0)
        {
            Log("Injected requested Pokémon into B1S1. ");
            SetTradeState(TradeState.EnteringCode);
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
                SetTradeState(TradeState.Failed);
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
                SetTradeState(TradeState.Failed);
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

        // PLZA saves the previous Link Code after the first trade.
        // If the pointer isn't valid, we haven't traded yet.
        var (valid, _) = await ValidatePointerAll(Offsets.LinkTradeCodePointer, token).ConfigureAwait(false);
        if (!valid)
        {
            // No previous trade, freely enter our code
            if (code != 0)
            {
                Log($"Entering Link Trade code: {code:0000 0000}...");
                SetTradeState(TradeState.EnteringCode);
                await EnterLinkCode(code, Hub.Config, token).ConfigureAwait(false);
            }
        }
        else
        {
            var prevCode = await GetStoredLinkTradeCode(token).ConfigureAwait(false);
            if (prevCode != code)
            {
                // Only clear if the new code is different
                var codeLength = await GetStoredLinkTradeCodeLength(token).ConfigureAwait(false);
                if (codeLength > 0)
                {
                    for (int i = 0; i < codeLength; i++)
                        await Click(B, 0, token).ConfigureAwait(false);
                    await Task.Delay(0_500, token).ConfigureAwait(false);
                }

                if (code != 0)
                {
                    Log($"Entering Link Trade code: {code:0000 0000}...");
                    SetTradeState(TradeState.EnteringCode);
                    await EnterLinkCode(code, Hub.Config, token).ConfigureAwait(false);
                }
            }
            else
            {
                Log($"Using previous Link Trade code: {code:0000 0000}.");
                SetTradeState(TradeState.EnteringCode);
            }
        }

        await Click(PLUS, 2_000, token).ConfigureAwait(false);

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
            SetTradeState(TradeState.Failed);
            await RecoverToOverworld(token).ConfigureAwait(false);
            return PokeTradeResult.RecoverStart;
        }

        Hub.Config.Stream.EndEnterCode(this);

        // Wait until we're in the trade box
        Log("Selecting Pokémon in B1S1...");
        SetTradeState(TradeState.EnteringCode);
        int boxCheckAttempts = 0;
        while (!await IsOnMenu(MenuState.InBox, token).ConfigureAwait(false))
        {
            await Task.Delay(500, token).ConfigureAwait(false);
            if (++boxCheckAttempts > 30) // 15 seconds max
            {
                Log("No trade partner found.");
                SetTradeState(TradeState.Failed);
                return PokeTradeResult.NoTrainerFound;
            }
        }

        // Wait for trade UI and partner data to load
        await Task.Delay(5_000, token).ConfigureAwait(false);

        // Now that data has loaded, read partner info
        var tradePartnerFullInfo = await GetTradePartnerFullInfo(token).ConfigureAwait(false);
        var tradePartner = new TradePartnerPLZA(tradePartnerFullInfo);

        var trainerNID = await GetTradePartnerNID(token).ConfigureAwait(false);

        Log($"[TradePartner] OT: {tradePartner.TrainerName} | TID: {tradePartner.TID7} | SID: {tradePartner.SID7} | Gender: {TrainerDisplayHelper.GetGenderString(tradePartner.Gender)} | Language: {TrainerDisplayHelper.GetLanguageString(tradePartner.Language)} | NID: {trainerNID}");


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

        // Read the partner's offered Pokemon BEFORE we start pressing A to confirm
        // This way we can cancel with B+A if they're offering something that will evolve
        if (offered == null) // Only read if we haven't already (Clone/Dump read it earlier)
        {
            offered = await ReadUntilPresentPointer(Offsets.LinkTradePartnerPokemonPointer, 3_000, 0_500, BoxFormatSlotSize, token).ConfigureAwait(false);
        }

        if (offered == null || offered.Species == 0 || !offered.ChecksumValid)
        {
            Log("Trade ended because trainer offer was rescinded too quickly.");
            SetTradeState(TradeState.Failed);
            poke.SendNotification(this, "Trade partner didn't offer a valid Pokémon.");
            await DisconnectFromTrade(token).ConfigureAwait(false);
            await ExitTradeToOverworld(false, token).ConfigureAwait(false);
            return PokeTradeResult.TrainerOfferCanceledQuick;
        }

        // Check if the offered Pokemon will evolve upon trade BEFORE confirming
        if (Hub.Config.Trade.TradeConfiguration.DisallowTradeEvolve && TradeEvolutions.WillTradeEvolve(offered.Species, offered.Form, offered.HeldItem, toSend.Species))
        {
            Log("Trade cancelled because trainer offered a Pokémon that would evolve upon trade.");
            SetTradeState(TradeState.Failed);
            poke.SendNotification(this, "Trade cancelled. You cannot trade a Pokémon that will evolve. To prevent this, either give your Pokémon an Everstone to hold, or trade a different Pokémon.");
            await DisconnectFromTrade(token).ConfigureAwait(false);
            await ExitTradeToOverworld(false, token).ConfigureAwait(false);
            return PokeTradeResult.TradeEvolveNotAllowed;
        }

        Log("Selecting \"Trade it.\" Now waiting for trade animation to begin...");
        SetTradeState(TradeState.Confirming);
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

        var offset2 = await GetBoxStartOffset(token).ConfigureAwait(false);
        var received = await ReadPokemon(offset2, BoxFormatSlotSize, token).ConfigureAwait(false);
        var checksumAfterTrade = received.Checksum;

        if (checksumBeforeTrade == checksumAfterTrade)
        {
            Log("Trade was canceled.");
            SetTradeState(TradeState.Failed);
            poke.SendNotification(this, "Trade was canceled. Please try again.");
            await DisconnectFromTrade(token).ConfigureAwait(false);
            await ExitTradeToOverworld(false, token).ConfigureAwait(false);
            return PokeTradeResult.TrainerTooSlow;
        }

        Log($"Trade complete! Received {(Species)received.Species}. Now waiting for trade animation to complete...");
        SetTradeState(TradeState.Completed);

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
        SetTradeState(TradeState.Failed);

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
            SetTradeState(TradeState.Failed);

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
    /// When the bot restarts AFTER successfully connecting to a trade partner (verified via MenuState.InBox),
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

            if (await IsOnMenu(MenuState.InBox, token).ConfigureAwait(false))
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
            SetTradeState(TradeState.Failed);
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
            SetTradeState(TradeState.Failed);
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
        SetTradeState(TradeState.Trading);


        if (!await CheckCloneChangedOffer(token).ConfigureAwait(false))
        {
            // They get one more chance.
            poke.SendNotification(this, "**HEY CHANGE IT NOW OR I AM LEAVING!!!**");
            if (!await CheckCloneChangedOffer(token).ConfigureAwait(false))
            {
                Log("Trade partner did not change their Pokémon.");
                SetTradeState(TradeState.Failed);
                return (PokeTradeResult.TrainerTooSlow, null);
            }
        }

        // If we got to here, we can read their offered Pokémon.
        var pk2 = await ReadUntilPresentPointer(Offsets.LinkTradePartnerPokemonPointer, 5_000, 1_000, BoxFormatSlotSize, token).ConfigureAwait(false);
        if (pk2 is null || SearchUtil.HashByDetails(pk2) == SearchUtil.HashByDetails(offered))
        {
            Log("Trade partner did not change their Pokémon.");
            SetTradeState(TradeState.Failed);
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
        var bctr = 0;

        while (ctr < maxDumps && DateTime.Now - start < time)
        {
            // Check if we're still in the trade box (user disconnected if not)
            if (!await IsOnMenu(MenuState.InBox, token).ConfigureAwait(false))
            {
                Log("Trade partner disconnected (not in trade box).");
                SetTradeState(TradeState.Failed);
                break;
            }

            // Periodic B button press to keep connection alive
            if (bctr++ % 3 == 0)
                await Click(B, 0_100, token).ConfigureAwait(false);

            // Warn user when they're running low on time
            var elapsed = DateTime.Now - start;
            if (!warnedAboutTime && elapsed.TotalSeconds > time.TotalSeconds - 15)
            {
                detail.SendNotification(this, "Only 15 seconds remaining! Show your last Pokémon or press B to exit.");
                warnedAboutTime = true;
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
            SetTradeState(TradeState.Trading);
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
        detail.Notifier.SendNotification(this, detail, $"Dumped {ctr} Pokémon.");
        detail.Notifier.TradeFinished(this, detail, pkprev); // Send last dumped Pokemon
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
            Log("Nothing to check, waiting for new users...");
            SetTradeState(TradeState.Idle);
        }

        return Task.Delay(1_000, token);
    }

    #endregion
}
