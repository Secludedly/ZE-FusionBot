using Discord.Interactions;
using Discord.WebSocket;
using PKHeX.Core;
using SysBot.Pokemon.Discord.Commands.Bots.Autocomplete;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord.Commands.Bots.SlashCommands;

/// <summary>
/// Slash command module for creating Scarlet/Violet (PK9) Pokemon with Tera Type
/// </summary>
public class CreatePokemonSVModule<T> : InteractionModuleBase<SocketInteractionContext> where T : PKM, new()
{
    [SlashCommand("create-sv", "Create a Scarlet/Violet Pokemon with Tera Type")]
    public async Task CreatePokemonSVAsync(
        [Summary("pokemon", "Pokemon species")]
        [Autocomplete(typeof(PokemonAutocompleteSVHandler))]
        string pokemon,

        [Summary("shiny", "Should the Pokemon be shiny?")]
        bool shiny = false,

        [Summary("item", "Held item (optional)")]
        [Autocomplete(typeof(ItemAutocompleteSVHandler))]
        string? item = null,

        [Summary("ball", "Poke Ball (optional)")]
        [Autocomplete(typeof(BallAutocompleteHandler))]
        string? ball = null,

        [Summary("teratype", "Tera Type (optional)")]
        [Choice("Normal", "Normal")]
        [Choice("Fire", "Fire")]
        [Choice("Water", "Water")]
        [Choice("Grass", "Grass")]
        [Choice("Electric", "Electric")]
        [Choice("Ice", "Ice")]
        [Choice("Fighting", "Fighting")]
        [Choice("Poison", "Poison")]
        [Choice("Ground", "Ground")]
        [Choice("Flying", "Flying")]
        [Choice("Psychic", "Psychic")]
        [Choice("Bug", "Bug")]
        [Choice("Rock", "Rock")]
        [Choice("Ghost", "Ghost")]
        [Choice("Dragon", "Dragon")]
        [Choice("Dark", "Dark")]
        [Choice("Steel", "Steel")]
        [Choice("Fairy", "Fairy")]
        [Choice("Stellar", "Stellar")]
        string? teratype = null,

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
            // Build Tera Type feature string (will be added to Showdown format)
            string specialFeature = !string.IsNullOrWhiteSpace(teratype) ? $"Tera Type: {teratype}" : string.Empty;

            // Post-processing: Apply Tera Type if specified
            void PostProcess(T pk)
            {
                if (!string.IsNullOrWhiteSpace(teratype) && pk is PK9 pk9)
                {
                    if (System.Enum.TryParse<MoveType>(teratype, true, out var tera))
                    {
                        pk9.TeraTypeOriginal = tera;
                        pk9.SetTeraType(tera);
                    }
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
