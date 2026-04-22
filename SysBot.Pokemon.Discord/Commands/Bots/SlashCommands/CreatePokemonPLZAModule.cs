using Discord.Interactions;
using Discord.WebSocket;
using PKHeX.Core;
using SysBot.Pokemon.Discord.Commands.Bots.Autocomplete;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord.Commands.Bots.SlashCommands;

/// <summary>
/// Slash command module for creating Legends: Z-A (PA9) Pokemon with Alpha support
/// </summary>
public class CreatePokemonPLZAModule<T> : InteractionModuleBase<SocketInteractionContext> where T : PKM, new()
{
    [SlashCommand("create-plza", "Create a Legends: Z-A Pokemon with Alpha support")]
    public async Task CreatePokemonPLZAAsync(
        [Summary("pokemon", "Pokemon species")]
        [Autocomplete(typeof(PokemonAutocompletePLZAHandler))]
        string pokemon,

        [Summary("shiny", "Should the Pokemon be shiny?")]
        bool shiny = false,

        [Summary("item", "Held item (optional)")]
        [Autocomplete(typeof(ItemAutocompletePLZAHandler))]
        string? item = null,

        [Summary("ball", "Poke Ball (optional)")]
        [Autocomplete(typeof(BallAutocompleteHandler))]
        string? ball = null,

        [Summary("alpha", "Should the Pokemon be Alpha?")]
        bool alpha = false,

        [Summary("level", "Pokemon level (1-100)")]
        [MinValue(1)]
        [MaxValue(100)]
        int level = 100,

        [Summary("nature", "Pokemon nature (optional)")]
        [Autocomplete(typeof(NatureAutocompleteHandler))]
        string? nature = null,

        [Summary("ivs", "Custom IVs (optional) - Format: 31/31/31/31/31/31 (HP/Atk/Def/SpA/SpD/Spe)")]
        string? ivs = null,

        [Summary("evs", "Custom EVs (optional) - Format: 252/252/4/0/0/0 (HP/Atk/Def/SpA/SpD/Spe)")]
        string? evs = null
    )
    {
        if (Context.Guild == null)
        {
            await RespondAsync("❌ This command can only be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: false).ConfigureAwait(false);

        try
        {
            // Build Alpha feature string (will be added to Showdown format)
            string specialFeature = alpha ? "Alpha: Yes" : string.Empty;

            await CreatePokemonHelper.ExecuteCreatePokemonAsync<T>(
                Context,
                pokemon,
                shiny,
                item,
                ball,
                level,
                nature,
                ivs,
                evs,
                specialFeature,
                null // No post-processing needed for Alpha (handled by Showdown)
            ).ConfigureAwait(false);
        }
        catch (System.Exception ex)
        {
            await FollowupAsync($"❌ An error occurred: {ex.Message}", ephemeral: true).ConfigureAwait(false);
        }
    }
}
