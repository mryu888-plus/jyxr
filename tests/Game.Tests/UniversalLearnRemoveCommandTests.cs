using Game.Application;
using Game.Core.Definitions;
using Game.Core.Definitions.Skills;
using Game.Core.Model;
using Game.Core.Model.Skills;

namespace Game.Tests;

public sealed class UniversalLearnRemoveCommandTests
{
    [Fact]
    public async Task ForgetSkill_FollowsPlayerRulesSeparatelyFromStoryRemove()
    {
        var external = TestContentFactory.CreateExternalSkill("external");
        var equipped = TestContentFactory.CreateInternalSkill("equipped");
        var spare = TestContentFactory.CreateInternalSkill("spare");
        var special = CreateSpecialSkill("special");
        var session = CreateSession(externalSkills: [external], internalSkills: [equipped, spare], specialSkills: [special]);
        var dispatcher = session.StoryService.CommandDispatcher;
        var parser = new ExpressionParser();
        await dispatcher.ExecuteCallAsync(parser.ParseCall("learn('hero', 'external', 1)"));
        await dispatcher.ExecuteCallAsync(parser.ParseCall("learn('hero', 'equipped', 1)"));
        await dispatcher.ExecuteCallAsync(parser.ParseCall("learn('hero', 'spare', 1)"));
        await dispatcher.ExecuteCallAsync(parser.ParseCall("learn('hero', 'special', 1)"));
        var hero = session.State.Party.GetMember("hero");
        session.CharacterService.EquipInternalSkill("hero", "equipped");
        var changed = new List<CharacterChangedEvent>();
        using var subscription = session.Events.Subscribe<CharacterChangedEvent>(changed.Add);

        var equippedSkill = hero.GetInternalSkills().Single(skill => skill.Id == "equipped");
        Assert.Equal(SkillForgetAvailability.EquippedInternalSkill, CharacterService.GetForgetAvailability(equippedSkill));
        Assert.Throws<InvalidOperationException>(() => session.CharacterService.ForgetSkill(equippedSkill));

        Assert.True(session.CharacterService.ForgetSkill(hero.GetInternalSkills().Single(skill => skill.Id == "spare")));
        Assert.True(session.CharacterService.ForgetSkill(hero.GetExternalSkills().Single()));
        Assert.True(session.CharacterService.ForgetSkill(hero.GetSpecialSkills().Single()));

        Assert.Null(hero.GetInternalSkillLevel("spare"));
        Assert.Null(hero.GetExternalSkillLevel("external"));
        Assert.Empty(hero.GetSpecialSkills());
        Assert.Equal(1, hero.GetInternalSkillLevel("equipped"));
        Assert.Equal(3, changed.Count);

        // 剧情强制移除不受玩家规则限制.
        await dispatcher.ExecuteCallAsync(parser.ParseCall("remove('hero', 'equipped')"));
        Assert.Null(hero.GetInternalSkillLevel("equipped"));
    }

    [Fact]
    public async Task CommandsResolveAllCategoriesAndApplyLevelsOnlyToLevelledSkills()
    {
        var external = TestContentFactory.CreateExternalSkill("external");
        var internalSkill = TestContentFactory.CreateInternalSkill("internal");
        var special = CreateSpecialSkill("special");
        var talent = new TalentDefinition { Id = "talent", Name = "talent" };
        var session = CreateSession(externalSkills: [external], internalSkills: [internalSkill], specialSkills: [special], talents: [talent]);
        var dispatcher = session.StoryService.CommandDispatcher;
        var parser = new ExpressionParser();

        await dispatcher.ExecuteCallAsync(parser.ParseCall("learn('hero', 'external', 4)"));
        await dispatcher.ExecuteCallAsync(parser.ParseCall("learn('hero', 'internal', 5)"));
        await dispatcher.ExecuteCallAsync(parser.ParseCall("learn('hero', 'special', 7)"));
        await dispatcher.ExecuteCallAsync(parser.ParseCall("learn('hero', 'talent', 9)"));

        var hero = session.State.Party.GetMember("hero");
        Assert.Equal(4, hero.GetExternalSkillLevel("external"));
        Assert.Equal(5, hero.GetInternalSkillLevel("internal"));
        Assert.Contains(hero.GetSpecialSkills(), skill => skill.Definition.Id == "special");
        Assert.True(hero.HasLearnedTalent("talent"));

        foreach (var id in new[] { "external", "internal", "special", "talent" })
            await dispatcher.ExecuteCallAsync(parser.ParseCall($"remove('hero', '{id}')"));

        Assert.Null(hero.GetExternalSkillLevel("external"));
        Assert.Null(hero.GetInternalSkillLevel("internal"));
        Assert.DoesNotContain(hero.GetSpecialSkills(), skill => skill.Definition.Id == "special");
        Assert.False(hero.HasLearnedTalent("talent"));
    }

    [Fact]
    public async Task CommandsUseExternalFirstWhenDefinitionsShareAnId()
    {
        var external = TestContentFactory.CreateExternalSkill("shared");
        var talent = new TalentDefinition { Id = "shared", Name = "shared" };
        var session = CreateSession(externalSkills: [external], talents: [talent]);
        var hero = session.State.Party.GetMember("hero");
        var dispatcher = session.StoryService.CommandDispatcher;
        var parser = new ExpressionParser();

        await dispatcher.ExecuteCallAsync(parser.ParseCall("learn('hero', 'shared', 3)"));

        Assert.Equal(3, hero.GetExternalSkillLevel("shared"));
        Assert.False(hero.HasLearnedTalent("shared"));

        session.CharacterService.LearnTalent(hero, "shared");
        await dispatcher.ExecuteCallAsync(parser.ParseCall("remove('hero', 'shared')"));

        Assert.Null(hero.GetExternalSkillLevel("shared"));
        Assert.True(hero.HasLearnedTalent("shared"));
    }

    [Fact]
    public async Task CommandsRejectInvalidLevelUnknownTargetMissingCharacterAndOldSignature()
    {
        var external = TestContentFactory.CreateExternalSkill("external");
        var session = CreateSession(externalSkills: [external]);
        var dispatcher = session.StoryService.CommandDispatcher;
        var parser = new ExpressionParser();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await dispatcher.ExecuteCallAsync(parser.ParseCall("learn('hero', 'external', 0)")));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await dispatcher.ExecuteCallAsync(parser.ParseCall("learn('hero', 'missing')")));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await dispatcher.ExecuteCallAsync(parser.ParseCall("remove('hero', 'missing')")));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await dispatcher.ExecuteCallAsync(parser.ParseCall("learn('missing_hero', 'external')")));
        await Assert.ThrowsAsync<ExpressionBindingException>(async () =>
            await dispatcher.ExecuteCallAsync(parser.ParseCall("learn('external', 'hero', 'external')")));
        await Assert.ThrowsAsync<ExpressionBindingException>(async () =>
            await dispatcher.ExecuteCallAsync(parser.ParseCall("remove('external', 'hero', 'external')")));
    }

    [Fact]
    public async Task ConsoleThinTokensInvokeUniversalCommands()
    {
        var external = TestContentFactory.CreateExternalSkill("external");
        var session = CreateSession(externalSkills: [external]);

        await session.StoryService.CommandLine.ExecuteAsync("learn hero external 6");
        Assert.Equal(6, session.State.Party.GetMember("hero").GetExternalSkillLevel("external"));

        await session.StoryService.CommandLine.ExecuteAsync("remove hero external");
        Assert.Null(session.State.Party.GetMember("hero").GetExternalSkillLevel("external"));
    }

    [Fact]
    public async Task MapActionUsesTheSameUniversalCommand()
    {
        var external = TestContentFactory.CreateExternalSkill("external");
        var map = new MapDefinition
        {
            Id = "inn",
            Name = "inn",
            Kind = MapKind.Small,
            Locations =
            [
                new MapLocationDefinition
                {
                    Id = "teacher",
                    Events =
                    [
                        new MapEventDefinition
                        {
                            Id = "inn-teacher-learn",
                            Action = new ExpressionParser().ParseCall("learn('hero', 'external', 2)", "map learn test"),
                        },
                    ],
                },
            ],
        };
        var session = CreateSession(externalSkills: [external], maps: [map]);

        var location = session.MapService.EnterMap("inn").Locations.Single();
        var interaction = session.MapService.InteractWithLocation(location);
        await session.StoryService.CommandDispatcher.ExecuteCallAsync(interaction.Command!);

        Assert.Equal(2, session.State.Party.GetMember("hero").GetExternalSkillLevel("external"));
    }

    [Fact]
    public async Task UpgradeCommandsSupportExplicitCategoriesAndAutomaticConvenienceCall()
    {
        var external = TestContentFactory.CreateExternalSkill("external");
        var internalSkill = TestContentFactory.CreateInternalSkill("internal");
        var session = CreateSession(externalSkills: [external], internalSkills: [internalSkill]);
        var parser = new ExpressionParser();
        var dispatcher = session.StoryService.CommandDispatcher;

        await dispatcher.ExecuteCallAsync(parser.ParseCall("learn_external('hero', 'external')"));
        await dispatcher.ExecuteCallAsync(parser.ParseCall("learn_internal('hero', 'internal', 2)"));
        await dispatcher.ExecuteCallAsync(parser.ParseCall("upgrade_external('hero', 'external', 2)"));
        await dispatcher.ExecuteCallAsync(parser.ParseCall("upgrade_internal('hero', 'internal', 3)"));
        await dispatcher.ExecuteCallAsync(parser.ParseCall("upgrade_skill('hero', 'external', 2)"));
        await session.StoryService.CommandLine.ExecuteAsync("upgrade_skill hero internal 1");

        var hero = session.State.Party.GetMember("hero");
        Assert.Equal(5, hero.GetExternalSkillLevel("external"));
        Assert.Equal(6, hero.GetInternalSkillLevel("internal"));
    }

    [Fact]
    public async Task AutomaticUpgradeUsesExternalFirstAndRejectsUnknownOrInvalidLevels()
    {
        var external = TestContentFactory.CreateExternalSkill("shared");
        var internalSkill = TestContentFactory.CreateInternalSkill("shared");
        var session = CreateSession(externalSkills: [external], internalSkills: [internalSkill]);
        var parser = new ExpressionParser();
        var dispatcher = session.StoryService.CommandDispatcher;

        await dispatcher.ExecuteCallAsync(parser.ParseCall("learn_external('hero', 'shared')"));
        await dispatcher.ExecuteCallAsync(parser.ParseCall("learn_internal('hero', 'shared')"));
        await dispatcher.ExecuteCallAsync(parser.ParseCall("upgrade_skill('hero', 'shared', 2)"));

        var hero = session.State.Party.GetMember("hero");
        Assert.Equal(3, hero.GetExternalSkillLevel("shared"));
        Assert.Equal(1, hero.GetInternalSkillLevel("shared"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await dispatcher.ExecuteCallAsync(parser.ParseCall("upgrade_skill('hero', 'missing')")));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await dispatcher.ExecuteCallAsync(parser.ParseCall("upgrade_external('hero', 'shared', 0)")));
    }

    private static GameSession CreateSession(
        IReadOnlyList<ExternalSkillDefinition>? externalSkills = null,
        IReadOnlyList<InternalSkillDefinition>? internalSkills = null,
        IReadOnlyList<SpecialSkillDefinition>? specialSkills = null,
        IReadOnlyList<TalentDefinition>? talents = null,
        IReadOnlyList<MapDefinition>? maps = null)
    {
        var hero = TestContentFactory.CreateCharacterDefinition("hero");
        var session = new GameSession(
            new GameState(),
            TestContentFactory.CreateRepository(
                characters: [hero],
                externalSkills: externalSkills,
                internalSkills: internalSkills,
                specialSkills: specialSkills,
                talents: talents,
                maps: maps));
        session.PartyService.Join("hero");
        return session;
    }

    private static SpecialSkillDefinition CreateSpecialSkill(string id) =>
        new(
            id,
            id,
            "",
            SpecialSkillIntent.Support,
            "",
            0,
            new SkillCostDefinition(0, 0),
            null,
            "",
            "",
            null,
            []);
}
