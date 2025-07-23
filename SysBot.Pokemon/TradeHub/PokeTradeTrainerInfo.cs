namespace SysBot.Pokemon;

public record PokeTradeTrainerInfo
{
    public readonly string TrainerName;
    public readonly ulong ID;

    public object DiscordUser { get; internal set; }

    public PokeTradeTrainerInfo(string name, ulong id = 0, object discordUser = null!)
    {
        TrainerName = name;
        ID = id;
        DiscordUser = discordUser ?? new object();
    }
}
