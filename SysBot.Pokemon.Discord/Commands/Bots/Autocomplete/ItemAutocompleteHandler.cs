using Discord;
using Discord.Interactions;
using PKHeX.Core;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord.Commands.Bots.Autocomplete;

/// <summary>
/// Autocomplete handler for held items
/// </summary>
public class ItemAutocompleteHandler : AutocompleteHandler
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

            // Get all item names from PKHeX
            var strings = GameInfo.GetStrings("en");
            var itemNames = strings.Item
                .Select((name, index) => new { Name = name, Index = index })
                .Where(item =>
                    !string.IsNullOrEmpty(item.Name) &&
                    item.Index > 0 && // Skip "None"
                    !item.Name.StartsWith("(") && // Skip invalid entries like "(illegal)"
                    !item.Name.Contains("???") && // Skip unknown items
                    ItemRestrictions.IsHeldItemAllowed((ushort)item.Index, EntityContext.Gen9)) // Only held items allowed in Gen 9
                .ToList();

            // Filter based on user input
            var filteredItems = string.IsNullOrWhiteSpace(userInput)
                ? itemNames
                    .OrderBy(i => i.Name)
                    .Take(25) // Show first 25 alphabetically if no input
                : itemNames
                    .Where(i => i.Name.Contains(userInput, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(i => i.Name.StartsWith(userInput, StringComparison.OrdinalIgnoreCase) ? 0 : 1) // Prioritize starts-with matches
                    .ThenBy(i => i.Name)
                    .Take(25); // Discord limit

            var results = filteredItems
                .Select(i => new AutocompleteResult(i.Name, i.Name))
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
