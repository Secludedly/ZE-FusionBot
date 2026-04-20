using Discord;
using Discord.Interactions;
using PKHeX.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord.Commands.Bots.Autocomplete;

/// <summary>
/// Autocomplete handler for Pokemon species names
/// </summary>
public class PokemonAutocompleteHandler : AutocompleteHandler
{
    private static readonly Lazy<List<string>> _cache = new(BuildSpeciesList);

    private static List<string> BuildSpeciesList() =>
        Enum.GetValues<Species>()
            .Where(s => s > 0 && s < Species.MAX_COUNT)
            .Select(s => s.ToString())
            .Where(name =>
                !name.Contains('_') &&
                !name.Equals("None", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("Egg", StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s)
            .ToList();

    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        try
        {
            var userInput = autocompleteInteraction.Data.Current.Value?.ToString() ?? string.Empty;

            var allSpecies = _cache.Value;

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
