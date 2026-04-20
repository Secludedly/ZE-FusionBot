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
        (int)Ball.Cherish,
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

    private static readonly Lazy<List<string>> _cache = new(BuildBallList);

    private static List<string> BuildBallList()
    {
        var strings = GameInfo.GetStrings("en");
        return strings.balllist
            .Select((name, index) => new { Name = name, Index = index })
            .Where(ball =>
                !string.IsNullOrEmpty(ball.Name) &&
                ball.Index > 0 &&
                ball.Index <= (int)Ball.LAOrigin &&
                !ExcludedBallIndices.Contains(ball.Index))
            .Select(ball => ball.Name)
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
            var ballNames = _cache.Value;

            var filteredBalls = string.IsNullOrWhiteSpace(userInput)
                ? ballNames.Take(25)
                : ballNames
                    .Where(name => name.Contains(userInput, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(name => name.StartsWith(userInput, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(name => name)
                    .Take(25);

            var results = filteredBalls
                .Select(name => new AutocompleteResult(name, name))
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

    private static readonly Lazy<List<string>> _cache = new(BuildBallList);

    private static List<string> BuildBallList()
    {
        var strings = GameInfo.GetStrings("en");
        return strings.balllist
            .Select((name, index) => new { Name = name, Index = index })
            .Where(ball =>
                !string.IsNullOrEmpty(ball.Name) &&
                AllowedPlaBalls.Contains(ball.Index))
            .Select(ball => ball.Name)
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
            var ballNames = _cache.Value;

            var filteredBalls = string.IsNullOrWhiteSpace(userInput)
                ? ballNames.Take(25)
                : ballNames
                    .Where(name => name.Contains(userInput, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(name => name.StartsWith(userInput, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(name => name)
                    .Take(25);

            var results = filteredBalls
                .Select(name => new AutocompleteResult(name, name))
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
