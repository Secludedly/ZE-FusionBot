using System.ComponentModel;

namespace SysBot.Pokemon;

public class TimingSettings
{
    private const string CloseGame = nameof(CloseGame);

    private const string Misc = nameof(Misc);

    private const string OpenGame = nameof(OpenGame);

    private const string Raid = nameof(Raid);

    [Category(Misc), Description("Enable this to decline incoming system updates.")]
    public bool AvoidSystemUpdate { get; set; }

    [Category(Misc), Description("Extra time in milliseconds to wait between attempts to reconnect. Base time is 30 seconds.")]
    public int ExtraReconnectDelay { get; set; }

    [Category(Raid), Description("[RaidBot] Extra time in milliseconds to wait after accepting a friend.")]
    public int ExtraTimeAddFriend { get; set; }

    [Category(CloseGame), Description("Extra time in milliseconds to wait after clicking to close the game.")]
    public int ExtraTimeCloseGame { get; set; }

    // Miscellaneous settings.
    [Category(Misc), Description("[SWSH/SV/PLZA] Extra time in milliseconds to wait after clicking + to connect to Y-Comm (SWSH), L to connect online (SV), or after connecting to Portal (PLZA). Base time for PLZA is 8 seconds.")]
    public int ExtraTimeConnectOnline { get; set; }

    [Category(Raid), Description("[RaidBot] Extra time in milliseconds to wait after deleting a friend.")]
    public int ExtraTimeDeleteFriend { get; set; }

    [Category(Raid), Description("[RaidBot] Extra time in milliseconds to wait before closing the game to reset the raid.")]
    public int ExtraTimeEndRaid { get; set; }

    [Category(Misc), Description("[BDSP] Extra time in milliseconds to wait for the Union Room to load before trying to call for a trade.")]
    public int ExtraTimeJoinUnionRoom { get; set; } = 500;

    [Category(Misc), Description("[BDSP] Extra time in milliseconds to wait for the overworld to load after leaving the Union Room.")]
    public int ExtraTimeLeaveUnionRoom { get; set; } = 1000;

    [Category(OpenGame), Description("Extra time in milliseconds to wait before clicking A in title screen.")]
    public int ExtraTimeLoadGame { get; set; } = 5000;

    [Category(OpenGame), Description("Extra time in milliseconds to wait for the overworld to load after the title screen.")]
    public int ExtraTimeLoadOverworld { get; set; } = 3000;

    [Category(Misc), Description("[SV] Extra time in milliseconds to wait for the Poké Portal to load.")]
    public int ExtraTimeLoadPortal { get; set; } = 1000;

    // Opening the game.
    [Category(OpenGame), Description("Enable this if you need to select a profile when starting the game.")]
    public bool ProfileSelectionRequired { get; set; } = true;

    [Category(OpenGame), Description("Extra time in milliseconds to wait for profiles to load when starting the game.")]
    public int ExtraTimeLoadProfile { get; set; }

    [Category(OpenGame), Description("Enable this to add a delay for \"Checking if Game Can be Played\" Pop-up.")]
    public bool CheckGameDelay { get; set; } = false;

    [Category(OpenGame), Description("Extra Time to wait for the \"Checking if Game Can Be Played\" Pop-up.")]
    public int ExtraTimeCheckGame { get; set; } = 200;

    // Raid-specific timings.
    [Category(Raid), Description("[RaidBot] Extra time in milliseconds to wait for the raid to load after clicking on the den.")]
    public int ExtraTimeLoadRaid { get; set; }

    [Category(Misc), Description("Extra time in milliseconds to wait for the box to load after finding a trade.")]
    public int ExtraTimeOpenBox { get; set; } = 1000;

    [Category(Misc), Description("Time to wait after opening the keyboard for code entry during trades.")]
    public int ExtraTimeOpenCodeEntry { get; set; } = 1000;

    [Category(Raid), Description("[RaidBot] Extra time in milliseconds to wait after clicking \"Invite Others\" before locking into a Pokémon.")]
    public int ExtraTimeOpenRaid { get; set; }

    [Category(Misc), Description("[BDSP] Extra time in milliseconds to wait for the Y Menu to load at the start of each trade loop.")]
    public int ExtraTimeOpenYMenu { get; set; } = 500;

    // Closing the game.
    [Category(CloseGame), Description("Extra time in milliseconds to wait after pressing HOME to minimize the game.")]
    public int ExtraTimeReturnHome { get; set; }

    [Category(Misc), Description("Time to wait after each keypress when navigating Switch menus or entering Link Code.")]
    public int KeypressTime { get; set; } = 200;

    [Category(Misc), Description("Number of times to attempt reconnecting to a socket connection after a connection is lost. Set this to -1 to try indefinitely.")]
    public int ReconnectAttempts { get; set; } = 30;
    public override string ToString() => "Extra Time Settings";
}
