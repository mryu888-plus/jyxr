using Game.Application;

namespace Game.Tests;

public sealed class BattleEncounterOutcomeTests
{
    [Fact]
    public void Default_LoseIsGameOverAndWinDoesNothing()
    {
        var projection = BattleResultProjection.FromCommandArguments("", " ");

        Assert.Equal(BattleEncounterFollowUpKind.None, projection.Resolve(isWin: true).Kind);
        Assert.Equal(BattleEncounterFollowUpKind.GameOver, projection.Resolve(isWin: false).Kind);
    }

    [Fact]
    public void ConfiguredStories_RunInsteadOfDefaults()
    {
        var projection = BattleResultProjection.FromCommandArguments("bandits_defeated", "robbed");

        Assert.Equal(BattleEncounterFollowUp.RunStory("bandits_defeated"), projection.Resolve(isWin: true));
        Assert.Equal(BattleEncounterFollowUp.RunStory("robbed"), projection.Resolve(isWin: false));
    }
}

public sealed class BattleEncounterContentTests
{
    [Fact]
    public void MapBattleAction_AcceptsExistingResultStories()
    {
        var repository = new Game.Content.Loading.JsonContentLoader().LoadFromPackage(CreatePackage("battle('ambush', 'won', '')"));

        Assert.True(repository.Battles.ContainsKey("ambush"));
    }

    [Fact]
    public void MapBattleAction_RejectsMissingResultStory()
    {
        var exception = Assert.ThrowsAny<Exception>(() =>
            new Game.Content.Loading.JsonContentLoader().LoadFromPackage(CreatePackage("battle('ambush', 'won', 'missing')")));

        Assert.Contains("missing result story 'missing'", exception.Message);
    }

    private static Game.Content.Loading.ContentPackage CreatePackage(string action)
    {
        var package = new Game.Content.Loading.ContentPackage
        {
            Battles =
            [
                new Game.Core.Definitions.BattleDefinition { Id = "ambush", Name = "ambush", Background = "battle_bg/test" },
            ],
            Maps =
            [
                new Game.Core.Definitions.MapDefinition
                {
                    Id = "road",
                    Name = "road",
                    Locations =
                    [
                        new Game.Core.Definitions.MapLocationDefinition
                        {
                            Id = "forward",
                            Events =
                            [
                                new Game.Core.Definitions.MapEventDefinition
                                {
                                    Id = "1",
                                    Action = new Game.Expressions.ExpressionParser().ParseCall(action),
                                },
                            ],
                        },
                    ],
                },
            ],
        };
        package.StoryScripts["test"] = Game.Core.Story.StoryScriptJson.Parse(
            """{"version":3,"segments":[{"name":"won","steps":[]}]}""");
        return package;
    }
}
