using Game.Application;
using Game.Core.Model;
using Game.Core.Model.Skills;

namespace Game.Tests;

public sealed class SkillMaxLevelPolicyTests
{
    [Fact]
    public void GetMaxLevel_UsesDefaultBaseLevelAndProfileBonus()
    {
        var externalSkill = TestContentFactory.CreateExternalSkill("dragon_palm");
        var internalSkill = TestContentFactory.CreateInternalSkill("yijinjing");
        var profile = new GameProfile();
        profile.AddSkillMaxLevelBonus(externalSkill.Id, 3);
        var policy = new SkillMaxLevelPolicy(profile: profile);

        Assert.Equal(13, policy.GetMaxLevel(externalSkill));
        Assert.Equal(10, policy.GetMaxLevel(internalSkill));
    }

    [Fact]
    public async Task MaxLevelCommand_AtHighRoundAddsOnlyItsOwnLevels()
    {
        // legacy ModData.AddSkillMaxLevel 会把当时的周目加成一起写进永久上限; 现在只记录指令本身的增量,
        // 周目加成只在计算当前有效上限时叠加.
        var externalSkill = TestContentFactory.CreateExternalSkill("dragon_palm");
        var state = new GameState();
        state.Adventure.SetRound(6);
        var session = new GameSession(state, TestContentFactory.CreateRepository(externalSkills: [externalSkill]));

        await session.StoryService.CommandDispatcher.ExecuteCallAsync(
            new Game.Expressions.ExpressionParser().ParseCall("maxlevel('dragon_palm', 2, 'key')"));

        Assert.Equal(2, session.Profile.GetSkillMaxLevelBonus("dragon_palm"));
        Assert.Equal(10 + 2 + 6 / 2, session.SkillMaxLevelPolicy.GetMaxLevel(externalSkill));
        Assert.Equal(12, session.SkillMaxLevelPolicy.GetExternalSkillMaxLevelWithoutRoundBonus("dragon_palm"));
    }

    [Fact]
    public void GetMaxLevel_ClampsProfileBonusToAbsoluteCap()
    {
        var externalSkill = TestContentFactory.CreateExternalSkill("dragon_palm");
        var profile = new GameProfile();
        profile.AddSkillMaxLevelBonus(externalSkill.Id, 50);
        var policy = new SkillMaxLevelPolicy(
            new GameConfig
            {
                BaseExternalSkillMaxLevel = 10,
                AbsoluteSkillMaxLevel = 20,
            },
            profile);

        Assert.Equal(20, policy.GetMaxLevel(externalSkill));
    }

    [Fact]
    public void GetMaxLevel_ForSkillInstanceDoesNotReturnLessThanCurrentLevel()
    {
        var externalSkill = TestContentFactory.CreateExternalSkill("dragon_palm");
        var owner = TestContentFactory.CreateCharacterInstance(
            "hero",
            TestContentFactory.CreateCharacterDefinition("hero"));
        var instance = new ExternalSkillInstance(externalSkill, owner, active: true)
        {
            Level = 12,
        };
        var policy = new SkillMaxLevelPolicy(
            new GameConfig
            {
                BaseExternalSkillMaxLevel = 10,
                AbsoluteSkillMaxLevel = 20,
            });

        Assert.Equal(10, policy.GetMaxLevel(externalSkill));
        Assert.Equal(12, policy.GetMaxLevel(instance));
    }

    [Fact]
    public void GetMaxLevel_AddsRoundBonusAndClampsToAbsoluteCap()
    {
        var externalSkill = TestContentFactory.CreateExternalSkill("dragon_palm");
        var profile = new GameProfile();
        profile.AddSkillMaxLevelBonus(externalSkill.Id, 6);
        var config = new GameConfig
        {
            BaseExternalSkillMaxLevel = 10,
            RoundsPerMaxSkillLevelIncrease = 2,
            AbsoluteSkillMaxLevel = 15,
        };

        Assert.Equal(10, new SkillMaxLevelPolicy(config, round: 1).GetMaxLevel(externalSkill));
        Assert.Equal(11, new SkillMaxLevelPolicy(config, round: 2).GetMaxLevel(externalSkill));
        Assert.Equal(12, new SkillMaxLevelPolicy(config, round: 5).GetMaxLevel(externalSkill));
        Assert.Equal(15, new SkillMaxLevelPolicy(config, profile, round: 5).GetMaxLevel(externalSkill));
    }
}
