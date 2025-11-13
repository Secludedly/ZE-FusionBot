using PKHeX.Core;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.Pokemon;

public interface IPokeBotRunner
{
    PokeTradeHubConfig Config { get; }

    bool RunOnce { get; }

    bool IsRunning { get; }

    IList<BotSource<PokeBotState>> Bots { get; }

    void StartAll();

    void StopAll();

    void InitializeStart();

    void Add(PokeRoutineExecutorBase newbot);

    void Remove(IConsoleBotConfig state, bool callStop);

    BotSource<PokeBotState>? GetBot(PokeBotState state);

    PokeRoutineExecutorBase CreateBotFromConfig(PokeBotState cfg);

    bool SupportsRoutine(PokeRoutineType pokeRoutineType);

    event EventHandler BotStopped;
}

public abstract class PokeBotRunner<T> : RecoverableBotRunner<PokeBotState>, IPokeBotRunner where T : PKM, new()
{
    public readonly PokeTradeHub<T> Hub;

    private readonly BotFactory<T> Factory;

    public event EventHandler BotStopped;

    public PokeTradeHubConfig Config => Hub.Config;

    IList<BotSource<PokeBotState>> IPokeBotRunner.Bots => base.Bots;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

    protected PokeBotRunner(PokeTradeHub<T> hub, BotFactory<T> factory)
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    {
        Hub = hub;
        Factory = factory;
    }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

    protected PokeBotRunner(PokeTradeHubConfig config, BotFactory<T> factory)
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    {
        Factory = factory;
        Hub = new PokeTradeHub<T>(config);
    }

    protected virtual void AddIntegrations()
    { }

    public override void Add(RoutineExecutor<PokeBotState> bot)
    {
        base.Add(bot);
        if (bot is PokeRoutineExecutorBase b && b.Config.InitialRoutine.IsTradeBot())
            Hub.Bots.Add(b);
    }

    public override bool Remove(IConsoleBotConfig cfg, bool callStop)
    {
        var bot = GetBot(cfg)?.Bot;
        if (bot is PokeRoutineExecutorBase b && b.Config.InitialRoutine.IsTradeBot())
            Hub.Bots.Remove(b);
        return base.Remove(cfg, callStop);
    }

    public override void StartAll()
    {
        InitializeStart();

        if (!Hub.Config.SkipConsoleBotCreation)
            base.StartAll();
    }

    public override void InitializeStart()
    {
        if (RunOnce)
            return;

        AutoLegalityWrapper.EnsureInitialized(Hub.Config.Legality);

        // Initialize recovery service with settings from config
        InitializeRecoveryService();

        AddIntegrations();
        AddTradeBotMonitors();

        base.InitializeStart();
    }

    private void InitializeRecoveryService()
    {
        var recoveryConfig = new RecoveryConfiguration
        {
            EnableRecovery = Hub.Config.Recovery.EnableRecovery,
            MaxRecoveryAttempts = Hub.Config.Recovery.MaxRecoveryAttempts,
            InitialRecoveryDelaySeconds = Hub.Config.Recovery.InitialRecoveryDelaySeconds,
            MaxRecoveryDelaySeconds = Hub.Config.Recovery.MaxRecoveryDelaySeconds,
            BackoffMultiplier = Hub.Config.Recovery.BackoffMultiplier,
            CrashHistoryWindowMinutes = Hub.Config.Recovery.CrashHistoryWindowMinutes,
            MaxCrashesInWindow = Hub.Config.Recovery.MaxCrashesInWindow,
            RecoverIntentionalStops = Hub.Config.Recovery.RecoverIntentionalStops,
            MinimumStableUptimeSeconds = Hub.Config.Recovery.MinimumStableUptimeSeconds,
            NotifyOnRecoveryAttempt = Hub.Config.Recovery.NotifyOnRecoveryAttempt,
            NotifyOnRecoveryFailure = Hub.Config.Recovery.NotifyOnRecoveryFailure
        };

        InitializeRecovery(recoveryConfig);
        
        if (Hub.Config.Recovery.EnableRecovery)
        {
            LogUtil.LogInfo("Bot recovery system is enabled", "Recovery");
        }
    }

    /// <summary>
    /// Gets the recovery service for external integrations (like Discord).
    /// </summary>
    public new BotRecoveryService<PokeBotState>? GetRecoveryService() => RecoveryService;

    public override void StopAll()
    {
        // Raise the BotStopped event
        BotStopped?.Invoke(this, EventArgs.Empty);

        base.StopAll();

        // bots currently don't de-register
        Thread.Sleep(100);

        int count = Hub.BotSync.Barrier.ParticipantCount;
        if (count != 0)
            Hub.BotSync.Barrier.RemoveParticipants(count);
            
        // Dispose recovery service when stopping all bots
        DisposeRecovery();
    }

    public override void PauseAll()
    {
        if (!Hub.Config.SkipConsoleBotCreation)
            base.PauseAll();
    }

    public override void ResumeAll()
    {
        if (!Hub.Config.SkipConsoleBotCreation)
            base.ResumeAll();
    }

    private void AddTradeBotMonitors()
    {
        Task.Run(async () => await new QueueMonitor<T>(Hub).MonitorOpenQueue(CancellationToken.None).ConfigureAwait(false));

        var path = Hub.Config.Folder.DistributeFolder;
        if (!Directory.Exists(path))
            LogUtil.LogError("Hub", "The distribution folder was not found. Please verify that it exists!");

        var pool = Hub.Ledy.Pool;
        if (!pool.Reload(Hub.Config.Folder.DistributeFolder))
            LogUtil.LogError("Hub", "Nothing to distribute for Empty Trade Queues!");
    }

    public PokeRoutineExecutorBase CreateBotFromConfig(PokeBotState cfg) => Factory.CreateBot(Hub, cfg);

    public BotSource<PokeBotState>? GetBot(PokeBotState state) => base.GetBot(state);

    void IPokeBotRunner.Remove(IConsoleBotConfig state, bool callStop) => Remove(state, callStop);

    public void Add(PokeRoutineExecutorBase newbot) => Add((RoutineExecutor<PokeBotState>)newbot);

    public bool SupportsRoutine(PokeRoutineType t) => Factory.SupportsRoutine(t);
}
