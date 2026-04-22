using Discord;
using Discord.Interactions;
using PKHeX.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord.Commands.Bots.Autocomplete;

/// <summary>
/// Autocomplete handler for Poke Balls
/// </summary>
public class BallAutocompleteHandler : AutocompleteHandler
{
    private static readonly HashSet<int> ExcludedBallIndices = new()
    {
        // Event-only Cherish Ball
        (int)Ball.Cherish,
        // PLA-only balls
        (int)Ball.LAPoke,
        (int)Ball.LAUltra,
        (int)Ball.LAFeather,
        (int)Ball.LAWing,
        (int)Ball.LAJet,
        (int)Ball.LAHeavy,
        (int)Ball.LALeaden,
        (int)Ball.LAGigaton,
        (int)Ball.LAOrigin,
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

            // Get all ball names from PKHeX
            var strings = GameInfo.GetStrings("en");
            var ballNames = strings.balllist
                .Select((name, index) => new { Name = name, Index = index })
                .Where(ball =>
                    !string.IsNullOrEmpty(ball.Name) &&
                    ball.Index > 0 && // Skip "None"
                    ball.Index <= (int)Ball.LAOrigin && // Only valid balls
                    !ExcludedBallIndices.Contains(ball.Index)) // Exclude PLA-only and event-only balls for non-PLA commands
                .ToList();

            // Filter based on user input
            var filteredBalls = string.IsNullOrWhiteSpace(userInput)
                ? ballNames
                    .OrderBy(b => b.Name)
                    .Take(25) // Show first 25 alphabetically if no input
                : ballNames
                    .Where(b => b.Name.Contains(userInput, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(b => b.Name.StartsWith(userInput, StringComparison.OrdinalIgnoreCase) ? 0 : 1) // Prioritize starts-with matches
                    .ThenBy(b => b.Name)
                    .Take(25); // Discord limit

            var results = filteredBalls
                .Select(b => new AutocompleteResult(b.Name, b.Name))
                .ToList();

            return Task.FromResult(
                results.Any()
                    ? AutocompletionResult.FromSuccess(results)
                    : AutocompletionResult.FromSuccess(new[]
                    {
                        new AutocompleteResult("No matches found", "Poke")
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

/// <summary>
/// PLA-specific autocomplete handler for Poke Balls (limited to PLA-legal balls)
/// </summary>
public class PlaBallAutocompleteHandler : AutocompleteHandler
{
    private static readonly HashSet<int> AllowedPlaBalls = new()
    {
        (int)Ball.Master,
        (int)Ball.LAPoke,
        (int)Ball.LAUltra,
        (int)Ball.LAFeather,
        (int)Ball.LAWing,
        (int)Ball.LAJet,
        (int)Ball.LAHeavy,
        (int)Ball.LALeaden,
        (int)Ball.LAGigaton,
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

            var strings = GameInfo.GetStrings("en");
            var ballNames = strings.balllist
                .Select((name, index) => new { Name = name, Index = index })
                .Where(ball =>
                    !string.IsNullOrEmpty(ball.Name) &&
                    AllowedPlaBalls.Contains(ball.Index))
                .ToList();

            var filteredBalls = string.IsNullOrWhiteSpace(userInput)
                ? ballNames
                    .OrderBy(b => b.Name)
                    .Take(25)
                : ballNames
                    .Where(b => b.Name.Contains(userInput, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(b => b.Name.StartsWith(userInput, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(b => b.Name)
                    .Take(25);

            var results = filteredBalls
                .Select(b => new AutocompleteResult(b.Name, b.Name))
                .ToList();

            return Task.FromResult(
                results.Any()
                    ? AutocompletionResult.FromSuccess(results)
                    : AutocompletionResult.FromSuccess(new[]
                    {
                        new AutocompleteResult("No matches found", "Poke")
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
