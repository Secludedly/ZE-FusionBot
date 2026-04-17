using Discord;
using Discord.Interactions;
using PKHeX.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord.Commands.Bots.Autocomplete;

public class MoveAutocompleteHandler : AutocompleteHandler
{
    private static readonly Lazy<IReadOnlyList<AutocompleteResult>> _cachedMoves = new(BuildMoveList);

    private static IReadOnlyList<AutocompleteResult> BuildMoveList()
    {
        var strings = GameInfo.GetStrings("en");
        var results = new List<AutocompleteResult>();
        for (int i = 1; i < strings.movelist.Length; i++)
        {
            var name = strings.movelist[i];
            if (!string.IsNullOrWhiteSpace(name))
                results.Add(new AutocompleteResult(name, name));
        }
        return results;
    }

    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        try
        {
            var userInput = autocompleteInteraction.Data.Current.Value?.ToString() ?? string.Empty;
            var allMoves = _cachedMoves.Value;

            IEnumerable<AutocompleteResult> results = string.IsNullOrWhiteSpace(userInput)
                ? allMoves.Take(25)
                : allMoves
                    .Where(m => m.Name.Contains(userInput, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(m => m.Name.StartsWith(userInput, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(m => m.Name)
                    .Take(25);

            var finalResults = results.ToList();
            if (finalResults.Count == 0)
                finalResults.Add(new AutocompleteResult("No matches found", "Tackle"));

            return Task.FromResult(AutocompletionResult.FromSuccess(finalResults));
        }
        catch (Exception ex)
        {
            return Task.FromResult(AutocompletionResult.FromError(InteractionCommandError.Unsuccessful, ex.Message));
        }
    }
}
