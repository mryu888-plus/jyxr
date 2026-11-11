using System.Collections.ObjectModel;
using Game.Core.Battle;
using Game.Core.Definitions;
using Game.Core.Model;

namespace Game.Core.Affix;

public abstract record AffixOrigin
{
    public abstract int LayerOrder { get; }
}

public sealed record CharacterAffixOrigin(ProviderKind ProviderKind, string SourceId) : AffixOrigin
{
    public override int LayerOrder => 0;
}

public sealed record BuffAffixOrigin(string BuffId, long AppliedAtActionSerial) : AffixOrigin
{
    public override int LayerOrder => 1;
}

public sealed record ScopedEffectAffixOrigin(string EffectId, long InstanceSequence) : AffixOrigin
{
    public override int LayerOrder => 2;
}

public sealed record ActiveAffixEntry(
    AffixDefinition Definition,
    AffixOrigin Origin,
    BattleUnit? Provider = null,
    int SourceLevel = 1,
    int AffixOrder = 0,
    long SourceSequence = 0);

public sealed record ActiveHookEntry(
    HookAffix Hook,
    AffixOrigin Origin,
    BattleUnit? Provider,
    int SourceLevel,
    int AffixOrder,
    long SourceSequence);

public sealed class AffixProjection
{
    public static AffixProjection Empty { get; } = new(
        new HashSet<TraitId>(),
        new ReadOnlyDictionary<HookTiming, IReadOnlyList<ActiveHookEntry>>(new Dictionary<HookTiming, IReadOnlyList<ActiveHookEntry>>()),
        new ReadOnlyDictionary<StatType, ModifierBucket>(new Dictionary<StatType, ModifierBucket>()),
        new ReadOnlyDictionary<string, ModifierBucket>(new Dictionary<string, ModifierBucket>()),
        new ReadOnlyDictionary<WeaponType, ModifierBucket>(new Dictionary<WeaponType, ModifierBucket>()),
        Array.Empty<SkillTargetingModifierAffix>(),
        new ReadOnlyDictionary<string, ModifierBucket>(new Dictionary<string, ModifierBucket>()));

    public AffixProjection(
        IReadOnlySet<TraitId> traits,
        IReadOnlyDictionary<HookTiming, IReadOnlyList<ActiveHookEntry>> hooksByTiming,
        IReadOnlyDictionary<StatType, ModifierBucket> statModifierBuckets,
        IReadOnlyDictionary<string, ModifierBucket> skillModifierBuckets,
        IReadOnlyDictionary<WeaponType, ModifierBucket> weaponModifierBuckets,
        IReadOnlyList<SkillTargetingModifierAffix> targetingModifiers,
        IReadOnlyDictionary<string, ModifierBucket> legendChanceModifierBuckets)
    {
        Traits = traits;
        HooksByTiming = hooksByTiming;
        StatModifierBuckets = statModifierBuckets;
        SkillModifierBuckets = skillModifierBuckets;
        WeaponModifierBuckets = weaponModifierBuckets;
        TargetingModifiers = targetingModifiers;
        LegendChanceModifierBuckets = legendChanceModifierBuckets;
    }

    public IReadOnlySet<TraitId> Traits { get; }
    public IReadOnlyDictionary<HookTiming, IReadOnlyList<ActiveHookEntry>> HooksByTiming { get; }
    public IReadOnlyDictionary<StatType, ModifierBucket> StatModifierBuckets { get; }
    public IReadOnlyDictionary<string, ModifierBucket> SkillModifierBuckets { get; }
    public IReadOnlyDictionary<WeaponType, ModifierBucket> WeaponModifierBuckets { get; }
    /// <summary>技能范围修改，保持内容原始顺序（角色 → 战斗内 buff → 场景效果）。</summary>
    public IReadOnlyList<SkillTargetingModifierAffix> TargetingModifiers { get; }

    /// <summary>按原始顺序依次应用匹配该技能的范围修改。</summary>
    public int EvaluateSkillTargeting(Model.Skills.SkillInstance skill, SkillTargetingField field, int baseValue)
    {
        ArgumentNullException.ThrowIfNull(skill);
        var bucket = ModifierBucket.Empty;
        foreach (var modifier in TargetingModifiers)
        {
            if (modifier.Field == field && modifier.Matches(skill))
            {
                bucket = bucket.Apply(modifier.Value);
            }
        }

        return checked((int)Math.Round(bucket.Evaluate(baseValue)));
    }
    public IReadOnlyDictionary<string, ModifierBucket> LegendChanceModifierBuckets { get; }
}

public sealed class CharacterProjection
{
    public static CharacterProjection Empty { get; } = new(
        AffixProjection.Empty,
        new HashSet<TalentDefinition>(),
        null);

    public CharacterProjection(
        AffixProjection affixes,
        IReadOnlySet<TalentDefinition> effectiveTalents,
        string? resolvedModelId)
    {
        Affixes = affixes;
        EffectiveTalents = effectiveTalents;
        ResolvedModelId = resolvedModelId;
    }

    public AffixProjection Affixes { get; }
    public IReadOnlySet<TalentDefinition> EffectiveTalents { get; }
    public string? ResolvedModelId { get; }
}
