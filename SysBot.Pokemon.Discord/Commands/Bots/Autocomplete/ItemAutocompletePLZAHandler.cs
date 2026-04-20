using Discord;
using Discord.Interactions;
using PKHeX.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord.Commands.Bots.Autocomplete;

/// <summary>
/// Autocomplete handler for Legends: Z-A items
/// </summary>
public class ItemAutocompletePLZAHandler : AutocompleteHandler
{
    private static readonly Lazy<List<string>> _cache = new(BuildItemList);

    private static List<string> BuildItemList()
    {
        var strings = GameInfo.GetStrings("en");
        return strings.Item
            .Select((name, index) => new { Name = name, Index = index })
            .Where(item =>
                !string.IsNullOrEmpty(item.Name) &&
                item.Index > 0 &&
                !item.Name.StartsWith("(") &&
                !item.Name.Contains("???") &&
                ItemRestrictions.IsHeldItemAllowed((ushort)item.Index, EntityContext.Gen9))
            .Select(item => item.Name)
            .OrderBy(name => name)
            .ToList();
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
            var itemNames = _cache.Value;

            var filteredItems = string.IsNullOrWhiteSpace(userInput)
                ? itemNames.Take(25)
                : itemNames
                    .Where(name => name.Contains(userInput, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(name => name.StartsWith(userInput, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(name => name)
                    .Take(25);

            var results = filteredItems
                .Select(name => new AutocompleteResult(name, name))
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
