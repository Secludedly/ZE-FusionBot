using Discord.Interactions;
using Discord.WebSocket;
using PKHeX.Core;
using SysBot.Pokemon.Discord.Commands.Bots.Autocomplete;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord.Commands.Bots.SlashCommands;

/// <summary>
/// Slash command module for creating Let's Go Pikachu/Eevee (PB7) Pokemon
/// </summary>
public class CreatePokemonLGPEModule<T> : InteractionModuleBase<SocketInteractionContext> where T : PKM, new()
{
    [SlashCommand("create-lgpe", "Create a Let's Go Pikachu/Eevee Pokemon")]
    public async Task CreatePokemonLGPEAsync(
        [Summary("pokemon", "Pokemon species")]
        [Autocomplete(typeof(PokemonAutocompleteLGPEHandler))]
        string pokemon,

        [Summary("shiny", "Should the Pokemon be shiny?")]
        bool shiny = false,

        // Note: Let's Go Pikachu/Eevee does not have held items or special features
        // [Summary("item", "Held item (optional)")]
        // [Autocomplete(typeof(ItemAutocompleteLGPEHandler))]
        // string? item = null,

        [Summary("ball", "Poke Ball (optional)")]
        [Autocomplete(typeof(BallAutocompleteHandler))]
        string? ball = null,

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
            await CreatePokemonHelper.ExecuteCreatePokemonAsync<T>(
                Context,
                pokemon,
                shiny,
                null, // No held item
                ball,
                level,
                nature,
                ivs,
                evs,
                string.Empty, // No special features
                null
            ).ConfigureAwait(false);
        }
        catch (System.Exception ex)
        {
            await FollowupAsync($"❌ An error occurred: {ex.Message}", ephemeral: true).ConfigureAwait(false);
        }
    }
}
