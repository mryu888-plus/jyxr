using System.Text.Json;
using Game.Core.Abstractions;
using Game.Core.Affix;
using Game.Core.Battle;
using Game.Core.Definitions;
using Game.Core.Definitions.Skills;
using Game.Core.Model;
using Game.Core.Model.Character;
using Game.Core.Serialization;

namespace Game.Tests;

/// <summary>统一伤害管线、效果能力边界与败北阻止窄操作。</summary>
public sealed class BattleDamagePipelineTests
{
    // ---------- 能力策略 ----------

    [Fact]
    public void CapabilityPolicy_RejectsHandlerDeclaringBaseContext()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CustomBattleEffectRegistry.Default.Register("t2a_wildcard_context", new WildcardHandler()));

        Assert.Contains("must declare a capability context", exception.Message);
    }

    [Fact]
    public void CapabilityPolicy_RejectsCapabilityThatDoesNotMatchTiming()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CustomBattleEffectRegistry.Default.Register("t2a_mismatched_context", new MismatchedHandler()));

        Assert.Contains("cannot use capability 'IDamageCalculationEffectContext' at timing 'OnHitConfirmed'", exception.Message);
    }

    [Fact]
    public void InternalEnergyAttack_DamagesTargetMpAndTransfersThroughRecoveryResolver()
    {
        var talent = HookTalent("internal_energy", HookTiming.OnHitConfirmed,
            """{"type":"custom","effectId":"internal_energy_attack","parameters":{"power":1}}""");
        var internalSkill = TestContentFactory.CreateInternalSkill("inner");
        var source = CreateUnit("source", 1, new GridPosition(0, 0), maxMp: 1000, mp: 0,
            stats: new Dictionary<StatType, int> { [StatType.Gengu] = 100 },
            talents: [talent],
            externalSkills: [new InitialExternalSkillEntryDefinition(Strike(), 1)],
            internalSkills: [new InitialInternalSkillEntryDefinition(internalSkill, Level: 10, Equipped: true)]);
        var target = CreateUnit("target", 2, new GridPosition(1, 0), maxHp: 100000, maxMp: 1000);
        var (engine, state) = Start(source, target);

        var result = engine.CastSkill(state, source.Id, source.Character.GetExternalSkills().Single(), target.Position);

        Assert.True(result.Success);
        Assert.Equal(800, target.Mp);
        Assert.Equal(200, source.Mp);
        Assert.Contains(result.Messages, message =>
            message is BattleFact { Kind: BattleFactKind.MpDamaged, UnitId: "target", Detail: "200" });
        Assert.Contains(result.Messages, message =>
            message is BattleFact { Kind: BattleFactKind.MpRecovered, UnitId: "source", Detail: "200" });
    }

    [Fact]
    public void GrievingBreeze_UsesActionStartMpDamageCapability()
    {
        var talent = HookTalent("grieving", HookTiming.BeforeActionStart,
            """{"type":"custom","effectId":"grieving_breeze","parameters":{}}""");
        var source = CreateUnit("source", 1, new GridPosition(0, 0), talents: [talent]);
        var near = CreateUnit("near", 2, new GridPosition(2, 0), maxMp: 100);
        var far = CreateUnit("far", 2, new GridPosition(6, 0), maxMp: 100);
        source.ActionGauge = 100;
        var state = new BattleState(new BattleGrid(8, 2), [source, near, far]);
        var engine = new BattleEngine(random: new FixedRandomService(0.5d));

        var result = engine.BeginAction(state, source.Id);

        Assert.True(result.Success);
        Assert.Equal(50, near.Mp);
        Assert.Equal(100, far.Mp);
        Assert.Contains(result.Messages, message => message is BattleFact { Kind: BattleFactKind.MpDamaged, UnitId: "near" });
    }

    // ---------- 败北阻止 ----------

    [Fact]
    public void DefeatPrevention_HigherPriorityEffectThatCannotPayCommitsNothing()
    {
        var qiShield = DefeatTalent("真气护体", "qi_shield_defeat_prevention", 300,
            """{"abilityId":"真气护体","chance":1,"mpCostPerDamage":2}""");
        var survive = DefeatTalent("百足之虫", "survive_at_one_hp", 200,
            """{"abilityId":"百足之虫","chance":1}""");
        var source = Attacker();
        var target = CreateUnit("target", 2, new GridPosition(1, 0), maxHp: 1, maxMp: 30, mp: 0,
            talents: [qiShield, survive]);
        var (engine, state) = Start(source, target);

        var result = engine.CastSkill(state, source.Id, source.Character.GetExternalSkills().Single(), target.Position);

        Assert.True(result.Success);
        Assert.Equal(1, target.Hp);
        Assert.Equal(0, target.Mp);
        Assert.Equal(1, target.GetAbilityUsageCount("百足之虫"));
        Assert.Equal(0, target.GetAbilityUsageCount("真气护体"));
        Assert.DoesNotContain(result.Messages, message => message is BattleFact { Kind: BattleFactKind.MpDamaged, UnitId: "target" });
        Assert.Single(result.Messages.OfType<BattleFact>(), fact => fact.Kind == BattleFactKind.DefeatPrevented);
    }

    [Fact]
    public void DefeatPrevention_RestoresThroughRecoveryResolverAndEmitsResourceFacts()
    {
        var endless = DefeatTalent("无尽斗志", "endless_fighting_spirit", 100,
            """{"abilityId":"无尽斗志","guaranteedFirstTalentId":"我就是神","chance":1}""");
        var halfHealing = Talent("""
            {
              "id": "half_healing", "name": "half_healing",
              "affixes": [{
                "type": "hook", "timing": "BeforeRecoveryResolved",
                "conditions": [{"type": "context_recovery_kind", "kind": "hp"}],
                "effects": [{"type": "modify_recovery", "op": "more", "delta": 0.5}]
              }]
            }
            """);
        var source = Attacker();
        var target = CreateUnit("target", 2, new GridPosition(1, 0), maxHp: 101, hp: 1, maxMp: 30, mp: 0,
            talents: [endless, halfHealing]);
        var (engine, state) = Start(source, target);

        var result = engine.CastSkill(state, source.Id, source.Character.GetExternalSkills().Single(), target.Position);

        Assert.True(result.Success);
        Assert.Equal(1 + 50, target.Hp);
        Assert.Equal(30, target.Mp);
        Assert.Equal(BattleUnit.MaxRage, target.Rage);
        Assert.Equal(1, target.GetAbilityUsageCount("无尽斗志"));
        Assert.Contains(result.Messages, message =>
            message is BattleFact { Kind: BattleFactKind.RageChanged, UnitId: "target", Timing: HookTiming.BeforeDefeated });
        Assert.Contains(result.Messages, message =>
            message is BattleFact { Kind: BattleFactKind.MpRecovered, UnitId: "target", Timing: HookTiming.BeforeDefeated });
        Assert.Contains(result.Messages, message =>
            message is BattleFact { Kind: BattleFactKind.Healed, UnitId: "target", Timing: HookTiming.BeforeDefeated });
    }

    [Fact]
    public void DefeatPrevention_QiShieldCommitsMpCostAsFact()
    {
        var qiShield = DefeatTalent("真气护体", "qi_shield_defeat_prevention", 200,
            """{"abilityId":"真气护体","chance":1,"mpCostPerDamage":1}""");
        var source = Attacker();
        var target = CreateUnit("target", 2, new GridPosition(1, 0), maxHp: 1, maxMp: 100000, talents: [qiShield]);
        var (engine, state) = Start(source, target);

        var result = engine.CastSkill(state, source.Id, source.Character.GetExternalSkills().Single(), target.Position);

        Assert.True(result.Success);
        var spent = 100000 - target.Mp;
        Assert.True(spent > 0);
        Assert.Contains(result.Messages, message =>
            message is BattleFact { Kind: BattleFactKind.MpDamaged, UnitId: "target" } fact &&
            fact.Detail == $"真气护体:{spent}");
        Assert.Equal(1, target.GetAbilityUsageCount("真气护体"));
    }

    // ---------- 能力伤害进入公共管线 ----------

    [Fact]
    public void AbilityDamage_CanBeRewrittenBeforeApplicationByTargetSideEffects()
    {
        var halfDamage = HookTalent("half_damage", HookTiming.BeforeDamageApplied,
            """{"type":"modify_damage","op":"more","delta":0.5}""");
        var (engine, state, source, target) = FirearmDuel(targetTalents: [halfDamage]);

        var result = engine.CastSkill(state, source.Id, source.Character.GetSpecialSkills().Single(), target.Position);

        Assert.True(result.Success);
        Assert.Equal(1000 - 100, target.Hp);
    }

    [Fact]
    public void AbilityDamage_JudgesHitLikeLegacySpecialSkills()
    {
        var dodge = Talent("""
            {
              "id": "dodge", "name": "dodge",
              "affixes": [{
                "type": "hook", "timing": "BeforeHitResolved",
                "conditions": [{"type": "context_unit_role", "role": "target"}],
                "effects": [{"type": "cancel_hit"}]
              }]
            }
            """);
        var (engine, state, source, target) = FirearmDuel(targetTalents: [dodge]);

        var result = engine.CastSkill(state, source.Id, source.Character.GetSpecialSkills().Single(), target.Position);

        Assert.True(result.Success);
        Assert.Equal(1000, target.Hp);
        Assert.Contains(result.Messages, message =>
            message is BattleFact { Kind: BattleFactKind.Damaged, UnitId: "target", Damage.Amount: 0 });
    }

    [Fact]
    public void AbilityDamage_IsNotCappedByNormalFormulaCaps()
    {
        var careful = HookTalent("careful", HookTiming.BeforeDamageApplied, CarefulDefenseEffect(cap: 10));
        var (engine, state, source, target) = FirearmDuel(targetTalents: [careful]);

        var result = engine.CastSkill(state, source.Id, source.Character.GetSpecialSkills().Single(), target.Position);

        Assert.True(result.Success);
        Assert.Equal(1000 - 200, target.Hp);
    }

    // ---------- 八卦阵：同一次攻击的多个承伤分量 ----------

    [Fact]
    public void TransferDamage_IsAnotherComponentOfTheSameAttack()
    {
        var bagua = HookTalent("bagua", HookTiming.BeforeDamageApplied,
            """{"type":"custom","effectId":"bagua_formation","parameters":{"radius":5,"transferFactor":0.8}}""");
        var dealtObserver = HookTalent("dealt_observer", HookTiming.OnDamageDealt, null);
        var takenObserver = HookTalent("taken_observer", HookTiming.OnDamageTaken, null);
        var source = Attacker(maxHp: 100000, hp: 10, lifestealRate: 0.5d, talents: [dealtObserver]);
        var target = CreateUnit("target", 2, new GridPosition(1, 0), maxHp: 100000, talents: [bagua, takenObserver]);
        var defender = CreateUnit("defender", 2, new GridPosition(1, 1), maxHp: 100000, talents: [takenObserver]);
        var (engine, state) = Start(source, target, defender);

        var result = engine.CastSkill(state, source.Id, source.Character.GetExternalSkills().Single(), target.Position);

        Assert.True(result.Success);
        var facts = result.Messages.OfType<BattleFact>().ToList();
        var defenderDamage = Assert.Single(facts, fact => fact.Kind == BattleFactKind.Damaged && fact.UnitId == "defender");
        var targetDamage = Assert.Single(facts, fact => fact.Kind == BattleFactKind.Damaged && fact.UnitId == "target");
        Assert.Equal("八卦阵", defenderDamage.Detail);
        Assert.Equal("source", defenderDamage.Damage!.SourceUnitId);
        Assert.True(defenderDamage.Damage.Amount > targetDamage.Damage!.Amount);
        Assert.True(facts.IndexOf(defenderDamage) < facts.IndexOf(targetDamage));

        var total = defenderDamage.Damage.Amount + targetDamage.Damage.Amount;
        var lifesteal = Assert.Single(facts, fact => fact.Kind == BattleFactKind.Lifesteal);
        Assert.Equal(total / 2, lifesteal.Lifesteal!.Amount);
        Assert.Single(result.Messages.OfType<BattleTrace>(), trace =>
            trace is { Kind: BattleTraceKind.HooksTriggered, UnitId: "source", Timing: HookTiming.OnDamageDealt });
        Assert.Contains(result.Messages, message =>
            message is BattleTrace { Kind: BattleTraceKind.HooksTriggered, UnitId: "defender", Timing: HookTiming.OnDamageTaken });
        Assert.Contains(result.Messages, message =>
            message is BattleTrace { Kind: BattleTraceKind.HooksTriggered, UnitId: "target", Timing: HookTiming.OnDamageTaken });
    }

    // ---------- 攀云乘龙 / 无形剑气：不受封顶影响的追加伤害 ----------

    [Fact]
    public void UncappedBonus_IsAddedAfterCapsLikeLegacy()
    {
        var cloud = CloudClimbingTalent();
        var careful = HookTalent("careful", HookTiming.BeforeDamageApplied, CarefulDefenseEffect(cap: 10));
        var source = Attacker(stats: new Dictionary<StatType, int> { [StatType.Shenfa] = 50 }, talents: [cloud]);
        var target = CreateUnit("target", 2, new GridPosition(1, 0), maxHp: 100000, talents: [careful]);
        var (engine, state) = Start(source, target);

        var result = engine.CastSkill(state, source.Id, source.Character.GetExternalSkills().Single(), target.Position);

        Assert.True(result.Success);
        Assert.Equal(100000 - 10 - 50, target.Hp);
    }

    [Fact]
    public void UncappedBonus_IsDroppedWhenDamageIsCancelled()
    {
        var cloud = CloudClimbingTalent();
        var eternalSpring = HookTalent("spring", HookTiming.BeforeDamageApplied,
            """{"type":"custom","effectId":"eternal_spring","parameters":{"enhancedTalentId":"none","chance":1,"enhancedChance":1,"recoveryFactor":0,"floatText":"spring"}}""");
        var source = Attacker(stats: new Dictionary<StatType, int> { [StatType.Shenfa] = 50 }, talents: [cloud]);
        var target = CreateUnit("target", 2, new GridPosition(1, 0), maxHp: 100000, talents: [eternalSpring]);
        var (engine, state) = Start(source, target);

        var result = engine.CastSkill(state, source.Id, source.Character.GetExternalSkills().Single(), target.Position);

        Assert.True(result.Success);
        Assert.Equal(100000, target.Hp);
    }

    // ---------- 斗转星移：反弹归因于反弹者 ----------

    [Fact]
    public void Reflection_IsAttributedToReflectorAndGrantsNoTakenRage()
    {
        var shiftingStars = HookTalent("stars", HookTiming.BeforeDamageApplied,
            """{"type":"custom","effectId":"shifting_stars_reflection","parameters":{"familyTalentId":"none","chance":1,"familyChance":1,"damageFactor":0.5,"floatText":"stars","speech":"stars"}}""");
        var source = Attacker(maxHp: 100000);
        var target = CreateUnit("target", 2, new GridPosition(1, 0), maxHp: 100000, talents: [shiftingStars]);
        var (engine, state) = Start(source, target);

        var result = engine.CastSkill(state, source.Id, source.Character.GetExternalSkills().Single(), target.Position);

        Assert.True(result.Success);
        Assert.Equal(100000, target.Hp);
        var reflected = Assert.Single(result.Messages.OfType<BattleFact>(),
            fact => fact.Kind == BattleFactKind.Damaged && fact.UnitId == "source");
        Assert.Equal("target", reflected.Damage!.SourceUnitId);
        Assert.Equal("斗转星移", reflected.Detail);
        Assert.True(reflected.Damage.Amount > 0);
        Assert.DoesNotContain(result.Messages, message =>
            message is BattleFact { Kind: BattleFactKind.RageChanged, UnitId: "source", Timing: HookTiming.OnDamageTaken });
    }

    // ---------- 描述的不变量 ----------

    [Fact]
    public void Descriptor_FactoriesEncodeStagePolicy()
    {
        var caster = CreateUnit("caster", 1, new GridPosition(0, 0));
        var other = CreateUnit("other", 2, new GridPosition(1, 0));
        var special = new SpecialSkillDefinition(
            "special", "special", string.Empty, SpecialSkillIntent.Offensive, string.Empty, 0,
            SkillCostDefinition.None, null, string.Empty, string.Empty, null, []);
        var skill = new Game.Core.Model.Skills.SpecialSkillInstance(special, caster.Character, active: true);

        var periodic = BattleDamageDescriptor.ForPeriodic(caster, other, 10);
        Assert.Equal(BattleDamageReactions.None, periodic.Reactions);
        Assert.Equal(1, periodic.MinimumRemainingHp);

        var reflection = BattleDamageDescriptor.ForReflection(caster, other, 10);
        Assert.False(reflection.Allows(BattleDamageReactions.PreApplication));
        Assert.False(reflection.Allows(BattleDamageReactions.TakenRage));

        var selfCost = BattleDamageDescriptor.ForSelfCost(caster, 10, skill);
        Assert.Same(caster, selfCost.Target);
        Assert.False(selfCost.Allows(BattleDamageReactions.PreApplication));
        Assert.True(selfCost.Allows(BattleDamageReactions.TakenResponses));

        var trueAbility = BattleDamageDescriptor.ForAbility(caster, other, 10, skill, mitigation: BattleDamageMitigation.True);
        Assert.False(trueAbility.Allows(BattleDamageReactions.PreApplication));
        Assert.Equal(BattleDamageHitMode.Judged, trueAbility.HitMode);

        Assert.Throws<ArgumentException>(() =>
            BattleDamageDescriptor.ForAbility(caster, other, 10, skill, mitigation: BattleDamageMitigation.Normal));
        Assert.Throws<ArgumentException>(() =>
            BattleDamageDescriptor.ForAbility(caster, other, 10, skill, hitMode: BattleDamageHitMode.Resolved));
    }

    // ---------- helpers ----------

    private static (BattleEngine Engine, BattleState State, BattleUnit Source, BattleUnit Target) FirearmDuel(
        IReadOnlyList<TalentDefinition> targetTalents)
    {
        var firearm = CustomSpecialSkill("firearm", "firearm_damage");
        var source = CreateUnit("source", 1, new GridPosition(0, 0), specialSkills: [firearm]);
        var target = CreateUnit("target", 2, new GridPosition(1, 0), maxHp: 1000, talents: targetTalents);
        var (engine, state) = Start(source, target);
        return (engine, state, source, target);
    }

    private static (BattleEngine Engine, BattleState State) Start(BattleUnit source, params BattleUnit[] others)
    {
        source.ActionGauge = 100;
        var state = new BattleState(new BattleGrid(6, 4), [source, .. others]);
        var engine = new BattleEngine(
            new BattleDamageCalculator(new FixedRandomService(0.5d)),
            random: new FixedRandomService(0d));
        engine.BeginAction(state, source.Id);
        return (engine, state);
    }

    private static ExternalSkillDefinition Strike(double lifestealRate = 0d) =>
        TestContentFactory.CreateExternalSkill(
            "strike", powerBase: 10, impactType: SkillImpactType.Single, impactSize: 0, castSize: 3)
        with { LifestealRate = lifestealRate };

    private static BattleUnit Attacker(
        int maxHp = 100,
        int? hp = null,
        double lifestealRate = 0d,
        IReadOnlyDictionary<StatType, int>? stats = null,
        IReadOnlyList<TalentDefinition>? talents = null)
    {
        var merged = new Dictionary<StatType, int>
        {
            [StatType.Quanzhang] = 100,
            [StatType.Bili] = 120,
        };
        foreach (var (stat, value) in stats ?? new Dictionary<StatType, int>())
        {
            merged[stat] = value;
        }

        return CreateUnit("source", 1, new GridPosition(0, 0), maxHp: maxHp, hp: hp, stats: merged, talents: talents,
            externalSkills: [new InitialExternalSkillEntryDefinition(Strike(lifestealRate), 1)]);
    }

    private static TalentDefinition CloudClimbingTalent() =>
        HookTalent("cloud", HookTiming.BeforeDamageCalculation,
            """{"type":"custom","effectId":"cloud_climbing_dragon","parameters":{"chance":1,"minimumShenfaFactor":1,"maximumShenfaFactor":1,"speechLines":["cloud"],"speechChance":0}}""");

    private static string CarefulDefenseEffect(int cap) =>
        $$"""
        {"type":"custom","effectId":"careful_defense","parameters":{
          "mode":"careful","carefulTalentId":"careful_id","smartTalentId":"smart_id",
          "carefulChance":1,"carefulDamageCap":{{cap}},"smartChance":0,"smartDamageCap":0,
          "carefulSpeech":{"lines":["careful"],"chance":0},"smartSpeech":{"lines":["smart"],"chance":0} } }
        """;

    private static TalentDefinition HookTalent(string id, HookTiming timing, string? effectJson) =>
        Talent($$"""
            {
              "id": "{{id}}", "name": "{{id}}",
              "affixes": [{"type": "hook", "timing": "{{timing}}", "effects": [{{effectJson ?? string.Empty}}]}]
            }
            """);

    private static TalentDefinition DefeatTalent(string id, string effectId, int priority, string parameters) =>
        Talent($$"""
            {
              "id": "{{id}}", "name": "{{id}}",
              "affixes": [{
                "type": "hook", "timing": "BeforeDefeated", "priority": {{priority}},
                "effects": [{"type": "custom", "effectId": "{{effectId}}", "parameters": {{parameters}} }]
              }]
            }
            """);

    private static TalentDefinition Talent(string json)
    {
        var talent = JsonSerializer.Deserialize<TalentDefinition>(json, GameJson.Default)!;
        var repository = TestContentFactory.CreateRepository(talents: [talent]);
        foreach (var affix in talent.Affixes)
        {
            affix.Resolve(repository);
        }

        return talent;
    }

    private static SpecialSkillDefinition CustomSpecialSkill(string id, string effectId)
    {
        using var parameters = JsonDocument.Parse("{}");
        var effect = new CustomAbilityBattleEffectDefinition(
            effectId,
            new TargetBattleUnitSelectorDefinition(),
            parameters.RootElement.Clone());
        var skill = new SpecialSkillDefinition(
            id, id, string.Empty, SpecialSkillIntent.Offensive, string.Empty, Cooldown: 0,
            SkillCostDefinition.None,
            new SkillTargetingDefinition(CastSize: 1, ImpactType: SkillImpactType.Single, ImpactSize: 0),
            string.Empty, string.Empty, Speech: null, Buffs: [], Effects: [effect]);
        effect.Resolve(TestContentFactory.CreateRepository(specialSkills: [skill]));
        return skill;
    }

    private static BattleUnit CreateUnit(
        string id,
        int team,
        GridPosition position,
        int maxHp = 100,
        int maxMp = 30,
        int? hp = null,
        int? mp = null,
        IReadOnlyDictionary<StatType, int>? stats = null,
        IReadOnlyList<TalentDefinition>? talents = null,
        IReadOnlyList<InitialExternalSkillEntryDefinition>? externalSkills = null,
        IReadOnlyList<InitialInternalSkillEntryDefinition>? internalSkills = null,
        IReadOnlyList<SpecialSkillDefinition>? specialSkills = null)
    {
        var mergedStats = new Dictionary<StatType, int>
        {
            [StatType.MaxHp] = maxHp,
            [StatType.MaxMp] = maxMp,
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
            internalSkills: internalSkills,
            talents: talents,
            gender: CharacterGender.Neutral,
            specialSkills: specialSkills);
        var character = TestContentFactory.CreateCharacterInstance(id, definition);
        return new BattleUnit(id, character, team, position, hp: hp, mp: mp);
    }

    private sealed class FixedRandomService(double value) : IRandomService
    {
        public double NextDouble() => value;

        public int Next(int minInclusive, int maxExclusive) => minInclusive;
    }

    private sealed record WildcardParameters;

    private sealed class WildcardHandler : CustomBattleEffectHandler<WildcardParameters, IBattleEffectContext>
    {
        public override IReadOnlySet<HookTiming> SupportedTimings { get; } =
            new HashSet<HookTiming> { HookTiming.OnHitConfirmed };

        public override void Execute(IBattleEffectContext context, WildcardParameters parameters)
        {
        }
    }

    private sealed class MismatchedHandler : CustomBattleEffectHandler<WildcardParameters, IDamageCalculationEffectContext>
    {
        public override IReadOnlySet<HookTiming> SupportedTimings { get; } =
            new HashSet<HookTiming> { HookTiming.OnHitConfirmed };

        public override void Execute(IDamageCalculationEffectContext context, WildcardParameters parameters)
        {
        }
    }
}
