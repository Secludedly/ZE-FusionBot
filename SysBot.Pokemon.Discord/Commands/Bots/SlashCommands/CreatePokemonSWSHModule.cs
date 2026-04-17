using Discord.Interactions;
using Discord.WebSocket;
using PKHeX.Core;
using SysBot.Pokemon.Discord.Commands.Bots.Autocomplete;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord.Commands.Bots.SlashCommands;

public class CreatePokemonSWSHModule<T> : InteractionModuleBase<SocketInteractionContext> where T : PKM, new()
{
    private static readonly HashSet<Species> GigantamaxSpecies =
    [
        Species.Venusaur, Species.Charizard, Species.Blastoise, Species.Butterfree,
        Species.Pikachu, Species.Meowth, Species.Machamp, Species.Gengar,
        Species.Kingler, Species.Lapras, Species.Eevee, Species.Snorlax,
        Species.Garbodor, Species.Melmetal, Species.Rillaboom, Species.Cinderace,
        Species.Inteleon, Species.Corviknight, Species.Orbeetle, Species.Drednaw,
        Species.Coalossal, Species.Flapple, Species.Appletun, Species.Sandaconda,
        Species.Toxtricity, Species.Centiskorch, Species.Hatterene, Species.Grimmsnarl,
        Species.Alcremie, Species.Copperajah, Species.Duraludon, Species.Urshifu,
    ];

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
        string? nature = null,

        [Summary("ivs", "Custom IVs (optional) - Format: 31/31/31/31/31/31 (HP/Atk/Def/SpA/SpD/Spe)")]
        string? ivs = null,

        [Summary("evs", "Custom EVs (optional) - Format: 252/252/4/0/0/0 (HP/Atk/Def/SpA/SpD/Spe)")]
        string? evs = null,

        [Summary("nickname", "Nickname for the Pokemon (optional)")]
        string? nickname = null,

        [Summary("ability", "Which ability slot to use (optional)")]
        [Choice("Ability 1", "0")]
        [Choice("Ability 2", "1")]
        [Choice("Hidden Ability", "H")]
        string? ability = null,

        [Summary("move1", "First move (optional)")]
        [Autocomplete(typeof(MoveAutocompleteHandler))]
        string? move1 = null,

        [Summary("move2", "Second move (optional)")]
        [Autocomplete(typeof(MoveAutocompleteHandler))]
        string? move2 = null,

        [Summary("move3", "Third move (optional)")]
        [Autocomplete(typeof(MoveAutocompleteHandler))]
        string? move3 = null,

        [Summary("move4", "Fourth move (optional)")]
        [Autocomplete(typeof(MoveAutocompleteHandler))]
        string? move4 = null,

        [Summary("language", "Pokemon language (optional)")]
        [Choice("English", "English")]
        [Choice("Japanese", "Japanese")]
        [Choice("French", "French")]
        [Choice("German", "German")]
        [Choice("Italian", "Italian")]
        [Choice("Spanish", "Spanish")]
        [Choice("Korean", "Korean")]
        [Choice("Chinese (Simplified)", "ChineseS")]
        [Choice("Chinese (Traditional)", "ChineseT")]
        string? language = null
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
            var speciesInput = pokemon.Contains('|') ? pokemon.Split('|')[0] : pokemon;
            bool canGigantamax = System.Enum.TryParse<Species>(speciesInput, true, out var species) && GigantamaxSpecies.Contains(species);
            string specialFeature = (gigantamax && canGigantamax) ? "Gigantamax: Yes" : string.Empty;

            void PostProcess(T pk)
            {
                if (gigantamax && canGigantamax && pk is PK8 pk8)
                    pk8.CanGigantamax = true;
            }

            await CreatePokemonHelper.ExecuteCreatePokemonAsync<T>(
                Context, pokemon, shiny, item, ball, level, nature, ivs, evs,
                specialFeature, PostProcess, nickname, ability, move1, move2, move3, move4, language
            ).ConfigureAwait(false);
        }
        catch (System.Exception ex)
        {
            await FollowupAsync($"❌ An error occurred: {ex.Message}", ephemeral: true).ConfigureAwait(false);
        }
    }
}
