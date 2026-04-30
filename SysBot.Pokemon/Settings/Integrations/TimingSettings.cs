using System.ComponentModel;

namespace SysBot.Pokemon;

public class TimingSettings
{
    private const string CloseGame = nameof(CloseGame);

    private const string Misc = nameof(Misc);

    private const string OpenGame = nameof(OpenGame);

    private const string Raid = nameof(Raid);

    [Category(Misc), Description("Enable this to decline incoming system updates."), DisplayName("Avoid System Update")]
    public bool AvoidSystemUpdate { get; set; } = true;

    [Category(Misc), Description("Extra time in milliseconds to wait between attempts to reconnect. Base time is 30 seconds."), DisplayName("Extra Reconnect Delay")]
    public int ExtraReconnectDelay { get; set; }

    [Category(Raid), Description("[RaidBot] Extra time in milliseconds to wait after accepting a friend."), DisplayName("Extra Time to Add Friend")]
    public int ExtraTimeAddFriend { get; set; }

    [Category(CloseGame), Description("Extra time in milliseconds to wait after clicking to close the game."), DisplayName("Extra Time to Close Game")]
    public int ExtraTimeCloseGame { get; set; }

    // Miscellaneous settings.
    [Category(Misc), Description("[SWSH/SV/PLZA] Extra time in milliseconds to wait after clicking + to connect to Y-Comm (SWSH), L to connect online (SV), or after connecting to Portal (PLZA). Base time for PLZA is 8 seconds."), DisplayName("Extra Time to Connect Online")]
    public int ExtraTimeConnectOnline { get; set; }

    [Category(Raid), Description("[RaidBot] Extra time in milliseconds to wait after deleting a friend."), DisplayName("Extra Time to Delete Friend")]
    public int ExtraTimeDeleteFriend { get; set; }

    [Category(Raid), Description("[RaidBot] Extra time in milliseconds to wait before closing the game to reset the raid."), DisplayName("Extra Time to Reset Raid")]
    public int ExtraTimeEndRaid { get; set; }

    [Category(Misc), Description("[BDSP] Extra time in milliseconds to wait for the Union Room to load before trying to call for a trade."), DisplayName("Extra Time to Load Union Room")]
    public int ExtraTimeJoinUnionRoom { get; set; } = 500;

    [Category(Misc), Description("[BDSP] Extra time in milliseconds to wait for the overworld to load after leaving the Union Room."), DisplayName("Extra Time to Leave Union Room")]
    public int ExtraTimeLeaveUnionRoom { get; set; } = 1000;

    [Category(OpenGame), Description("Extra time in milliseconds to wait before clicking A in title screen."), DisplayName("Extra Time to Load Game")]
    public int ExtraTimeLoadGame { get; set; } = 5000;

    [Category(OpenGame), Description("Extra time in milliseconds to wait for the overworld to load after the title screen."), DisplayName("Extra Time to Load Overworld")]
    public int ExtraTimeLoadOverworld { get; set; } = 3000;

    [Category(Misc), Description("[SV] Extra time in milliseconds to wait for the Poké Portal to load."), DisplayName("Extra Time to Load Portal")]
    public int ExtraTimeLoadPortal { get; set; } = 1000;

    // Opening the game.
    [Category(OpenGame), Description("Enable this if you need to select a profile when starting the game."), DisplayName("Profile Selection Required")]
    public bool ProfileSelectionRequired { get; set; } = false;

    [Category(OpenGame), Description("Extra time in milliseconds to wait for profiles to load when starting the game."), DisplayName("Extra Time to Load Profile")]
    public int ExtraTimeLoadProfile { get; set; }

    [Category(OpenGame), Description("Enable this to add a delay for \"Checking if Game Can be Played\" Pop-up."), DisplayName("Check Game Pop-Up Delay")]
    public bool CheckGameDelay { get; set; } = false;

    [Category(OpenGame), Description("Extra Time to wait for the \"Checking if Game Can Be Played\" Pop-up."), DisplayName("Extra Time to Check Game")]
    public int ExtraTimeCheckGame { get; set; } = 200;

    // Raid-specific timings.
    [Category(Raid), Description("[RaidBot] Extra time in milliseconds to wait for the raid to load after clicking on the den."), DisplayName("Extra Time to Load Raid")]
    public int ExtraTimeLoadRaid { get; set; }

    [Category(Misc), Description("Extra time in milliseconds to wait for the box to load after finding a trade."), DisplayName("Extra Time to Open Box")]
    public int ExtraTimeOpenBox { get; set; } = 1000;

    [Category(Misc), Description("Time to wait after opening the keyboard for code entry during trades."), DisplayName("Extra Time Starting Code Entry")]
    public int ExtraTimeOpenCodeEntry { get; set; } = 1000;

    [Category(Raid), Description("[RaidBot] Extra time in milliseconds to wait after clicking \"Invite Others\" before locking into a Pokémon."), DisplayName("Extra Time to Open Raid")]
    public int ExtraTimeOpenRaid { get; set; }

    [Category(Misc), Description("[BDSP] Extra time in milliseconds to wait for the Y Menu to load at the start of each trade loop."), DisplayName("Extra Time to Load Y-Menu")]
    public int ExtraTimeOpenYMenu { get; set; } = 500;

    // Closing the game.
    [Category(CloseGame), Description("Extra time in milliseconds to wait after pressing HOME to minimize the game."), DisplayName("Extra Time to Return Home")]
    public int ExtraTimeReturnHome { get; set; }

    [Category(Misc), Description("Time to wait after each keypress when navigating Switch menus or entering Link Code."), DisplayName("Switch Keypress Time")]
    public int KeypressTime { get; set; } = 200;

    [Category(Misc), Description("Number of times to attempt reconnecting to a socket connection after a connection is lost. Set this to -1 to try indefinitely."), DisplayName("Reconnection Attempts")]
    public int ReconnectAttempts { get; set; } = 30;
    public override string ToString() => "Extra Time Settings";
}
