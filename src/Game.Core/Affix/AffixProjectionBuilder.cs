using System.Collections.ObjectModel;
using Game.Core.Model;

namespace Game.Core.Affix;

public static class AffixProjectionBuilder
{
    public static CharacterProjection BuildCharacter(ResolvedAffixSet resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        var sourceSequences = new Dictionary<(ProviderKind Kind, string Id), long>();
        var entries = resolved.Affixes.Select(affix =>
        {
            var sourceKey = (affix.SourceKind, affix.SourceId);
            if (!sourceSequences.TryGetValue(sourceKey, out var sourceSequence))
            {
                sourceSequence = sourceSequences.Count;
                sourceSequences.Add(sourceKey, sourceSequence);
            }

            return new ActiveAffixEntry(
                affix,
                new CharacterAffixOrigin(affix.SourceKind, affix.SourceId),
                SourceLevel: 1,
                AffixOrder: affix.SourceAffixOrder,
                SourceSequence: sourceSequence);
        }).ToList();
        var selectedModel = resolved.Affixes.OfType<GrantModelAffix>()
            .Select((affix, index) => (affix, index))
            .OrderByDescending(static item => item.affix.Priority)
            .ThenByDescending(static item => item.index)
            .Select(static item => item.affix.ModelId)
            .FirstOrDefault();
        return new CharacterProjection(Build(entries), resolved.PossessedTalents, resolved.ActiveTalents, selectedModel);
    }

    public static AffixProjection Build(IEnumerable<ActiveAffixEntry> sourceEntries)
    {
        ArgumentNullException.ThrowIfNull(sourceEntries);
        var entries = sourceEntries.ToList();
        var stat = new Dictionary<StatType, ModifierBucket>();
        var skill = new Dictionary<string, ModifierBucket>();
        var weapon = new Dictionary<WeaponType, ModifierBucket>();
        var targeting = new List<SkillTargetingModifierAffix>();
        var legend = new Dictionary<LegendSkillChanceModifierKey, ModifierBucket>();
        var traits = entries.Where(static entry => entry.Definition is TraitAffix)
            .Select(static entry => ((TraitAffix)entry.Definition).TraitId)
            .ToHashSet();
        var hooks = new Dictionary<HookTiming, List<ActiveHookEntry>>();

        foreach (var entry in entries)
        {
            switch (entry.Definition)
            {
                case StatModifierAffix value:
                    Apply(stat, value.Stat, value.Value);
                    if (ShouldDouble(value, traits)) Apply(stat, value.Stat, value.Value);
                    break;
                case BuffLevelStatModifierAffix value:
                    var add = value.AddBase + value.AddPerLevel * entry.SourceLevel;
                    var mul = value.MulPerLevel * entry.SourceLevel;
                    if (Math.Abs(add) > double.Epsilon) Apply(stat, value.Stat, ModifierValue.Add(add));
                    if (Math.Abs(mul) > double.Epsilon) Apply(stat, value.Stat, ModifierValue.Increase(mul));
                    break;
                case SkillBonusModifierAffix value: Apply(skill, value.SkillId, value.Value); break;
                case WeaponBonusModifierAffix value: Apply(weapon, value.WeaponType, value.Value); break;
                case LegendSkillChanceModifierAffix value: Apply(legend, new LegendSkillChanceModifierKey(value.SkillId), value.Value); break;
                case SkillTargetingModifierAffix value:
                    targeting.Add(value);
                    break;
                case HookAffix hook:
                    if (!hooks.TryGetValue(hook.Timing, out var list)) hooks[hook.Timing] = list = [];
                    list.Add(new ActiveHookEntry(hook, entry.Origin, entry.Provider, entry.SourceLevel, entry.AffixOrder, entry.SourceSequence));
                    break;
            }
        }

        return new AffixProjection(
            new ReadOnlySet<TraitId>(traits),
            new ReadOnlyDictionary<HookTiming, IReadOnlyList<ActiveHookEntry>>(hooks.ToDictionary(
                static pair => pair.Key,
                static pair => (IReadOnlyList<ActiveHookEntry>)new ReadOnlyCollection<ActiveHookEntry>(pair.Value))),
            new ReadOnlyDictionary<StatType, ModifierBucket>(stat),
            new ReadOnlyDictionary<string, ModifierBucket>(skill),
            new ReadOnlyDictionary<WeaponType, ModifierBucket>(weapon),
            new ReadOnlyCollection<SkillTargetingModifierAffix>(targeting),
            new ReadOnlyDictionary<LegendSkillChanceModifierKey, ModifierBucket>(legend));
    }

    private static void Apply<TKey>(Dictionary<TKey, ModifierBucket> buckets, TKey key, ModifierValue value) where TKey : notnull =>
        buckets[key] = buckets.GetValueOrDefault(key, ModifierBucket.Empty).Apply(value);

    private static bool ShouldDouble(StatModifierAffix affix, IReadOnlySet<TraitId> traits) =>
        traits.Contains(TraitId.DoubleSkillEquipmentTenDimensionAffixes) &&
        StatCatalog.TenDimensionStats.Contains(affix.Stat) &&
        affix.SourceKind is ProviderKind.Equipment or ProviderKind.ExternalSkill or ProviderKind.InternalSkill;
}
