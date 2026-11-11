using Game.Core.Affix;
using Game.Core.Model;

namespace Game.Core.Battle;

public sealed class BattleEffectProjectionResolver(BattleState state)
{
    public AffixProjection GetEffectiveProjection(BattleUnit unit) => AffixProjectionCombiner.Combine(
        unit.Character.Projection.Affixes,
        unit.GetLocalBattleProjection(),
        state.ScopedEffects.ResolveProjection(state, unit));

    public double GetStat(BattleUnit unit, StatType stat) =>
        GetBucket(GetEffectiveProjection(unit).StatModifierBuckets, stat).Evaluate(unit.Character.GetBaseStat(stat));

    public double GetWeaponBonus(BattleUnit unit, WeaponType weaponType, double baseValue) =>
        GetBucket(GetEffectiveProjection(unit).WeaponModifierBuckets, weaponType).Evaluate(baseValue);

    public int GetSkillTargeting(BattleUnit unit, Model.Skills.SkillInstance skill, SkillTargetingField field, int baseValue) =>
        GetEffectiveProjection(unit).EvaluateSkillTargeting(skill, field, baseValue);

    public bool HasTrait(BattleUnit unit, TraitId trait) => GetEffectiveProjection(unit).Traits.Contains(trait);

    public IReadOnlyList<ActiveHookEntry> GetHooks(BattleUnit unit, HookTiming timing)
    {
        if (!GetEffectiveProjection(unit).HooksByTiming.TryGetValue(timing, out var hooks)) return [];
        return hooks.Select(entry => entry.Origin is BuffAffixOrigin buffOrigin
                ? entry with { Provider = state.TryGetUnit(unit.Buffs.FirstOrDefault(buff =>
                    buff.Definition.Id == buffOrigin.BuffId &&
                    buff.AppliedAtActionSerial == buffOrigin.AppliedAtActionSerial)?.SourceUnitId ?? unit.Id) }
                : entry)
            .ToList();
    }

    private static ModifierBucket GetBucket<TKey>(IReadOnlyDictionary<TKey, ModifierBucket> buckets, TKey key)
        where TKey : notnull => buckets.TryGetValue(key, out var bucket) ? bucket : ModifierBucket.Empty;
}

internal static class AffixProjectionCombiner
{
    public static AffixProjection Combine(params AffixProjection[] projections)
    {
        var hooks = projections.SelectMany(static value => value.HooksByTiming)
            .GroupBy(static pair => pair.Key)
            .ToDictionary(static group => group.Key, static group => (IReadOnlyList<ActiveHookEntry>)group.SelectMany(static pair => pair.Value).ToList());
        return new AffixProjection(
            projections.SelectMany(static value => value.Traits).ToHashSet(),
            hooks,
            CombineBuckets(projections.Select(static value => value.StatModifierBuckets)),
            CombineBuckets(projections.Select(static value => value.SkillModifierBuckets)),
            CombineBuckets(projections.Select(static value => value.WeaponModifierBuckets)),
            projections.SelectMany(static value => value.TargetingModifiers).ToList(),
            CombineBuckets(projections.Select(static value => value.LegendChanceModifierBuckets)));
    }

    private static IReadOnlyDictionary<TKey, ModifierBucket> CombineBuckets<TKey>(
        IEnumerable<IReadOnlyDictionary<TKey, ModifierBucket>> sources) where TKey : notnull
    {
        var result = new Dictionary<TKey, ModifierBucket>();
        foreach (var source in sources)
        foreach (var (key, bucket) in source)
            result[key] = result.GetValueOrDefault(key, ModifierBucket.Empty).Combine(bucket);
        return result;
    }
}
