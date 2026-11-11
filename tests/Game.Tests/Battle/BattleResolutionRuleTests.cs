using System.Text.Json;
using Game.Content.Loading;
using Game.Core.Abstractions;
using Game.Core.Affix;
using Game.Core.Battle;
using Game.Core.Definitions;
using Game.Core.Definitions.Skills;
using Game.Core.Model;
using Game.Core.Model.Character;
using Game.Core.Model.Skills;
using Game.Core.Serialization;

namespace Game.Tests;

/// <summary>
/// 技能选择器、施法者自伤、方向技能、active buff 快照、Debuff 施加顺序、两阶段命中结算与回怒语义。
/// </summary>
public sealed class BattleResolutionRuleTests
{
    // ---- 方向技能不能对自身格施法 ----

    [Theory]
    [InlineData(SkillImpactType.Line)]
    [InlineData(SkillImpactType.Fan)]
    [InlineData(SkillImpactType.Cleave)]
    public void DirectionalImpact_OnCasterCell_HasNoCoverage(SkillImpactType impactType)
    {
        var position = new GridPosition(2, 2);

        Assert.Empty(BattleEngine.GetImpactPositions(position, position, impactType, impactSize: 2));
        Assert.True(SkillTargetingDefaults.IsDirectional(impactType));
    }

    [Fact]
    public void DirectionalImpact_UsesCasterToTargetDirection()
    {
        var positions = BattleEngine.GetImpactPositions(
            new GridPosition(2, 2),
            new GridPosition(2, 1),
            SkillImpactType.Cleave,
            impactSize: 1);

        Assert.Equal(
            new HashSet<GridPosition> { new(2, 1), new(1, 1), new(3, 1) },
            positions);
    }

    [Theory]
    [InlineData(WeaponType.Jianfa, null)]
    [InlineData(WeaponType.Quanzhang, SkillImpactType.Fan)]
    [InlineData(WeaponType.Daofa, null)]
    public void JsonLoader_RejectsSelfCastForDirectionalSkills(WeaponType weaponType, SkillImpactType? impactType)
    {
        var skill = TestContentFactory.CreateExternalSkill(
            "directional",
            type: weaponType,
            impactType: impactType,
            canCastAtSelf: true);

        var exception = Assert.ThrowsAny<Exception>(() =>
            new JsonContentLoader().LoadFromPackage(new ContentPackage { ExternalSkills = [skill] }));
        Assert.Contains("canCastAtSelf", exception.Message);
    }

    [Fact]
    public void JsonLoader_RejectsSelfCastFormInheritingDirectionalImpact()
    {
        var form = new FormSkillDefinition(
            "directional.form", "directional.form", "", null, 1, 0, new SkillCostDefinition(),
            new SkillTargetingDefinition(CanCastAtSelf: true), 0, "", "", []);
        var skill = TestContentFactory.CreateExternalSkill("directional", type: WeaponType.Jianfa, formSkills: [form]);

        var exception = Assert.ThrowsAny<Exception>(() =>
            new JsonContentLoader().LoadFromPackage(new ContentPackage { ExternalSkills = [skill] }));
        Assert.Contains("directional.form", exception.Message);
    }

    // ---- CanImpactSelf：施法者绕过友伤过滤 ----

    [Fact]
    public void CanImpactSelf_IncludesCasterEvenWhenFriendlyFireIsDisabled()
    {
        var skill = CreateSelfSpecial("self_burst", SpecialSkillIntent.Offensive, effects: []);
        var caster = CreateUnit("caster", 1, new GridPosition(1, 1), specialSkills: [skill]);
        var ally = CreateUnit("ally", 1, new GridPosition(1, 2));
        var state = new BattleState(new BattleGrid(4, 4), [caster, ally]);
        var instance = caster.Character.GetSpecialSkills().Single();

        var targets = BattleSkillTargeting.ResolveEffectiveTargets(
            state,
            caster,
            instance,
            new HashSet<GridPosition> { caster.Position, ally.Position });

        Assert.Equal(GameDifficulty.Normal, state.RuleSettings.Difficulty);
        Assert.Equal([caster.Id], targets.Select(static unit => unit.Id));
    }

    // ---- 技能选择器 ----

    [Fact]
    public void SkillSelector_MatchesWeaponTypeAndInheritsSourceForForms()
    {
        var form = new FormSkillDefinition(
            "sword.form", "sword.form", "", null, 1, 0, new SkillCostDefinition(),
            new SkillTargetingDefinition(CastSize: 1), 0, "", "", []);
        var sword = TestContentFactory.CreateExternalSkill("sword", type: WeaponType.Jianfa, castSize: 2, formSkills: [form]);
        var fist = TestContentFactory.CreateExternalSkill("fist", type: WeaponType.Quanzhang, castSize: 2);
        var talent = new TalentDefinition
        {
            Id = "sword_reach",
            Name = "sword_reach",
            Affixes =
            [
                new SkillTargetingModifierAffix(
                    new SkillSelectorDefinition(WeaponTypes: [WeaponType.Jianfa]),
                    SkillTargetingField.CastSize,
                    ModifierValue.Add(2)),
                new SkillTargetingModifierAffix(
                    new SkillSelectorDefinition(SkillKinds: [SkillKind.Form], SourceKinds: [SkillSourceKind.External]),
                    SkillTargetingField.CastSize,
                    ModifierValue.Add(10)),
            ],
        };
        var unit = CreateUnit(
            "unit",
            1,
            new GridPosition(0, 0),
            externalSkills:
            [
                new InitialExternalSkillEntryDefinition(sword, 1),
                new InitialExternalSkillEntryDefinition(fist, 1),
            ],
            talents: [talent]);
        var swordSkill = unit.Character.ExternalSkills.Single(skill => skill.Id == "sword");
        var formSkill = swordSkill.GetFormSkills().Single();

        Assert.Equal(SkillSourceKind.External, formSkill.SourceKind);
        Assert.Equal(4, BattleSkillTargeting.ResolveEffectiveCastSize(unit, swordSkill));
        Assert.Equal(2, BattleSkillTargeting.ResolveEffectiveCastSize(unit, unit.Character.ExternalSkills.Single(skill => skill.Id == "fist")));
        Assert.Equal(13, BattleSkillTargeting.ResolveEffectiveCastSize(unit, formSkill));
        Assert.Equal(13, SkillTargetingResolver.ResolveCastSize(formSkill));
    }

    [Theory]
    [InlineData(true, 8)]
    [InlineData(false, 5)]
    public void SkillTargetingModifiers_ApplyInContentOrder(bool specificFirst, int expected)
    {
        var skill = TestContentFactory.CreateExternalSkill("skill", castSize: 2);
        AffixDefinition specific = new SkillTargetingModifierAffix(
            new SkillSelectorDefinition(SourceSkillIds: ["skill"]),
            SkillTargetingField.CastSize,
            ModifierValue.Override(5));
        AffixDefinition global = new SkillTargetingModifierAffix(null, SkillTargetingField.CastSize, ModifierValue.Override(8));
        var talent = new TalentDefinition
        {
            Id = "ordered",
            Name = "ordered",
            Affixes = specificFirst ? [specific, global] : [global, specific],
        };
        var unit = CreateUnit(
            "unit",
            1,
            new GridPosition(0, 0),
            externalSkills: [new InitialExternalSkillEntryDefinition(skill, 1)],
            talents: [talent]);

        Assert.Equal(expected, BattleSkillTargeting.ResolveEffectiveCastSize(unit, unit.Character.ExternalSkills.Single()));
    }

    [Fact]
    public void SkillTargetingModifier_DeserializesSelector()
    {
        var talent = JsonSerializer.Deserialize<TalentDefinition>(
            """
            {
              "id": "reach",
              "name": "reach",
              "affixes": [
                {
                  "type": "skill_targeting_modifier",
                  "selector": {
                    "sourceSkillIds": ["sword"],
                    "weaponTypes": ["jianfa"],
                    "skillKinds": ["Form"],
                    "sourceKinds": ["external"]
                  },
                  "field": "cast_size",
                  "value": {"op": "add", "delta": 1}
                }
              ]
            }
            """,
            GameJson.Default)!;

        var modifier = Assert.IsType<SkillTargetingModifierAffix>(Assert.Single(talent.Affixes));
        Assert.Equal(["sword"], modifier.Selector!.SourceSkillIds);
        Assert.Equal([WeaponType.Jianfa], modifier.Selector.WeaponTypes);
        Assert.Equal([SkillKind.Form], modifier.Selector.SkillKinds);
        Assert.Equal([SkillSourceKind.External], modifier.Selector.SourceKinds);
    }

    // ---- active buff 快照 ----

    [Fact]
    public void ActiveBuffs_SnapshotIsReusedAndRefreshedOnBuffChanges()
    {
        var unit = CreateUnit("unit", 1, new GridPosition(0, 0), stats: new Dictionary<StatType, int> { [StatType.Bili] = 50 });
        var state = new BattleState(new BattleGrid(4, 4), [unit]);
        var mighty = new BuffDefinition
        {
            Id = "mighty",
            Name = "mighty",
            IsDebuff = false,
            Affixes = [new BuffLevelStatModifierAffix(StatType.Bili, AddPerLevel: 10)],
        };
        var buff = new BattleBuffInstance(mighty, level: 1, remainingTurns: 2, unit.Id, 0);

        var empty = unit.ActiveBuffs;
        Assert.Same(empty, unit.ActiveBuffs);
        Assert.True(unit.TryApplyBuff(buff));
        Assert.NotSame(empty, unit.ActiveBuffs);
        Assert.Same(unit.ActiveBuffs, unit.ActiveBuffs);
        Assert.Equal(60d, unit.GetStat(StatType.Bili));

        buff.Strengthen(levelDelta: 2, turnDelta: 0);
        Assert.Equal(80d, unit.GetStat(StatType.Bili));

        var beforeRemoval = unit.ActiveBuffs;
        Assert.Single(unit.RemoveBuffs(static candidate => candidate.Definition.Id == "mighty"));
        Assert.NotSame(beforeRemoval, unit.ActiveBuffs);
        Assert.Empty(unit.ActiveBuffs);
        buff.Strengthen(levelDelta: 5, turnDelta: 0);
        Assert.Null(unit.TryGetBuff("mighty"));
        Assert.Equal(50d, unit.GetStat(StatType.Bili));
        Assert.NotNull(state);
    }

    // ---- Debuff 施加顺序 ----

    [Fact]
    public void ResistedDebuff_IsSilentAndSkipsBeforeBuffAppliedHooks()
    {
        var poison = new BuffDefinition { Id = "venom", Name = "venom", IsDebuff = true };
        var skill = TestContentFactory.CreateExternalSkill(
            "venom_strike",
            powerBase: 1,
            impactType: SkillImpactType.Single,
            impactSize: 0,
            castSize: 3,
            buffs: [new SkillBuffDefinition(poison, level: 1, duration: 3, chance: 100)]);
        var enhancer = new TalentDefinition
        {
            Id = "enhancer",
            Name = "enhancer",
            Affixes =
            [
                new HookAffix
                {
                    Timing = HookTiming.BeforeBuffApplied,
                    Effects = [new StrengthenContextBuffBattleHookEffectDefinition(LevelDelta: 1)],
                    FloatText = new BattleFloatTextDefinition { Text = "enhanced" },
                },
            ],
        };
        var hero = CreateUnit(
            "hero",
            1,
            new GridPosition(0, 0),
            stats: new Dictionary<StatType, int> { [StatType.Quanzhang] = 100, [StatType.Bili] = 100 },
            externalSkills: [new InitialExternalSkillEntryDefinition(skill, 1)],
            talents: [enhancer]);
        var enemy = CreateUnit(
            "enemy",
            2,
            new GridPosition(1, 0),
            maxHp: 1000,
            stats: new Dictionary<StatType, int> { [StatType.AntiDebuff] = 1 });
        hero.ActionGauge = 100;
        var state = new BattleState(new BattleGrid(4, 4), [hero, enemy]);
        var engine = new BattleEngine(random: new FixedRandomService(0d));
        engine.BeginAction(state, hero.Id);

        var result = engine.CastSkill(state, hero.Id, hero.Character.GetExternalSkills().Single(), enemy.Position);

        Assert.True(result.Success);
        Assert.False(enemy.HasBuff("venom"));
        Assert.DoesNotContain(result.Messages.OfType<BattleFact>(), static fact => fact.Kind == BattleFactKind.BuffApplied);
        Assert.DoesNotContain(result.Messages, static message => message is BattleCue { Kind: BattleCueKind.FloatTextRequested });
        Assert.DoesNotContain(result.Messages.OfType<BattleTrace>(), static trace => trace.Timing == HookTiming.BeforeBuffApplied);
    }

    // ---- 两阶段命中结算 ----

    [Theory]
    [InlineData(1, false)]
    [InlineData(0, true)]
    public void DodgeCounter_CommitsOnlyWhenTheHitIsFinallyCancelled(int sourceAccuracy, bool expectCounter)
    {
        var mark = new BuffDefinition { Id = "counter_mark", Name = "counter_mark", IsDebuff = true };
        var skill = TestContentFactory.CreateExternalSkill(
            "strike",
            powerBase: 1,
            impactType: SkillImpactType.Single,
            impactSize: 0,
            castSize: 3);
        var counter = new TalentDefinition
        {
            Id = "dodge_counter",
            Name = "dodge_counter",
            Affixes =
            [
                new HookAffix
                {
                    Timing = HookTiming.BeforeHitResolved,
                    Conditions = [new ContextUnitRoleBattleHookConditionDefinition(BattleHookContextUnitRole.Target)],
                    Effects =
                    [
                        new CancelHitBattleHookEffectDefinition(),
                        new ApplyBuffBattleEffectDefinition(new SourceBattleUnitSelectorDefinition(), "counter_mark", 1, 2),
                    ],
                    FloatText = new BattleFloatTextDefinition { Text = "dodged" },
                },
            ],
        };
        var hero = CreateUnit(
            "hero",
            1,
            new GridPosition(0, 0),
            stats: new Dictionary<StatType, int>
            {
                [StatType.Quanzhang] = 100,
                [StatType.Bili] = 100,
                [StatType.Accuracy] = sourceAccuracy,
            },
            externalSkills: [new InitialExternalSkillEntryDefinition(skill, 1)]);
        var enemy = CreateUnit("enemy", 2, new GridPosition(1, 0), maxHp: 1000, talents: [counter]);
        hero.ActionGauge = 100;
        var state = new BattleState(new BattleGrid(4, 4), [hero, enemy]);
        var engine = new BattleEngine(
            random: new FixedRandomService(0d),
            buffResolver: id => id == mark.Id ? mark : throw new InvalidOperationException(id));
        engine.BeginAction(state, hero.Id);

        var result = engine.CastSkill(state, hero.Id, hero.Character.GetExternalSkills().Single(), enemy.Position);

        Assert.True(result.Success);
        Assert.Equal(expectCounter, hero.HasBuff("counter_mark"));
        Assert.Equal(expectCounter, result.Messages.OfType<BattleCue>().Any(static cue => cue.Kind == BattleCueKind.FloatTextRequested));
        Assert.Equal(expectCounter, enemy.Hp == 1000);
    }

    // ---- 回怒 ----

    [Fact]
    public void AttackRage_UsesUnitsActuallyAffectedBySkillEffects()
    {
        var mark = new BuffDefinition { Id = "star_mark", Name = "star_mark", IsDebuff = true };
        var special = CreateSelfSpecial(
            "flying_stars",
            SpecialSkillIntent.Offensive,
            effects: [new ApplyBuffBattleEffectDefinition(new AllEnemiesBattleUnitSelectorDefinition(), "star_mark", 1, 2)]);
        var hero = CreateUnit("hero", 1, new GridPosition(0, 0), specialSkills: [special]);
        var enemy = CreateUnit("enemy", 2, new GridPosition(3, 3));
        hero.ActionGauge = 100;
        var state = new BattleState(new BattleGrid(4, 4), [hero, enemy]);
        var engine = new BattleEngine(
            random: new FixedRandomService(0d),
            buffResolver: id => id == mark.Id ? mark : throw new InvalidOperationException(id));
        engine.BeginAction(state, hero.Id);

        var result = engine.CastSkill(state, hero.Id, hero.Character.GetSpecialSkills().Single(), hero.Position);

        Assert.True(result.Success);
        Assert.True(enemy.HasBuff("star_mark"));
        Assert.Contains(enemy.Id, result.Value!.AffectedUnitIds);
        Assert.Single(result.Messages.OfType<BattleFact>(), fact =>
            fact.Kind == BattleFactKind.RageChanged &&
            fact.UnitId == hero.Id &&
            fact.Detail == "attack:1");
    }

    [Fact]
    public void DamagedRage_IsRolledOncePerTargetPerSkillCast()
    {
        var skill = TestContentFactory.CreateExternalSkill(
            "strike",
            powerBase: 1,
            impactType: SkillImpactType.Single,
            impactSize: 0,
            castSize: 3);
        var multiStrike = new TalentDefinition
        {
            Id = "multi_strike",
            Name = "multi_strike",
            Affixes =
            [
                new HookAffix
                {
                    Timing = HookTiming.OnHitConfirmed,
                    Conditions = [new ContextUnitRoleBattleHookConditionDefinition(BattleHookContextUnitRole.Source)],
                    Effects =
                    [
                        new ExtraStrikeBattleHookEffectDefinition(
                            new TargetBattleUnitSelectorDefinition(),
                            [0.6d, 0.3d],
                            Chance: 1d),
                    ],
                },
            ],
        };
        var hero = CreateUnit(
            "hero",
            1,
            new GridPosition(0, 0),
            stats: new Dictionary<StatType, int> { [StatType.Quanzhang] = 100, [StatType.Bili] = 120 },
            externalSkills: [new InitialExternalSkillEntryDefinition(skill, 1)],
            talents: [multiStrike]);
        var enemy = CreateUnit("enemy", 2, new GridPosition(1, 0), maxHp: 10000);
        hero.ActionGauge = 100;
        var state = new BattleState(new BattleGrid(4, 4), [hero, enemy]);
        var engine = new BattleEngine(random: new FixedRandomService(0d));
        engine.BeginAction(state, hero.Id);

        var result = engine.CastSkill(state, hero.Id, hero.Character.GetExternalSkills().Single(), enemy.Position);

        Assert.True(result.Success);
        Assert.True(result.Messages.OfType<BattleFact>().Count(fact =>
            fact.Kind == BattleFactKind.Damaged && fact.UnitId == enemy.Id) > 1);
        Assert.Single(result.Messages.OfType<BattleFact>(), fact =>
            fact.Kind == BattleFactKind.RageChanged &&
            fact.UnitId == enemy.Id &&
            fact.Detail!.StartsWith("damaged", StringComparison.Ordinal));
        Assert.Equal(1, enemy.Rage);
    }

    private static SpecialSkillDefinition CreateSelfSpecial(
        string id,
        SpecialSkillIntent intent,
        IReadOnlyList<BattleEffectDefinition> effects) =>
        new(
            id,
            id,
            string.Empty,
            intent,
            string.Empty,
            Cooldown: 0,
            SkillCostDefinition.None,
            new SkillTargetingDefinition(
                CanCastAtSelf: true,
                CanImpactSelf: true,
                CastSize: 0,
                ImpactType: SkillImpactType.Single,
                ImpactSize: 0),
            string.Empty,
            string.Empty,
            Speech: null,
            Buffs: [],
            Effects: effects);

    private static BattleUnit CreateUnit(
        string id,
        int team,
        GridPosition position,
        int maxHp = 100,
        IReadOnlyDictionary<StatType, int>? stats = null,
        IReadOnlyList<TalentDefinition>? talents = null,
        IReadOnlyList<InitialExternalSkillEntryDefinition>? externalSkills = null,
        IReadOnlyList<SpecialSkillDefinition>? specialSkills = null)
    {
        var mergedStats = new Dictionary<StatType, int>
        {
            [StatType.MaxHp] = maxHp,
            [StatType.MaxMp] = 30,
            [StatType.Speed] = 10,
            [StatType.Movement] = 3,
        };
        foreach (var (stat, value) in stats ?? new Dictionary<StatType, int>())
        {
            mergedStats[stat] = value;
        }

        var definition = TestContentFactory.CreateCharacterDefinition(
            id,
            stats: mergedStats,
            externalSkills: externalSkills,
            talents: talents,
            specialSkills: specialSkills);
        var character = TestContentFactory.CreateCharacterInstance(id, definition);
        return new BattleUnit(id, character, team, position);
    }

    private sealed class FixedRandomService(double value) : IRandomService
    {
        public double NextDouble() => value;

        public int Next(int minInclusive, int maxExclusive) => minInclusive;
    }
}
