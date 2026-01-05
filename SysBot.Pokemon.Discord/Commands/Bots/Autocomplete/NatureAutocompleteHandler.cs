using Discord;
using Discord.Interactions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord.Commands.Bots.Autocomplete;

/// <summary>
/// Autocomplete handler for Pokemon natures that shows the stat changes in the label
/// while keeping the raw nature name as the value passed to the command.
/// </summary>
public class NatureAutocompleteHandler : AutocompleteHandler
{
    private static readonly IReadOnlyList<AutocompleteResult> NatureOptions = new List<AutocompleteResult>
    {
        new("Adamant (Atk+ / SpA-)", "Adamant"),
        new("Bashful (Neutral)", "Bashful"),
        new("Bold (Def+ / Atk-)", "Bold"),
        new("Brave (Atk+ / Spe-)", "Brave"),
        new("Calm (SpD+ / Atk-)", "Calm"),
        new("Careful (SpD+ / SpA-)", "Careful"),
        new("Docile (Neutral)", "Docile"),
        new("Gentle (SpD+ / Def-)", "Gentle"),
        new("Hardy (Neutral)", "Hardy"),
        new("Hasty (Spe+ / Def-)", "Hasty"),
        new("Impish (Def+ / SpA-)", "Impish"),
        new("Jolly (Spe+ / SpA-)", "Jolly"),
        new("Lax (Def+ / SpD-)", "Lax"),
        new("Lonely (Atk+ / Def-)", "Lonely"),
        new("Mild (SpA+ / Def-)", "Mild"),
        new("Modest (SpA+ / Atk-)", "Modest"),
        new("Naive (Spe+ / SpD-)", "Naive"),
        new("Naughty (Atk+ / SpD-)", "Naughty"),
        new("Quiet (SpA+ / Spe-)", "Quiet"),
        new("Quirky (Neutral)", "Quirky"),
        new("Rash (SpA+ / SpD-)", "Rash"),
        new("Relaxed (Def+ / Spe-)", "Relaxed"),
        new("Sassy (SpD+ / Spe-)", "Sassy"),
        new("Serious (Neutral)", "Serious"),
        new("Timid (Spe+ / Atk-)", "Timid"),
    };

    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        try
        {
            var userInput = autocompleteInteraction.Data.Current.Value?.ToString() ?? string.Empty;

            IEnumerable<AutocompleteResult> results = NatureOptions;

            if (!string.IsNullOrWhiteSpace(userInput))
            {
                results = NatureOptions
                    .Where(option =>
                        option.Name.Contains(userInput, StringComparison.OrdinalIgnoreCase) ||
                        (option.Value?.ToString()?.Contains(userInput, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            var finalResults = results.Take(25).ToList();

            if (finalResults.Count == 0)
            {
                finalResults.Add(new AutocompleteResult("No matches found", "Adamant"));
            }

            return Task.FromResult(AutocompletionResult.FromSuccess(finalResults));
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                AutocompletionResult.FromError(InteractionCommandError.Unsuccessful, ex.Message)
            );
        }
    }
}
