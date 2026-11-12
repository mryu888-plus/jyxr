using System.Collections.ObjectModel;
using Game.Core.Definitions;

namespace Game.Core.Affix;

public static class AffixResolver
{
    public static ResolvedAffixSet Resolve(
        IEnumerable<AffixDefinition> activeAffixes,
        IEnumerable<TalentDefinition> learnedTalents)
    {
        return TalentResolver.Resolve(learnedTalents, new ReadOnlyCollection<AffixDefinition>(activeAffixes.ToList()));
    }

    public static IReadOnlyList<AffixDefinition> ResolveProviderAffixes(
        IAffixProvider provider,
        ProviderKind sourceKind,
        string sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        var affixes = new List<AffixDefinition>();

        foreach (var (affix, index) in provider.Affixes.Select((affix, index) => (affix, index)))
        {
            affixes.Add(affix with
            {
                SourceKind = sourceKind,
                SourceId = sourceId,
                SourceAffixOrder = index,
            });
        }

        return new ReadOnlyCollection<AffixDefinition>(affixes);
    }

    public static IReadOnlyList<AffixDefinition> ResolveSkillAffixes(
        IEnumerable<SkillAffixDefinition> skillAffixes,
        int skillLevel,
        bool isEquippedInternalSkill,
        ProviderKind sourceKind,
        string sourceId)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(skillLevel, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        var affixes = new List<AffixDefinition>();

        foreach (var (affix, index) in skillAffixes.Select((affix, index) => (affix, index)))
        {
            if (skillLevel < affix.MinimumLevel)
            {
                continue;
            }

            if (affix.RequiresEquippedInternalSkill && !isEquippedInternalSkill)
            {
                continue;
            }

            affixes.Add(affix.Effect with
            {
                SourceKind = sourceKind,
                SourceId = sourceId,
                SourceAffixOrder = index,
            });
        }

        return new ReadOnlyCollection<AffixDefinition>(affixes);
    }
}
