using System;
using FluentAssertions;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
using SysBot.Pokemon;
using Xunit;

namespace SysBot.Tests;

/// <summary>
/// Tests to verify egg generation works correctly using ALM's GenerateEgg method.
/// </summary>
public class EggTests
{
    static EggTests() => AutoLegalityWrapper.EnsureInitialized(new Pokemon.LegalitySettings());

    [Theory]
    [InlineData("Sprigatito", "Protean", true, false)]
    [InlineData("Fuecoco", "Unaware", false, true)]
    [InlineData("Quaxly", "Moxie", true, false)]
    public void CanGenerateGen9Eggs(string species, string ability, bool isMale, bool isShiny)
    {
        // Arrange
        var sav = AutoLegalityWrapper.GetTrainerInfo<PK9>();
        var set = new ShowdownSet($@"Egg ({species}) ({(isMale ? "M" : "F")})
Ability: {ability}
Ball: Poke Ball
Shiny: {(isShiny ? "Yes" : "No")}");

        var template = AutoLegalityWrapper.GetTemplate(set);

        // Act
        var egg = sav.GenerateEgg(template, out var result);

        // Assert
        result.Should().Be(LegalizationResult.Regenerated, "egg generation should succeed");
        egg.Should().NotBeNull();
        egg.Should().BeOfType<PK9>();

        var pk9 = (PK9)egg;
        pk9.IsEgg.Should().BeTrue("the Pokémon should be an egg");
        pk9.Species.Should().Be((ushort)Enum.Parse<Species>(species));
        pk9.EggLocation.Should().Be(Locations.Picnic9, "Gen 9 eggs come from picnics");
        pk9.Version.Should().Be(0, "unhatched Gen 9 eggs should have Version = 0");
        pk9.IsShiny.Should().Be(isShiny);

        var la = new LegalityAnalysis(pk9);
        la.Valid.Should().BeTrue($"the egg should be legal:\n{la.Report()}");
    }

    [Theory]
    [InlineData("Grookey", "Grassy Surge", true, false)]
    [InlineData("Scorbunny", "Libero", true, true)]
    [InlineData("Sobble", "Torrent", false, true)]
    public void CanGenerateGen8Eggs(string species, string ability, bool isMale, bool isShiny)
    {
        // Arrange
        var sav = AutoLegalityWrapper.GetTrainerInfo<PK8>();
        var set = new ShowdownSet($@"Egg ({species}) ({(isMale ? "M" : "F")})
Ability: {ability}
Ball: Poke Ball
Shiny: {(isShiny ? "Yes" : "No")}");

        var template = AutoLegalityWrapper.GetTemplate(set);

        // Act
        var egg = sav.GenerateEgg(template, out var result);

        // Assert
        result.Should().Be(LegalizationResult.Regenerated, "egg generation should succeed");
        egg.Should().NotBeNull();
        egg.Should().BeOfType<PK8>();

        var pk8 = (PK8)egg;
        pk8.IsEgg.Should().BeTrue("the Pokémon should be an egg");
        pk8.Species.Should().Be((ushort)Enum.Parse<Species>(species));
        pk8.IsShiny.Should().Be(isShiny);

        var la = new LegalityAnalysis(pk8);
        la.Valid.Should().BeTrue($"the egg should be legal:\n{la.Report()}");
    }

    [Theory]
    [InlineData("Turtwig", "Shell Armor", true, false)]
    [InlineData("Chimchar", "Iron Fist", true, true)]
    [InlineData("Piplup", "Competitive", false, false)]
    public void CanGenerateBDSPEggs(string species, string ability, bool isMale, bool isShiny)
    {
        // Arrange
        var sav = AutoLegalityWrapper.GetTrainerInfo<PB8>();
        var set = new ShowdownSet($@"Egg ({species}) ({(isMale ? "M" : "F")})
Ability: {ability}
Ball: Poke Ball
Shiny: {(isShiny ? "Yes" : "No")}");

        var template = AutoLegalityWrapper.GetTemplate(set);

        // Act
        var egg = sav.GenerateEgg(template, out var result);

        // Assert
        result.Should().Be(LegalizationResult.Regenerated, "egg generation should succeed");
        egg.Should().NotBeNull();
        egg.Should().BeOfType<PB8>();

        var pb8 = (PB8)egg;
        pb8.IsEgg.Should().BeTrue("the Pokémon should be an egg");
        pb8.Species.Should().Be((ushort)Enum.Parse<Species>(species));
        pb8.IsShiny.Should().Be(isShiny);

        var la = new LegalityAnalysis(pb8);
        la.Valid.Should().BeTrue($"the egg should be legal:\n{la.Report()}");
    }

    [Fact]
    public void EggShouldHaveCorrectFriendship()
    {
        // Arrange
        var sav = AutoLegalityWrapper.GetTrainerInfo<PK9>();
        var set = new ShowdownSet(@"Egg (Sprigatito) (M)
Ability: Overgrow
Ball: Poke Ball");

        var template = AutoLegalityWrapper.GetTemplate(set);

        // Act
        var egg = sav.GenerateEgg(template, out var result);

        // Assert
        result.Should().Be(LegalizationResult.Regenerated);
        egg.Should().NotBeNull();

        var pk9 = (PK9)egg;
        pk9.OriginalTrainerFriendship.Should().BeGreaterOrEqualTo(1, "egg should have minimum hatch cycles");
        pk9.OriginalTrainerFriendship.Should().BeLessOrEqualTo(pk9.PersonalInfo.HatchCycles, "egg friendship should not exceed species hatch cycles");
    }

    [Fact]
    public void EggShouldHaveEggMoves()
    {
        // Arrange
        var sav = AutoLegalityWrapper.GetTrainerInfo<PK9>();
        var set = new ShowdownSet(@"Egg (Sprigatito) (M)
Ability: Overgrow
Ball: Poke Ball
- Scratch
- Tail Whip
- Leafage");

        var template = AutoLegalityWrapper.GetTemplate(set);

        // Act
        var egg = sav.GenerateEgg(template, out var result);

        // Assert
        result.Should().Be(LegalizationResult.Regenerated);
        egg.Should().NotBeNull();

        var pk9 = (PK9)egg;
        pk9.Move1.Should().NotBe(0, "egg should have at least one move");

        var la = new LegalityAnalysis(pk9);
        la.Valid.Should().BeTrue($"the egg with moves should be legal:\n{la.Report()}");
    }

    [Fact]
    public void EggNicknameShouldBeCorrect()
    {
        // Arrange
        var sav = AutoLegalityWrapper.GetTrainerInfo<PK9>();
        var set = new ShowdownSet(@"Egg (Sprigatito) (M)
Ability: Overgrow
Ball: Poke Ball");

        var template = AutoLegalityWrapper.GetTemplate(set);

        // Act
        var egg = sav.GenerateEgg(template, out var result);

        // Assert
        result.Should().Be(LegalizationResult.Regenerated);
        egg.Should().NotBeNull();

        var pk9 = (PK9)egg;
        pk9.Nickname.Should().Be("Egg", "unhatched eggs should be nicknamed 'Egg' in English");
        pk9.IsNicknamed.Should().BeTrue("eggs should have the nickname flag set");
    }
}
