using Game.Core.Affix;
using Game.Core.Definitions;
using Game.Core.Definitions.Skills;
using Game.Core.Model;
using Game.Core.Model.Character;

namespace Game.Tests;

public sealed class CharacterTalentTests
{
    [Fact]
    public void CreateInitial_IgnoresDefinedWuxueStat()
    {
        var definition = TestContentFactory.CreateCharacterDefinition(
            "hero",
            new Dictionary<StatType, int>
            {
                [StatType.Wuxue] = 9999,
                [StatType.Bili] = 10,
            });

        var character = TestContentFactory.CreateCharacterInstance("char_001", definition);

        Assert.Equal(0, character.GetBaseStat(StatType.Wuxue));
        Assert.Equal(10, character.GetBaseStat(StatType.Bili));
    }

    [Fact]
    public void TalentPointCapacity_UsesDefaultInnatePointsAndLevelGrowth()
    {
        var definition = TestContentFactory.CreateCharacterDefinition("hero", level: 3);
        var growTemplate = TestContentFactory.CreateGrowTemplate(
            CharacterExperienceProgression.DefaultGrowTemplateId,
            new Dictionary<StatType, int>
            {
                [StatType.Wuxue] = 8,
            });
        var character = TestContentFactory.CreateCharacterInstance("char_001", definition);

        var capacity = CharacterTalentPointCalculator.CalculateCapacity(character, growTemplate);

        Assert.Equal(44, capacity);
    }

    [Fact]
    public void CreateInitial_RebuildsSnapshotForEquippedInternalSkillGrantedTalent()
    {
        var radiance = new TalentDefinition
        {
            Id = "radiance",
            Name = "Radiance",
        };
        var grantRadiance = new GrantTalentAffix("radiance");
        ContentReferenceGraph.Resolve(grantRadiance, TestContentFactory.CreateRepository(talents: [radiance]));
        var internalSkill = TestContentFactory.CreateInternalSkill(
            "inner_light",
            affixes:
            [
                new SkillAffixDefinition(
                    grantRadiance,
                    MinimumLevel: 10,
                    RequiresEquippedInternalSkill: true),
            ]);
        var definition = TestContentFactory.CreateCharacterDefinition(
            "hero_monk",
            internalSkills:
            [
                new InitialInternalSkillEntryDefinition(
                    internalSkill,
                    Level: 10,
                    Equipped: true),
            ],
            talents: []);

        var character = TestContentFactory.CreateCharacterInstance("char_001", definition);

        Assert.True(character.HasTalent("radiance"));
        Assert.Contains(character.PossessedTalents, talent => talent.Id == "radiance");
    }

    [Fact]
    public void CreateInitial_RebuildsSnapshotForLearnedTalentAffixes()
    {
        var battleFocus = new TalentDefinition
        {
            Id = "battle_focus",
            Name = "Battle Focus",
            Affixes =
            [
                new StatModifierAffix(StatType.Fuyuan, ModifierValue.Add(7)),
            ],
        };
        var definition = TestContentFactory.CreateCharacterDefinition(
            "hero_knight",
            new Dictionary<StatType, int>
            {
                [StatType.Fuyuan] = 10,
            },
            talents: [battleFocus]);

        var character = TestContentFactory.CreateCharacterInstance("char_001", definition);

        Assert.True(character.HasTalent("battle_focus"));
        Assert.Equal(17, character.GetStat(StatType.Fuyuan));
    }

    [Fact]
    public void RebuildSnapshot_UpperTalentReplacesLearnedLowerTalent()
    {
        var lower = new TalentDefinition
        {
            Id = "qiankun_shift",
            Name = "乾坤大挪移",
            Affixes =
            [
                new StatModifierAffix(StatType.Fuyuan, ModifierValue.Add(7)),
            ],
        };
        var upper = new TalentDefinition
        {
            Id = "qiankun_shift_aoyi",
            Name = "乾坤大挪移奥义",
            ReplaceTalentIds = ["qiankun_shift"],
            Affixes =
            [
                new StatModifierAffix(StatType.Dingli, ModifierValue.Add(4)),
            ],
        };
        var definition = TestContentFactory.CreateCharacterDefinition(
            "hero_knight",
            new Dictionary<StatType, int>
            {
                [StatType.Fuyuan] = 10,
                [StatType.Dingli] = 10,
            },
            talents: [lower, upper]);

        var character = TestContentFactory.CreateCharacterInstance("char_001", definition);

        Assert.True(character.HasTalent("qiankun_shift"));
        Assert.True(character.HasTalent("qiankun_shift_aoyi"));
        Assert.False(character.HasActiveTalent("qiankun_shift"));
        Assert.True(character.HasActiveTalent("qiankun_shift_aoyi"));
        Assert.Equal(["qiankun_shift_aoyi"], character.ActiveTalents.Select(static talent => talent.Id));
        Assert.Equal(10, character.GetStat(StatType.Fuyuan));
        Assert.Equal(14, character.GetStat(StatType.Dingli));
    }

    [Fact]
    public void RebuildSnapshot_UpperTalentBlocksGrantedLowerTalent()
    {
        var lower = new TalentDefinition
        {
            Id = "qiankun_shift",
            Name = "乾坤大挪移",
            Affixes =
            [
                new StatModifierAffix(StatType.Fuyuan, ModifierValue.Add(7)),
            ],
        };
        var upper = new TalentDefinition
        {
            Id = "qiankun_shift_aoyi",
            Name = "乾坤大挪移奥义",
            ReplaceTalentIds = ["qiankun_shift"],
            Affixes =
            [
                new StatModifierAffix(StatType.Dingli, ModifierValue.Add(4)),
            ],
        };
        var repository = TestContentFactory.CreateRepository(talents: [lower, upper]);
        var grantLower = new GrantTalentAffix("qiankun_shift");
        ContentReferenceGraph.Resolve(grantLower, repository);
        var skill = TestContentFactory.CreateExternalSkill(
            "grant_lower",
            affixes:
            [
                new SkillAffixDefinition(grantLower),
            ]);
        var definition = TestContentFactory.CreateCharacterDefinition(
            "hero_knight",
            new Dictionary<StatType, int>
            {
                [StatType.Fuyuan] = 10,
                [StatType.Dingli] = 10,
            },
            externalSkills: [new InitialExternalSkillEntryDefinition(skill, Level: 1)],
            talents: [upper]);

        var character = TestContentFactory.CreateCharacterInstance("char_001", definition);

        Assert.True(character.HasTalent("qiankun_shift"));
        Assert.True(character.HasTalent("qiankun_shift_aoyi"));
        Assert.False(character.HasActiveTalent("qiankun_shift"));
        Assert.True(character.HasActiveTalent("qiankun_shift_aoyi"));
        Assert.Equal(["qiankun_shift_aoyi"], character.ActiveTalents.Select(static talent => talent.Id));
        Assert.Equal(10, character.GetStat(StatType.Fuyuan));
        Assert.Equal(14, character.GetStat(StatType.Dingli));
    }

    [Fact]
    public void RebuildSnapshot_DoesNotExpandGrantTalentAffixBlockedBySkillCondition()
    {
        var battleFocus = new TalentDefinition
        {
            Id = "battle_focus",
            Name = "Battle Focus",
            Affixes =
            [
                new StatModifierAffix(StatType.Fuyuan, ModifierValue.Add(7)),
            ],
        };
        var grantAffix = new GrantTalentAffix("battle_focus");
        ContentReferenceGraph.Resolve(grantAffix, TestContentFactory.CreateRepository(talents: [battleFocus]));
        var skill = TestContentFactory.CreateExternalSkill(
            "late_focus",
            affixes:
            [
                new SkillAffixDefinition(
                    grantAffix,
                    MinimumLevel: 5),
            ]);
        var character = TestContentFactory.CreateCharacterInstance(
            "char_001",
            TestContentFactory.CreateCharacterDefinition(
                "hero_knight",
                new Dictionary<StatType, int>
                {
                    [StatType.Fuyuan] = 10,
                },
                externalSkills: [new InitialExternalSkillEntryDefinition(skill, Level: 1)]));

        character.RebuildSnapshot();

        Assert.Empty(character.PossessedTalents);
        Assert.False(character.HasTalent("battle_focus"));
        Assert.Equal(10, character.GetStat(StatType.Fuyuan));
    }
}
