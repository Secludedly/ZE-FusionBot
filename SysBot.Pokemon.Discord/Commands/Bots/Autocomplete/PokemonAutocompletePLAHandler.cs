using Discord;
using Discord.Interactions;
using PKHeX.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord.Commands.Bots.Autocomplete;

/// <summary>
/// Autocomplete handler for Legends: Arceus Pokemon species
/// </summary>
public class PokemonAutocompletePLAHandler : AutocompleteHandler
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

            // Get valid Pokemon for Legends: Arceus
            var validSpecies = GetValidSpeciesForGame(GameVersion.PLA);

            // Filter based on user input
            var filteredSpecies = string.IsNullOrWhiteSpace(userInput)
                ? validSpecies.Take(25)
                : validSpecies
                    .Where(s => s.Display.Contains(userInput, StringComparison.OrdinalIgnoreCase))
                    .Take(25);

            var results = filteredSpecies
                .Select(s => new AutocompleteResult(s.Display, s.Value))
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

    private static List<(string Display, string Value)> GetValidSpeciesForGame(GameVersion game)
    {
        var table = PersonalTable.LA;
        var strings = GameInfo.GetStrings("en");
        var validSpecies = new List<(string Display, string Value)>();

        for (ushort species = 1; species < (ushort)Species.MAX_COUNT; species++)
        {
            var name = ((Species)species).ToString();
            if (name.Equals("None", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Egg", StringComparison.OrdinalIgnoreCase))
                continue;

            var formCount = table[species].FormCount;
            var formList = FormConverter.GetFormList(species, strings.Types, strings.forms, GameInfo.GenderSymbolASCII, EntityContext.Gen8a);

            for (byte form = 0; form < formCount; form++)
            {
                if (!table.IsPresentInGame(species, form))
                    continue;

                var formName = formList.Length > form ? formList[form] : string.Empty;
                if (form > 0 && string.IsNullOrWhiteSpace(formName))
                    continue; // skip unnamed extra forms to avoid duplicates
                if (ForbiddenForms.IsForbidden(species, form, formName))
                    continue;

                var displayName = BuildDisplayName(name, formName, species, form);
                var showdownName = displayName;
                var value = $"{name}|{form}|{showdownName}";
                validSpecies.Add((displayName, value));
            }
        }

        return validSpecies.OrderBy(s => s.Display).ToList();
    }

    private static string BuildDisplayName(string baseName, string formName, ushort species, byte form)
    {
        if (string.IsNullOrWhiteSpace(formName))
            return baseName;

        if (formName.Equals("Normal", StringComparison.OrdinalIgnoreCase))
            return baseName;

        if (ForbiddenForms.ShouldSuppressSuffix(species, form, formName))
            return baseName;

        if (species == (ushort)Species.Basculin && formName.Contains("Striped", StringComparison.OrdinalIgnoreCase))
        {
            var color = formName.Replace("-Striped", string.Empty, StringComparison.OrdinalIgnoreCase)
                                 .Replace("Striped", string.Empty, StringComparison.OrdinalIgnoreCase)
                                 .Replace(" ", string.Empty);
            return $"{baseName}-{color}";
        }

        return $"{baseName}-{formName.Replace(' ', '-')}";
    }
}
