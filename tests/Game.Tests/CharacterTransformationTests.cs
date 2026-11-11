using Game.Application;
using Game.Core.Definitions;
using Game.Core.Definitions.Skills;
using Game.Core.Model;

namespace Game.Tests;

public sealed class CharacterTransformationTests
{
    [Fact]
    public async Task Transform_RebuildsFromNewTemplateAndKeepsGrowthAndRosterSlot()
    {
        var palm = TestContentFactory.CreateExternalSkill("palm");
        var finger = TestContentFactory.CreateExternalSkill("finger");
        var learned = TestContentFactory.CreateExternalSkill("learned");
        var heart = TestContentFactory.CreateInternalSkill("heart");
        var oldTalent = new TalentDefinition { Id = "old_talent", Name = "old_talent" };
        var newTalent = new TalentDefinition { Id = "new_talent", Name = "new_talent" };
        var low = TestContentFactory.CreateCharacterDefinition(
            "程英.初级",
            stats: new Dictionary<StatType, int> { [StatType.Bili] = 40, [StatType.Shenfa] = 10 },
            externalSkills: [new InitialExternalSkillEntryDefinition(palm, 6)],
            internalSkills: [new InitialInternalSkillEntryDefinition(heart, 2, null, true)],
            talents: [oldTalent],
            level: 3) with { Name = "程英" };
        var high = TestContentFactory.CreateCharacterDefinition(
            "程英.高级",
            stats: new Dictionary<StatType, int> { [StatType.Bili] = 20, [StatType.Shenfa] = 50, [StatType.MaxHp] = 300 },
            externalSkills: [new InitialExternalSkillEntryDefinition(palm, 4), new InitialExternalSkillEntryDefinition(finger, 8)],
            talents: [newTalent],
            level: 10) with { Name = "程英" };
        var other = TestContentFactory.CreateCharacterDefinition("other");
        var session = new GameSession(
            new GameState(),
            TestContentFactory.CreateRepository(
                characters: [low, high, other],
                externalSkills: [palm, finger, learned],
                internalSkills: [heart],
                talents: [oldTalent, newTalent]),
            config: new GameConfig { MaximizeNewPartyCharacterSkills = false });
        var dispatcher = session.StoryService.CommandDispatcher;
        var parser = new ExpressionParser();
        await dispatcher.ExecuteCallAsync(parser.ParseCall("join('程英', '程英.初级')"));
        await dispatcher.ExecuteCallAsync(parser.ParseCall("join('other')"));
        await dispatcher.ExecuteCallAsync(parser.ParseCall("learn('程英', 'learned', 7)"));
        var before = session.State.Party.GetMember("程英");
        before.Name = "英妹";
        before.GrantStatPoints(4);

        await dispatcher.ExecuteCallAsync(parser.ParseCall("transform('程英', '程英.高级')"));

        var after = session.State.Party.GetMember("程英");
        Assert.NotSame(before, after);
        Assert.Equal("程英.高级", after.Definition.Id);
        Assert.Equal(["程英", "other"], session.State.Party.Members.Select(member => member.Id).ToArray());
        Assert.Equal("英妹", after.Name);
        Assert.Equal(10, after.Level);
        Assert.Equal(40, after.GetBaseStat(StatType.Bili));
        Assert.Equal(50, after.GetBaseStat(StatType.Shenfa));
        Assert.Equal(4, after.UnspentStatPoints);
        Assert.Equal(6, after.GetExternalSkillLevel("palm"));
        Assert.Equal(8, after.GetExternalSkillLevel("finger"));
        Assert.Equal(7, after.GetExternalSkillLevel("learned"));
        Assert.Equal("heart", after.EquippedInternalSkillId);
        Assert.True(after.HasTalent("old_talent"));
        Assert.True(after.HasTalent("new_talent"));
        Assert.Equal(300, after.CurrentHp);
    }

    [Fact]
    public async Task Transform_RejectsCharactersOutsideTheRoster()
    {
        var target = TestContentFactory.CreateCharacterDefinition("target");
        var session = new GameSession(new GameState(), TestContentFactory.CreateRepository(characters: [target]));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await session.StoryService.CommandDispatcher.ExecuteCallAsync(
                new ExpressionParser().ParseCall("transform('nobody', 'target')")));
    }
}
