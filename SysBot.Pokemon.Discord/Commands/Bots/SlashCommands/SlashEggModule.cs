using Discord.Interactions;
using Discord.WebSocket;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
using SysBot.Base;
using SysBot.Pokemon.Discord.Commands.Bots.Autocomplete;
using SysBot.Pokemon.Helpers;
using System;
using System.Text;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord.Commands.Bots.SlashCommands;

public class SlashEggModule<T> : InteractionModuleBase<SocketInteractionContext> where T : PKM, new()
{
    [SlashCommand("egg-sv", "Request an egg from a Scarlet/Violet Pokemon")]
    public async Task EggSVAsync(
        [Summary("pokemon", "Pokemon species for the egg")]
        [Autocomplete(typeof(PokemonAutocompleteSVHandler))]
        string pokemon,

        [Summary("shiny", "Should the egg produce a shiny Pokemon?")]
        bool shiny = false
    )
    {
        if (typeof(T).Name != "PK9")
        {
            await RespondAsync("❌ This command requires the bot to be running in SV mode.", ephemeral: true).ConfigureAwait(false);
            return;
        }
        await HandleEggAsync(pokemon, shiny).ConfigureAwait(false);
    }

    [SlashCommand("egg-swsh", "Request an egg from a Sword/Shield Pokemon")]
    public async Task EggSWSHAsync(
        [Summary("pokemon", "Pokemon species for the egg")]
        [Autocomplete(typeof(PokemonAutocompleteSWSHHandler))]
        string pokemon,

        [Summary("shiny", "Should the egg produce a shiny Pokemon?")]
        bool shiny = false
    )
    {
        if (typeof(T).Name != "PK8")
        {
            await RespondAsync("❌ This command requires the bot to be running in SWSH mode.", ephemeral: true).ConfigureAwait(false);
            return;
        }
        await HandleEggAsync(pokemon, shiny).ConfigureAwait(false);
    }

    [SlashCommand("egg-bdsp", "Request an egg from a Brilliant Diamond/Shining Pearl Pokemon")]
    public async Task EggBDSPAsync(
        [Summary("pokemon", "Pokemon species for the egg")]
        [Autocomplete(typeof(PokemonAutocompleteBDSPHandler))]
        string pokemon,

        [Summary("shiny", "Should the egg produce a shiny Pokemon?")]
        bool shiny = false
    )
    {
        if (typeof(T).Name != "PB8")
        {
            await RespondAsync("❌ This command requires the bot to be running in BDSP mode.", ephemeral: true).ConfigureAwait(false);
            return;
        }
        await HandleEggAsync(pokemon, shiny).ConfigureAwait(false);
    }

    private async Task HandleEggAsync(string pokemon, bool shiny)
    {
        if (Context.Guild == null)
        {
            await RespondAsync("❌ This command can only be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var Info = SysCord<T>.Runner.Hub.Queues.Info;
        if (Info.IsUserInQueue(Context.User.Id))
        {
            await RespondAsync("❌ You are already in the queue! Please wait until your current trade is processed.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: false).ConfigureAwait(false);

        try
        {
            // Resolve species name from autocomplete value
            string showdownName = pokemon;
            if (pokemon.Contains('|'))
            {
                var parts = pokemon.Split('|');
                showdownName = parts.Length >= 3 ? parts[2] : parts[0];
            }

            var setBuilder = new StringBuilder();
            setBuilder.AppendLine(showdownName);
            setBuilder.AppendLine("IVs: 31 HP / 31 Atk / 31 Def / 31 SpA / 31 SpD / 31 Spe");
            if (shiny)
                setBuilder.AppendLine("Shiny: Yes");

            var set = new ShowdownSet(setBuilder.ToString());
            var regenTemplate = new RegenTemplate(set);
            var sav = AutoLegalityWrapper.GetTrainerInfo<T>();
            var pkm = sav.GenerateEgg(regenTemplate, out var result);

            if (result != LegalizationResult.Regenerated || pkm == null)
            {
                var reason = result == LegalizationResult.Timeout
                    ? "Egg generation timed out."
                    : $"Failed to generate an egg for **{showdownName}**. This species may not be breedable in this game.";
                await FollowupAsync($"❌ {reason}", ephemeral: true).ConfigureAwait(false);
                return;
            }

            pkm = EntityConverter.ConvertToType(pkm, typeof(T), out _) ?? pkm;
            if (pkm is not T pk)
            {
                await FollowupAsync("❌ Failed to convert the egg. Please try again.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            pk.ResetPartyStats();
            await CreatePokemonHelper.QueuePokemonForTradeAsync(Context, pk, $"{showdownName} Egg", isMysteryEgg: false).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogUtil.LogSafe(ex, nameof(SlashEggModule<T>));
            await FollowupAsync($"❌ An error occurred: {ex.Message}", ephemeral: true).ConfigureAwait(false);
        }
    }
}
