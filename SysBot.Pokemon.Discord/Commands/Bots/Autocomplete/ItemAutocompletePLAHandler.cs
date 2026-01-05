using Discord;
using Discord.Interactions;
using PKHeX.Core;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord.Commands.Bots.Autocomplete;

public class ItemAutocompletePLAHandler : AutocompleteHandler
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
            var strings = GameInfo.GetStrings("en");
            var itemNames = strings.Item
                .Select((name, index) => new { Name = name, Index = index })
                .Where(item =>
                    !string.IsNullOrEmpty(item.Name) &&
                    item.Index > 0 &&
                    !item.Name.StartsWith("(") &&
                    !item.Name.Contains("???") &&
                    ItemRestrictions.IsHeldItemAllowed((ushort)item.Index, EntityContext.Gen8a))
                .ToList();

            var filteredItems = string.IsNullOrWhiteSpace(userInput)
                ? itemNames.OrderBy(i => i.Name).Take(25)
                : itemNames
                    .Where(i => i.Name.Contains(userInput, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(i => i.Name.StartsWith(userInput, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(i => i.Name)
                    .Take(25);

            var results = filteredItems.Select(i => new AutocompleteResult(i.Name, i.Name)).ToList();
            return Task.FromResult(results.Any() ? AutocompletionResult.FromSuccess(results) : AutocompletionResult.FromSuccess(new[] { new AutocompleteResult("No matches found", "None") }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(AutocompletionResult.FromError(InteractionCommandError.Unsuccessful, ex.Message));
        }
    }
}
