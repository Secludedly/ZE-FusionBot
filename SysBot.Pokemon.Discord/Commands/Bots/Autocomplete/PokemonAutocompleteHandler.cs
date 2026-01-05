using Discord;
using Discord.Interactions;
using PKHeX.Core;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord.Commands.Bots.Autocomplete;

/// <summary>
/// Autocomplete handler for Pokemon species names
/// </summary>
public class PokemonAutocompleteHandler : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        try
        {
            var userInput = autocompleteInteraction.Data.Current.Value?.ToString() ?? string.Empty;

            // Get all valid Pokemon species (exclude forms, Eggs, None, etc.)
            var allSpecies = Enum.GetValues<Species>()
                .Where(s => s > 0 && s < Species.MAX_COUNT) // Valid species only
                .Select(s => s.ToString())
                .Where(name =>
                    !name.Contains('_') && // Exclude forms like Pikachu_Cosplay
                    !name.Equals("None", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("Egg", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Filter based on user input
            var filteredSpecies = string.IsNullOrWhiteSpace(userInput)
                ? allSpecies.Take(25) // Show first 25 if no input
                : allSpecies
                    .Where(s => s.Contains(userInput, StringComparison.OrdinalIgnoreCase))
                    .Take(25); // Discord limit

            var results = filteredSpecies
                .Select(s => new AutocompleteResult(s, s))
                .ToList();

            return Task.FromResult(
                results.Any()
                    ? AutocompletionResult.FromSuccess(results)
                    : AutocompletionResult.FromSuccess(new[]
                    {
                        new AutocompleteResult("No matches found", "None")
                    })
            );
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                AutocompletionResult.FromError(InteractionCommandError.Unsuccessful, ex.Message)
            );
        }
    }
}
