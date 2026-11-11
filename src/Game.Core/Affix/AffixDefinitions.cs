using System.Text.Json.Serialization;
using Game.Core.Abstractions;
using Game.Core.Battle;
using Game.Core.Definitions;
using Game.Core.Model;
using Game.Core.Model.Skills;

namespace Game.Core.Affix;

public interface IAffixProvider
{
    ProviderKind ProviderKind { get; }

    IReadOnlyList<AffixDefinition> Affixes { get; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(StatModifierAffix), "stat_modifier")]
[JsonDerivedType(typeof(GrantTalentAffix), "grant_talent")]
[JsonDerivedType(typeof(GrantModelAffix), "grant_model")]
[JsonDerivedType(typeof(SkillBonusModifierAffix), "skill_bonus_modifier")]
[JsonDerivedType(typeof(WeaponBonusModifierAffix), "weapon_bonus_modifier")]
[JsonDerivedType(typeof(LegendSkillChanceModifierAffix), "legend_skill_chance_modifier")]
[JsonDerivedType(typeof(BuffLevelStatModifierAffix), "buff_level_stat_modifier")]
[JsonDerivedType(typeof(SkillTargetingModifierAffix), "skill_targeting_modifier")]
[JsonDerivedType(typeof(HookAffix), "hook")]
[JsonDerivedType(typeof(TraitAffix), "trait")]
public abstract record AffixDefinition
{
    [JsonIgnore]
    public ProviderKind SourceKind { get; internal init; } = ProviderKind.Other;

    [JsonIgnore]
    public string SourceId { get; internal init; } = "";

    [JsonIgnore]
    public int SourceAffixOrder { get; internal init; }

    public virtual void Resolve(IContentRepository contentRepository)
    {
    }
}

public sealed record SkillAffixDefinition(
    AffixDefinition Effect,
    int MinimumLevel = 1,
    bool RequiresEquippedInternalSkill = false);

public sealed record StatModifierAffix(StatType Stat, ModifierValue Value) : AffixDefinition;

public sealed record GrantTalentAffix(string TalentId) : AffixDefinition
{
    [JsonIgnore]
    public TalentDefinition Talent { get; private set; } = null!;

    public override void Resolve(IContentRepository contentRepository)
    {
        Talent = contentRepository.GetTalent(TalentId);
    }
}

public sealed record GrantModelAffix(string ModelId, int Priority, string Description = "") : AffixDefinition;

public sealed record SkillBonusModifierAffix(string SkillId, ModifierValue Value) : AffixDefinition;

public sealed record WeaponBonusModifierAffix(WeaponType WeaponType, ModifierValue Value) : AffixDefinition;

public sealed record LegendSkillChanceModifierAffix(string SkillId, ModifierValue Value) : AffixDefinition;

public sealed record BuffLevelStatModifierAffix(
    StatType Stat,
    double AddBase = 0d,
    double AddPerLevel = 0d,
    double MulPerLevel = 0d) : AffixDefinition;

/// <summary>
/// 技能范围修改。<see cref="Selector"/> 为空表示作用于全部技能；同一单位的多个范围修改按内容原始顺序依次应用。
/// </summary>
public sealed record SkillTargetingModifierAffix(
    SkillSelectorDefinition? Selector,
    SkillTargetingField Field,
    ModifierValue Value) : AffixDefinition
{
    public bool Matches(SkillInstance skill) => Selector is null || Selector.Matches(skill);

    public override void Resolve(IContentRepository contentRepository) =>
        Selector?.Resolve(contentRepository);
}

/// <summary>
/// 技能选择器：各条件之间为“且”，同一条件内为“或”；未声明的条件不限制。
/// sourceSkillIds 按技能来源 ID 匹配（招式、奥义匹配其父技能）。
/// </summary>
public sealed record SkillSelectorDefinition(
    IReadOnlyList<string>? SourceSkillIds = null,
    IReadOnlyList<WeaponType>? WeaponTypes = null,
    IReadOnlyList<SkillKind>? SkillKinds = null,
    IReadOnlyList<SkillSourceKind>? SourceKinds = null)
{
    public bool Matches(SkillInstance skill)
    {
        ArgumentNullException.ThrowIfNull(skill);
        return (SourceSkillIds is null || SourceSkillIds.Contains(skill.SourceSkillId, StringComparer.Ordinal)) &&
               (WeaponTypes is null || WeaponTypes.Contains(skill.WeaponType)) &&
               (SkillKinds is null || SkillKinds.Contains(skill.SkillKind)) &&
               (SourceKinds is null || SourceKinds.Contains(skill.SourceKind));
    }

    public void Resolve(IContentRepository contentRepository)
    {
        foreach (var (name, count) in new[]
                 {
                     ("sourceSkillIds", SourceSkillIds?.Count),
                     ("weaponTypes", WeaponTypes?.Count),
                     ("skillKinds", SkillKinds?.Count),
                     ("sourceKinds", SourceKinds?.Count),
                 })
        {
            if (count == 0)
            {
                throw new InvalidOperationException($"Skill selector field '{name}' must not be empty when declared.");
            }
        }

        if (SourceSkillIds is null && WeaponTypes is null && SkillKinds is null && SourceKinds is null)
        {
            throw new InvalidOperationException("Skill selector must declare at least one condition; omit the selector to match every skill.");
        }

        foreach (var sourceSkillId in SourceSkillIds ?? [])
        {
            if (string.IsNullOrWhiteSpace(sourceSkillId) ||
                (!contentRepository.TryGetExternalSkill(sourceSkillId, out _) &&
                 !contentRepository.TryGetInternalSkill(sourceSkillId, out _) &&
                 !contentRepository.TryGetSpecialSkill(sourceSkillId, out _)))
            {
                throw new InvalidOperationException(
                    $"Skill selector references missing source skill '{sourceSkillId}'.");
            }
        }
    }
}

public sealed record HookAffix : AffixDefinition
{
    public required HookTiming Timing { get; init; }

    public int Priority { get; init; }

    public IReadOnlyList<BattleHookConditionDefinition> Conditions { get; init; } = [];

    public IReadOnlyList<BattleEffectDefinition> Effects { get; init; } = [];

    public BattleFloatTextDefinition? FloatText { get; init; }

    public BattleSpeechDefinition? Speech { get; init; }

    public override void Resolve(IContentRepository contentRepository)
    {
        foreach (var effect in Effects)
        {
            effect.Resolve(contentRepository);
        }
    }
}

public sealed record TraitAffix(TraitId TraitId) : AffixDefinition;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ChanceBattleHookConditionDefinition), "chance")]
[JsonDerivedType(typeof(UnitLevelChanceBattleHookConditionDefinition), "unit_level_chance")]
[JsonDerivedType(typeof(DamagePositiveBattleHookConditionDefinition), "damage_positive")]
[JsonDerivedType(typeof(ContextBuffIdBattleHookConditionDefinition), "context_buff_id")]
[JsonDerivedType(typeof(ContextBuffNegativeBattleHookConditionDefinition), "context_buff_negative")]
[JsonDerivedType(typeof(ContextUnitHpRatioBattleHookConditionDefinition), "context_unit_hp_ratio")]
[JsonDerivedType(typeof(ContextUnitEffectiveTalentBattleHookConditionDefinition), "context_unit_effective_talent")]
[JsonDerivedType(typeof(ContextUnitEquippedInternalSkillBattleHookConditionDefinition), "context_unit_equipped_internal_skill")]
[JsonDerivedType(typeof(ContextUnitRelationBattleHookConditionDefinition), "context_unit_relation")]
[JsonDerivedType(typeof(ContextUnitRoleBattleHookConditionDefinition), "context_unit_role")]
[JsonDerivedType(typeof(ContextUnitGenderBattleHookConditionDefinition), "context_unit_gender")]
[JsonDerivedType(typeof(ContextHitStateBattleHookConditionDefinition), "context_hit_state")]
[JsonDerivedType(typeof(ContextSkillSourceIdBattleHookConditionDefinition), "context_skill_source_id")]
[JsonDerivedType(typeof(ContextSkillNameEqualsBattleHookConditionDefinition), "context_skill_name_equals")]
[JsonDerivedType(typeof(ContextSkillNameContainsBattleHookConditionDefinition), "context_skill_name_contains")]
[JsonDerivedType(typeof(ContextSkillKindBattleHookConditionDefinition), "context_skill_kind")]
[JsonDerivedType(typeof(ContextSkillWeaponTypeBattleHookConditionDefinition), "context_skill_weapon_type")]
[JsonDerivedType(typeof(ContextRecoveryKindBattleHookConditionDefinition), "context_recovery_kind")]
public abstract record BattleHookConditionDefinition;

public sealed record ChanceBattleHookConditionDefinition(double Value) : BattleHookConditionDefinition;

public sealed record UnitLevelChanceBattleHookConditionDefinition(
    double BaseValue,
    double ValuePerLevel,
    double MaxValue = 1d) : BattleHookConditionDefinition;

public sealed record DamagePositiveBattleHookConditionDefinition : BattleHookConditionDefinition;

public sealed record ContextBuffIdBattleHookConditionDefinition(string BuffId) : BattleHookConditionDefinition;

public sealed record ContextBuffNegativeBattleHookConditionDefinition : BattleHookConditionDefinition;

public sealed record ContextUnitHpRatioBattleHookConditionDefinition(
    double? MinExclusive = null,
    double? MaxInclusive = null) : BattleHookConditionDefinition
{
    public double? MinInclusive { get; init; }

    public double? MaxExclusive { get; init; }
}

public sealed record ContextUnitEffectiveTalentBattleHookConditionDefinition(
    IReadOnlyList<string> TalentIds) : BattleHookConditionDefinition;

public sealed record ContextUnitEquippedInternalSkillBattleHookConditionDefinition(
    IReadOnlyList<string> InternalSkillIds) : BattleHookConditionDefinition;

public sealed record ContextUnitRelationBattleHookConditionDefinition(
    BattleHookContextUnitRole Role,
    BattleHookRelation Relation) : BattleHookConditionDefinition;

public sealed record ContextUnitRoleBattleHookConditionDefinition(BattleHookContextUnitRole Role) : BattleHookConditionDefinition;

public sealed record ContextUnitGenderBattleHookConditionDefinition(
    BattleHookContextUnitRole Role,
    IReadOnlyList<CharacterGender> Genders) : BattleHookConditionDefinition;

public sealed record ContextHitStateBattleHookConditionDefinition(
    BattleHitState State) : BattleHookConditionDefinition;

public sealed record ContextSkillSourceIdBattleHookConditionDefinition(
    IReadOnlyList<string> SourceSkillIds) : BattleHookConditionDefinition;

public sealed record ContextSkillNameEqualsBattleHookConditionDefinition(
    IReadOnlyList<string> Values) : BattleHookConditionDefinition;

public sealed record ContextSkillNameContainsBattleHookConditionDefinition(
    IReadOnlyList<string> Values) : BattleHookConditionDefinition;

public sealed record ContextSkillKindBattleHookConditionDefinition(
    IReadOnlyList<SkillKind> Kinds) : BattleHookConditionDefinition;

public sealed record ContextSkillWeaponTypeBattleHookConditionDefinition(
    IReadOnlyList<WeaponType> WeaponTypes) : BattleHookConditionDefinition;

public sealed record ContextRecoveryKindBattleHookConditionDefinition(
    BattleRecoveryKind Kind) : BattleHookConditionDefinition;

public enum BattleHookRounding
{
    Truncate,
    Floor,
    Ceiling,
    Round
}

public sealed record ModifyDamageBattleHookEffectDefinition(
    ModifierOp Op,
    double Delta = 0d,
    double DeltaPerBuffLevel = 0d,
    BattleHookRounding Rounding = BattleHookRounding.Truncate) : BattleEffectDefinition;

public sealed record ModifyDamageContextBattleHookEffectDefinition(
    BattleDamageContextField Field,
    ModifierOp Op,
    double Delta = 0d,
    double DeltaPerUnitLevel = 0d,
    double DeltaPerBuffLevel = 0d,
    double? DeltaPowerBasePerBuffLevel = null,
    double? DeltaMin = null,
    double? DeltaMax = null) : BattleEffectDefinition;

public sealed record ModifyMpCostBattleHookEffectDefinition(
    ModifierOp Op,
    double Delta = 0d,
    double DeltaPerBuffLevel = 0d,
    BattleHookRounding Rounding = BattleHookRounding.Ceiling) : BattleEffectDefinition;

public sealed record ModifyRecoveryBattleHookEffectDefinition(
    ModifierOp Op,
    double Delta = 0d,
    double DeltaPerBuffLevel = 0d,
    BattleHookRounding Rounding = BattleHookRounding.Truncate) : BattleEffectDefinition;

public sealed record SetHitSuccessBattleHookEffectDefinition : BattleEffectDefinition;

public sealed record StrengthenContextBuffBattleHookEffectDefinition(
    int LevelDelta = 0,
    int TurnDelta = 0) : BattleEffectDefinition;
