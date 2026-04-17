using Discord.Interactions;
using Discord.WebSocket;
using PKHeX.Core;
using SysBot.Pokemon.Discord.Commands.Bots.Autocomplete;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord.Commands.Bots.SlashCommands;

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
        string? language = null,

        [Summary("scale", "Pokemon size scale 0=tiny, 128=average, 255=huge (optional)")]
        int? scale = null
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
            string specialFeature = !string.IsNullOrWhiteSpace(teratype) ? $"Tera Type: {teratype}" : string.Empty;

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

                if (scale.HasValue && pk is PK9 pk9s)
                {
                    pk9s.Scale = (byte)System.Math.Clamp(scale.Value, 0, 255);
                }
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
