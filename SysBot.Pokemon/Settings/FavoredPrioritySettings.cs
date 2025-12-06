using System;
using System.ComponentModel;

namespace SysBot.Pokemon;

/// <summary>
/// Settings for priority user favoritism in the trade queue.
/// Priority users can skip ahead of regular users while ensuring regular users still get processed.
/// </summary>
public class FavoredPrioritySettings : IFavoredCPQSetting
{
    private const int MinSkipPercentage = 0;
    private const int MaxSkipPercentage = 100;
    private const int MinRegularUsers = 0;

    private const string Configure = nameof(Configure);
    private const string Operation = nameof(Operation);

    private int _skipPercentage = 50;
    private int _minimumRegularUsersFirst = 3;

    [Category(Operation), Description("Enable or disable priority user favoritism. When disabled, all users are treated equally."), DisplayName("Enable Favoritism")]
    public bool EnableFavoritism { get; set; } = true;

    [Category(Configure), Description("Percentage of regular users that priority users can skip (0-100). For example: 50% means a priority user joins halfway through the regular users in queue. Higher percentage = more favorable to priority users."), DisplayName("Skip Percentage")]
    public int SkipPercentage
    {
        get => _skipPercentage;
        set => _skipPercentage = Math.Clamp(value, MinSkipPercentage, MaxSkipPercentage);
    }

    [Category(Configure), Description("Minimum number of regular users that must be processed before any priority user can skip ahead. This prevents priority users from completely blocking regular users, even in large queues."), DisplayName("Minimum User Amount Before Favoritism")]
    public int MinimumRegularUsersFirst
    {
        get => _minimumRegularUsersFirst;
        set => _minimumRegularUsersFirst = Math.Max(MinRegularUsers, value);
    }

    public override string ToString() => "Favoritism Settings";
}
