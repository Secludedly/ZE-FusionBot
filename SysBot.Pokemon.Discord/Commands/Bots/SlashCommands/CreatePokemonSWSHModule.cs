using Discord.Interactions;
using Discord.WebSocket;
using PKHeX.Core;
using SysBot.Pokemon.Discord.Commands.Bots.Autocomplete;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord.Commands.Bots.SlashCommands;

/// <summary>
/// Slash command module for creating Sword/Shield (PK8) Pokemon with Gigantamax support
/// </summary>
public class CreatePokemonSWSHModule<T> : InteractionModuleBase<SocketInteractionContext> where T : PKM, new()
{
    /// <summary>
    /// List of Pokemon species that can Gigantamax in Sword/Shield
    /// </summary>
    private static readonly HashSet<Species> GigantamaxSpecies = new()
    {
        Species.Venusaur,
        Species.Charizard,
        Species.Blastoise,
        Species.Butterfree,
        Species.Pikachu,
        Species.Meowth,
        Species.Machamp,
        Species.Gengar,
        Species.Kingler,
        Species.Lapras,
        Species.Eevee,
        Species.Snorlax,
        Species.Garbodor,
        Species.Melmetal,
        Species.Rillaboom,
        Species.Cinderace,
        Species.Inteleon,
        Species.Corviknight,
        Species.Orbeetle,
        Species.Drednaw,
        Species.Coalossal,
        Species.Flapple,
        Species.Appletun,
        Species.Sandaconda,
        Species.Toxtricity,
        Species.Centiskorch,
        Species.Hatterene,
        Species.Grimmsnarl,
        Species.Alcremie,
        Species.Copperajah,
        Species.Duraludon,
        Species.Urshifu,
    };

    [SlashCommand("create-swsh", "Create a Sword/Shield Pokemon with Gigantamax support")]
    public async Task CreatePokemonSWSHAsync(
        [Summary("pokemon", "Pokemon species")]
        [Autocomplete(typeof(PokemonAutocompleteSWSHHandler))]
        string pokemon,

        [Summary("shiny", "Should the Pokemon be shiny?")]
        bool shiny = false,

        [Summary("item", "Held item (optional)")]
        [Autocomplete(typeof(ItemAutocompleteSWSHHandler))]
        string? item = null,

        [Summary("ball", "Poke Ball (optional)")]
        [Autocomplete(typeof(BallAutocompleteHandler))]
        string? ball = null,

        [Summary("gigantamax", "Can Gigantamax?")]
        bool gigantamax = false,

        [Summary("level", "Pokemon level (1-100)")]
        [MinValue(1)]
        [MaxValue(100)]
        int level = 100,

        [Summary("nature", "Pokemon nature (optional)")]
        [Autocomplete(typeof(NatureAutocompleteHandler))]
        string? nature = null
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
            // Parse Pokemon species to check if it can Gigantamax
            var speciesInput = pokemon;
            if (!string.IsNullOrWhiteSpace(pokemon) && pokemon.Contains('|'))
            {
                speciesInput = pokemon.Split('|')[0];
            }

            bool canGigantamax = false;
            if (System.Enum.TryParse<Species>(speciesInput, true, out var species))
            {
                canGigantamax = GigantamaxSpecies.Contains(species);
            }

            // Build Gigantamax feature string - only if the species can actually Gigantamax
            string specialFeature = (gigantamax && canGigantamax) ? "Gigantamax: Yes" : string.Empty;

            // Post-processing: Apply Gigantamax only if the species can actually Gigantamax
            void PostProcess(T pk)
            {
                if (gigantamax && canGigantamax && pk is PK8 pk8)
                {
                    pk8.CanGigantamax = true;
                }
            }

            await CreatePokemonHelper.ExecuteCreatePokemonAsync<T>(
                Context,
                pokemon,
                shiny,
                item,
                ball,
                level,
                nature,
                specialFeature,
                PostProcess
            ).ConfigureAwait(false);
        }
        catch (System.Exception ex)
        {
            await FollowupAsync($"❌ An error occurred: {ex.Message}", ephemeral: true).ConfigureAwait(false);
        }
    }
}
