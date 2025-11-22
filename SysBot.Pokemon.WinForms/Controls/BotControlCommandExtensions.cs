using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.Discord;
using SysBot.Pokemon.WinForms;
using SysBot.Pokemon;
using System.Threading.Tasks;
using System.Threading;
using System;

public static class BotControlCommandExtensions
{
    public static bool IsUsable(this BotController.BotControlCommand cmd, bool running, bool paused) => cmd switch
    {
        BotController.BotControlCommand.Start => !running,
        BotController.BotControlCommand.Stop => running,
        BotController.BotControlCommand.Idle => running && !paused,
        BotController.BotControlCommand.Resume => paused,
        BotController.BotControlCommand.Restart => true,
        BotController.BotControlCommand.RebootAndStop => true,
        BotController.BotControlCommand.ScreenOn => running,
        BotController.BotControlCommand.ScreenOff => running,
        _ => false
    };

    public static async Task SendScreenState(string ip, bool turnOn)
    {
        var sent = false;
        var runners = new Func<Task>[]
        {
            () => TrySendScreenState<PA9>(ip, turnOn),
            () => TrySendScreenState<PK9>(ip, turnOn),
            () => TrySendScreenState<PK8>(ip, turnOn),
            () => TrySendScreenState<PA8>(ip, turnOn),
            () => TrySendScreenState<PB8>(ip, turnOn),
            () => TrySendScreenState<PB7>(ip, turnOn)
        };

        foreach (var runner in runners)
        {
            try
            {
                await runner();
                sent = true;
                break;
            }
            catch (BotNotFoundException) { /* continue */ }
            catch (Exception ex)
            {
                LogUtil.LogSafe(ex, "[ScreenToggle]");
            }
        }

        if (!sent)
            LogUtil.LogError($"[ScreenToggle] No matching bot found with IP: {ip} in any SysCord<T> runner", "RemoteControl");
    }

    private static async Task TrySendScreenState<T>(string ip, bool turnOn) where T : PKM, new()
    {
        var bot = SysCord<T>.Runner.GetBot(ip);
        if (bot == null)
            throw new BotNotFoundException();

        var connection = bot.Bot.Connection;
        if (connection == null)
            throw new Exception($"[ScreenToggle] Bot connection is null for IP: {ip}");

        var isCRLF = bot.Bot is SwitchRoutineExecutor<PokeBotState> { UseCRLF: true };
        var cmd = SwitchCommand.SetScreen(turnOn ? ScreenState.On : ScreenState.Off, isCRLF);

        await connection.SendAsync(cmd, CancellationToken.None).ConfigureAwait(false);
        LogUtil.LogInfo($"[ScreenToggle] Screen turned {(turnOn ? "on" : "off")} for {connection.Name}", "RemoteControl");
    }

    private class BotNotFoundException : Exception { }
}
