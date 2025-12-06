using PKHeX.Core;
using SysBot.Base;
using System.ComponentModel;

namespace SysBot.Pokemon;

public class DistributionSettings : ISynchronizationSetting
{
    private const string Distribute = nameof(Distribute);

    private const string Synchronize = nameof(Synchronize);

    [Category(Distribute), Description("When enabled, idle LinkTrade bots will randomly distribute PKM files from the DistributeFolder."), DisplayName("Distribute While Idle")]
    public bool DistributeWhileIdle { get; set; } = true;

    [Category(Distribute), Description("When set to true, Random Ledy nickname-swap trades will quit rather than trade a random entity from the pool."), DisplayName("Ledy Quit on No Match")]
    public bool LedyQuitIfNoMatch { get; set; }

    [Category(Distribute), Description("When set to something other than None, the Random Trades will require this species in addition to the nickname match."), DisplayName("Ledy Species")]
    public Species LedySpecies { get; set; } = Species.None;

    [Category(Distribute), Description("Distribution Trade Link Code uses the Min and Max range rather than the fixed trade code."), DisplayName("Random Code")]
    public bool RandomCode { get; set; }

    [Category(Distribute), Description("For BDSP, the distribution bot will go to a specific room and remain there until the bot is stopped."), DisplayName("Remain in Union Room")]
    public bool RemainInUnionRoomBDSP { get; set; } = true;

    // Distribute
    [Category(Distribute), Description("When enabled, the DistributionFolder will yield randomly rather than in the same sequence."), DisplayName("Shuffle Distribution Folder")]
    public bool Shuffled { get; set; }

    [Category(Synchronize), Description("Link Trade: Using multiple distribution bots -- all bots will confirm their trade code at the same time. When Local, the bots will continue when all are at the barrier. When Remote, something else must signal the bots to continue."), DisplayName("Synchronize Bots")]
    public BotSyncOption SynchronizeBots { get; set; } = BotSyncOption.LocalSync;

    // Synchronize
    [Category(Synchronize), Description("Link Trade: Using multiple distribution bots -- once all bots are ready to confirm trade code, the Hub will wait X milliseconds before releasing all bots."), DisplayName("Synchronize Delay Barrier")]
    public int SynchronizeDelayBarrier { get; set; }

    [Category(Synchronize), Description("Link Trade: Using multiple distribution bots -- how long (seconds) a bot will wait for synchronization before continuing anyways."), DisplayName("Synchronize Timeout")]
    public double SynchronizeTimeout { get; set; } = 90;

    [Category(Distribute), Description("Distribution Trade Link Code."), DisplayName("Trade Code")]
    public int TradeCode { get; set; } = 7196;

    public override string ToString() => "Distribution Trade Settings";
}
