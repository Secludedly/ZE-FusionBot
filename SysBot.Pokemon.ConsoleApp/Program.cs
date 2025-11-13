using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.Z3;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SysBot.Pokemon.ConsoleApp;

public static class Program
{
    private const string ConfigPath = "config.json";

    private static void ExitNoConfig()
    {
        var bot = new PokeBotState { Connection = new SwitchConnectionConfig { IP = "192.168.0.1", Port = 6000 }, InitialRoutine = PokeRoutineType.FlexTrade };
        var cfg = new ProgramConfig { Bots = [bot] };
        var created = JsonSerializer.Serialize(cfg, ProgramConfigContext.Default.ProgramConfig);
        File.WriteAllText(ConfigPath, created);
        LogUtil.LogInfo("SysBot", "Created new config file since none was found in the program's path. Please configure it and restart the program.");
        LogUtil.LogInfo("SysBot", "It is suggested to configure this config file using the GUI project if possible, as it will help you assign values correctly.");
        LogUtil.LogInfo("SysBot", "Press any key to exit.");
        Console.ReadKey();
    }

    private static void Main(string[] args)
    {
        LogUtil.LogInfo("SysBot", "Starting up...");
        if (args.Length > 1)
            LogUtil.LogInfo("SysBot", "This program does not support command line arguments.");

        if (!File.Exists(ConfigPath))
        {
            ExitNoConfig();
            return;
        }

        try
        {
            var lines = File.ReadAllText(ConfigPath);
            var cfg = JsonSerializer.Deserialize(lines, ProgramConfigContext.Default.ProgramConfig) ?? new ProgramConfig();
            PokeTradeBotSWSH.SeedChecker = new Z3SeedSearchHandler<PK8>();
            BotContainer.RunBots(cfg);
        }
        catch (Exception)
        {
            LogUtil.LogInfo("SysBot", "Unable to start bots with saved config file. Please copy your config from the WinForms project or delete it and reconfigure.");
            Console.ReadKey();
        }
    }
}

[JsonSerializable(typeof(ProgramConfig))]
[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public sealed partial class ProgramConfigContext : JsonSerializerContext;

public static class BotContainer
{
    public static void RunBots(ProgramConfig prog)
    {
        IPokeBotRunner env = GetRunner(prog);
        foreach (var bot in prog.Bots)
        {
            bot.Initialize();
            if (!AddBot(env, bot, prog.Mode))
                LogUtil.LogInfo("SysBot", $"Failed to add bot: {bot}");
        }

        LogUtil.Forwarders.Add(ConsoleForwarder.Instance);
        env.StartAll();
        LogUtil.LogInfo("SysBot", $"Started all bots (Count: {prog.Bots.Length}).");
        LogUtil.LogInfo("SysBot", "Press any key to stop execution and quit. Feel free to minimize this window!");
        Console.ReadKey();
        env.StopAll();
    }

    private static bool AddBot(IPokeBotRunner env, PokeBotState cfg, ProgramMode mode)
    {
        if (!cfg.IsValid())
        {
            LogUtil.LogInfo("SysBot", $"{cfg}'s config is not valid.");
            return false;
        }

        PokeRoutineExecutorBase newBot;
        try
        {
            newBot = env.CreateBotFromConfig(cfg);
        }
        catch
        {
            LogUtil.LogInfo("SysBot", $"Current Mode ({mode}) does not support this type of bot ({cfg.CurrentRoutineType}).");
            return false;
        }
        try
        {
            env.Add(newBot);
        }
        catch (ArgumentException ex)
        {
            LogUtil.LogInfo("SysBot", ex.Message);
            return false;
        }

        LogUtil.LogInfo("SysBot", $"Added: {cfg}: {cfg.InitialRoutine}");
        return true;
    }

    private static IPokeBotRunner GetRunner(ProgramConfig prog) => prog.Mode switch
    {
        ProgramMode.SWSH => new PokeBotRunnerImpl<PK8>(new PokeTradeHub<PK8>(prog.Hub), new BotFactory8SWSH(), prog),
        ProgramMode.BDSP => new PokeBotRunnerImpl<PB8>(new PokeTradeHub<PB8>(prog.Hub), new BotFactory8BS(), prog),
        ProgramMode.LA => new PokeBotRunnerImpl<PA8>(new PokeTradeHub<PA8>(prog.Hub), new BotFactory8LA(), prog),
        ProgramMode.SV => new PokeBotRunnerImpl<PK9>(new PokeTradeHub<PK9>(prog.Hub), new BotFactory9SV(), prog),
        ProgramMode.LGPE => new PokeBotRunnerImpl<PB7>(new PokeTradeHub<PB7>(prog.Hub), new BotFactory7LGPE(), prog),
        ProgramMode.PLZA => new PokeBotRunnerImpl<PA9>(new PokeTradeHub<PA9>(prog.Hub), new BotFactory9PLZA(), prog),
        _ => throw new IndexOutOfRangeException("Unsupported mode."),
    };
}
