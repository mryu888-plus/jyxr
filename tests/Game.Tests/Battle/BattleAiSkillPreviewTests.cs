using System.Text.Json;
using Game.Core.Abstractions;
using Game.Core.Battle;
using Game.Core.Definitions;
using Game.Core.Definitions.Skills;
using Game.Core.Model;

namespace Game.Tests;

/// <summary>AI 候选生成与评分共用技能执行预览：实际作用单位、非伤害收益与辅助技能筛选。</summary>
public sealed class BattleAiSkillPreviewTests
{
    private static readonly BuffDefinition Poison = new() { Id = "中毒", Name = "中毒", IsDebuff = true };
    private static readonly BuffDefinition Stun = new() { Id = "晕眩", Name = "晕眩", IsDebuff = true };
    private static readonly BuffDefinition AttackUp = new() { Id = "攻击强化", Name = "攻击强化", IsDebuff = false };

    [Fact]
    public void Decide_CastsSelfTargetedFieldWideDebuff_WhenItAffectsMoreEnemiesThanAllies()
    {
        // 飞星术: 对自己施放, 实际经 all_units 作用全场
        var flyingStars = CreateSpecialSkill(
            "flying_stars",
            SpecialSkillIntent.Offensive,
            new SkillTargetingDefinition(CanCastAtSelf: true, CanImpactSelf: true, CastSize: 0, ImpactType: SkillImpactType.Single, ImpactSize: 0),
            CreateRandomBuffEffect(new AllUnitsBattleUnitSelectorDefinition(), "中毒"));
        var caster = CreateUnit("caster", team: 2, new GridPosition(0, 0), specialSkills: [flyingStars]);
        var enemies = Enumerable.Range(0, 3)
            .Select(index => CreateUnit($"enemy{index}", team: 1, new GridPosition(7, index * 2)))
            .ToArray();
        var (engine, state) = Begin(caster, enemies);

        var plan = CreateAgent(engine).Decide(state, caster.Id);

        Assert.Equal(BattleMainActionKind.CastSkill, plan.MainAction.Kind);
        Assert.Equal("flying_stars", plan.MainAction.SkillId);
    }

    [Fact]
    public void Decide_SkipsFieldWideDebuff_WhenItHurtsMoreAlliesThanEnemies()
    {
        var flyingStars = CreateSpecialSkill(
            "flying_stars",
            SpecialSkillIntent.Offensive,
            new SkillTargetingDefinition(CanCastAtSelf: true, CanImpactSelf: true, CastSize: 0, ImpactType: SkillImpactType.Single, ImpactSize: 0),
            CreateRandomBuffEffect(new AllUnitsBattleUnitSelectorDefinition(), "中毒"));
        var caster = CreateUnit("caster", team: 2, new GridPosition(0, 0), specialSkills: [flyingStars]);
        var allies = Enumerable.Range(0, 3)
            .Select(index => CreateUnit($"ally{index}", team: 2, new GridPosition(1, index * 2 + 1)))
            .ToArray();
        var enemy = CreateUnit("enemy", team: 1, new GridPosition(7, 5));
        var (engine, state) = Begin(caster, [.. allies, enemy]);

        var plan = CreateAgent(engine).Decide(state, caster.Id);

        Assert.NotEqual("flying_stars", plan.MainAction.SkillId);
    }

    [Fact]
    public void Decide_UsesNonDamageControlSkill_OnlyWhenItRemovesEnemyRage()
    {
        // 沉鱼落雁: 无伤害, 清空目标怒气
        var calmBeauty = CreateSpecialSkill(
            "calm_beauty",
            SpecialSkillIntent.Offensive,
            new SkillTargetingDefinition(CastSize: 3, ImpactType: SkillImpactType.Single, ImpactSize: 0),
            new SetRageBattleEffectDefinition(new TargetBattleUnitSelectorDefinition(), 0));

        var angryEnemy = CreateUnit("enemy", team: 1, new GridPosition(2, 0), rage: 5);
        var casterA = CreateUnit("caster", team: 2, new GridPosition(0, 0), specialSkills: [calmBeauty]);
        var (engineA, stateA) = Begin(casterA, [angryEnemy]);
        var planA = CreateAgent(engineA).Decide(stateA, casterA.Id);

        var calmEnemy = CreateUnit("enemy", team: 1, new GridPosition(2, 0), rage: 0);
        var casterB = CreateUnit("caster", team: 2, new GridPosition(0, 0), specialSkills: [calmBeauty]);
        var (engineB, stateB) = Begin(casterB, [calmEnemy]);
        var candidatesB = new BattleTurnCandidateGenerator(engineB).Generate(stateB, casterB.Id);

        Assert.Equal("calm_beauty", planA.MainAction.SkillId);
        Assert.Equal(new GridPosition(2, 0), planA.MainAction.TargetPosition);
        Assert.DoesNotContain(candidatesB, candidate => candidate.Plan.MainAction.Kind == BattleMainActionKind.CastSkill);
    }

    [Fact]
    public void Decide_PrefersDamageSkillThatAlsoAppliesEnemyDebuff_WhenDamageIsEqual()
    {
        var plain = TestContentFactory.CreateExternalSkill(
            "a_plain",
            powerBase: 10,
            impactType: SkillImpactType.Single,
            impactSize: 0,
            castSize: 1);
        var stunning = TestContentFactory.CreateExternalSkill(
            "z_stunning",
            powerBase: 10,
            impactType: SkillImpactType.Single,
            impactSize: 0,
            castSize: 1,
            buffs: [new SkillBuffDefinition(Stun, level: 1, duration: 2, chance: 100)]);
        var caster = CreateUnit(
            "caster",
            team: 2,
            new GridPosition(0, 0),
            externalSkills:
            [
                new InitialExternalSkillEntryDefinition(plain, 1),
                new InitialExternalSkillEntryDefinition(stunning, 1),
            ]);
        var enemy = CreateUnit("enemy", team: 1, new GridPosition(1, 0), maxHp: 100000);
        var (engine, state) = Begin(caster, [enemy]);
        var generator = new BattleTurnCandidateGenerator(engine);

        var candidates = generator.Generate(state, caster.Id);
        var plan = CreateAgent(engine).Decide(state, caster.Id);

        var plainCandidate = candidates.First(candidate => candidate.Plan.MainAction.SkillId == "a_plain");
        var stunningCandidate = candidates.First(candidate =>
            candidate.Plan.MainAction.SkillId == "z_stunning" &&
            candidate.Plan.MoveDestination == plainCandidate.Plan.MoveDestination &&
            candidate.Plan.MainAction.TargetPosition == plainCandidate.Plan.MainAction.TargetPosition);
        Assert.Equal(plainCandidate.EnemyDamage, stunningCandidate.EnemyDamage);
        Assert.True(stunningCandidate.UtilityScore > 0d);
        Assert.Equal("z_stunning", plan.MainAction.SkillId);
    }

    [Fact]
    public void Generate_IgnoresDebuffThatTargetAlreadyCarriesAtHigherLevel()
    {
        var stunning = TestContentFactory.CreateExternalSkill(
            "stunning",
            powerBase: 10,
            impactType: SkillImpactType.Single,
            impactSize: 0,
            castSize: 1,
            buffs: [new SkillBuffDefinition(Stun, level: 1, duration: 2, chance: 100)]);
        var caster = CreateUnit("caster", team: 2, new GridPosition(0, 0),
            externalSkills: [new InitialExternalSkillEntryDefinition(stunning, 1)]);
        var enemy = CreateUnit("enemy", team: 1, new GridPosition(1, 0), maxHp: 100000);
        var (engine, state) = Begin(caster, [enemy]);
        enemy.TryApplyBuff(new BattleBuffInstance(Stun, level: 5, remainingTurns: 5, sourceUnitId: caster.Id, appliedAtActionSerial: 0));

        var candidates = new BattleTurnCandidateGenerator(engine).Generate(state, caster.Id);

        Assert.All(
            candidates.Where(candidate => candidate.Plan.MainAction.Kind == BattleMainActionKind.CastSkill),
            candidate => Assert.Equal(0d, candidate.UtilityScore));
    }

    [Fact]
    public void Decide_SupportFallbackPrefersSkillWithPreviewedBenefit()
    {
        // 满血时自疗没有收益, 应选择能给自己加攻的技能 (即便随机数总取第一个)
        var selfHeal = CreateSpecialSkill(
            "a_self_heal",
            SpecialSkillIntent.Support,
            new SkillTargetingDefinition(CanCastAtSelf: true, CanImpactSelf: true, CastSize: 0, ImpactType: SkillImpactType.Single, ImpactSize: 0),
            new AddHpBattleEffectDefinition(new SelfBattleUnitSelectorDefinition(), 50));
        var rally = CreateSpecialSkill(
            "b_rally",
            SpecialSkillIntent.Support,
            new SkillTargetingDefinition(CanCastAtSelf: true, CanImpactSelf: true, CastSize: 0, ImpactType: SkillImpactType.Single, ImpactSize: 0),
            new ApplyBuffBattleEffectDefinition(new SelfBattleUnitSelectorDefinition(), "攻击强化", 3, 3));
        var caster = CreateUnit("caster", team: 2, new GridPosition(1, 2), specialSkills: [selfHeal, rally]);
        var enemy = CreateUnit("enemy", team: 1, new GridPosition(5, 2));
        var (engine, state) = Begin(caster, [enemy], new MinimumRandomService());

        var plan = CreateAgent(engine).Decide(state, caster.Id);

        Assert.Equal("b_rally", plan.MainAction.SkillId);
    }

    [Fact]
    public void PreviewSkillExecution_ResolvesNearbySelectorsFromCastDestination()
    {
        var shockwave = CreateSpecialSkill(
            "shockwave",
            SpecialSkillIntent.Offensive,
            new SkillTargetingDefinition(CanCastAtSelf: true, CanImpactSelf: true, CastSize: 0, ImpactType: SkillImpactType.Single, ImpactSize: 0),
            new SetActionGaugeBattleEffectDefinition(new NearbyEnemiesBattleUnitSelectorDefinition(1), 0));
        var caster = CreateUnit("caster", team: 2, new GridPosition(0, 0), specialSkills: [shockwave]);
        var enemy = CreateUnit("enemy", team: 1, new GridPosition(5, 0));
        var (engine, state) = Begin(caster, [enemy]);
        enemy.ActionGauge = 60;
        var skill = caster.Character.GetSpecialSkills().Single();
        var atStart = new List<BattleSkillPreviewEffect>();
        var nearEnemy = new List<BattleSkillPreviewEffect>();

        engine.PreviewSkillExecution(state, caster, caster.Position, skill, [caster], (_, _) => 0, atStart);
        engine.PreviewSkillExecution(state, caster, new GridPosition(4, 0), skill, [caster], (_, _) => 0, nearEnemy);

        Assert.Empty(atStart);
        var gauge = Assert.Single(nearEnemy);
        Assert.Same(enemy, gauge.Unit);
        Assert.Equal(BattleSkillPreviewEffectKind.ActionGaugeChange, gauge.Kind);
        Assert.Equal(-60, gauge.Amount);
    }

    [Fact]
    public void Decide_AvoidsSelfSacrificingSkill_WhenItCannotKillTheTarget()
    {
        // 天地同寿: 以施法者全部气血为代价
        using var parameters = JsonDocument.Parse("{}");
        var sacrifice = CreateSpecialSkill(
            "sacrifice",
            SpecialSkillIntent.Offensive,
            new SkillTargetingDefinition(CastSize: 1, ImpactType: SkillImpactType.Single, ImpactSize: 0),
            new CustomAbilityBattleEffectDefinition(
                "heaven_and_earth_same_lifespan",
                new TargetBattleUnitSelectorDefinition(),
                parameters.RootElement.Clone()));
        var caster = CreateUnit("caster", team: 2, new GridPosition(0, 0), specialSkills: [sacrifice]);
        var enemy = CreateUnit("enemy", team: 1, new GridPosition(1, 0), maxHp: 100000);
        var (engine, state) = Begin(caster, [enemy]);
        var generator = new BattleTurnCandidateGenerator(engine);

        var candidate = generator.Generate(state, caster.Id)
            .First(candidate => candidate.Plan.MainAction.SkillId == "sacrifice");
        var plan = CreateAgent(engine).Decide(state, caster.Id);

        Assert.Equal(caster.Hp, candidate.AllyDamage);
        Assert.Equal(1, candidate.AllyKills);
        Assert.NotEqual("sacrifice", plan.MainAction.SkillId);
    }

    private static CustomAbilityBattleEffectDefinition CreateRandomBuffEffect(
        BattleUnitSelectorDefinition target,
        string buffId)
    {
        using var parameters = JsonDocument.Parse(
            $$"""{"buffId":"{{buffId}}","chance":1.0,"minimumLevel":5,"maximumLevel":10,"minimumDuration":3,"maximumDuration":3}""");
        return new CustomAbilityBattleEffectDefinition("random_buff", target, parameters.RootElement.Clone());
    }

    private static SpecialSkillDefinition CreateSpecialSkill(
        string id,
        SpecialSkillIntent intent,
        SkillTargetingDefinition targeting,
        params BattleEffectDefinition[] effects)
    {
        var skill = new SpecialSkillDefinition(
            id,
            id,
            string.Empty,
            intent,
            string.Empty,
            Cooldown: 0,
            SkillCostDefinition.None,
            targeting,
            string.Empty,
            string.Empty,
            Speech: null,
            Buffs: [],
            Effects: effects);
        ContentReferenceGraph.Resolve(skill, TestContentFactory.CreateRepository(specialSkills: [skill], buffs: [Poison, Stun, AttackUp]));
        return skill;
    }

    private static (BattleEngine Engine, BattleState State) Begin(
        BattleUnit actor,
        IReadOnlyList<BattleUnit> others,
        IRandomService? random = null)
    {
        actor.ActionGauge = 100;
        var state = new BattleState(new BattleGrid(8, 6), [actor, .. others]);
        var engine = new BattleEngine(random: random, buffResolver: ResolveBuff);
        engine.BeginAction(state, actor.Id);
        return (engine, state);
    }

    private static BuffDefinition ResolveBuff(string id) =>
        new[] { Poison, Stun, AttackUp }.Single(buff => buff.Id == id);

    private static BattleUnit CreateUnit(
        string id,
        int team,
        GridPosition position,
        int maxHp = 1000,
        int rage = 0,
        IReadOnlyList<InitialExternalSkillEntryDefinition>? externalSkills = null,
        IReadOnlyList<SpecialSkillDefinition>? specialSkills = null)
    {
        var definition = TestContentFactory.CreateCharacterDefinition(
            id,
            stats: new Dictionary<StatType, int>
            {
                [StatType.MaxHp] = maxHp,
                [StatType.MaxMp] = 1000,
                [StatType.Movement] = 3,
            },
            externalSkills: externalSkills,
            specialSkills: specialSkills);
        var character = TestContentFactory.CreateCharacterInstance(id, definition);
        return new BattleUnit(id, character, team, position, rage: rage);
    }

    private static BasicEnemyBattleAgent CreateAgent(BattleEngine engine) =>
        new(new BattleTurnCandidateGenerator(engine), new BattleAiPolicyResolver(_ => 10));

    private sealed class MinimumRandomService : IRandomService
    {
        public double NextDouble() => 0d;

        public int Next(int minInclusive, int maxExclusive) => minInclusive;
    }
}
