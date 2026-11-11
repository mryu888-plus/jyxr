using Game.Core.Model;
using Game.Core.Model.Character;

namespace Game.Application;

internal static class CharacterTransformation
{
    public static void CarryOverGrowth(CharacterInstance previous, CharacterInstance rebuilt)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(rebuilt);

        if (!string.Equals(previous.Name, previous.Definition.Name, StringComparison.Ordinal))
        {
            rebuilt.Name = previous.Name;
        }

        if (!string.Equals(previous.Portrait, previous.Definition.Portrait, StringComparison.Ordinal))
        {
            rebuilt.Portrait = previous.Portrait;
        }

        if (previous.Level > rebuilt.Level)
        {
            rebuilt.SetLevel(previous.Level);
        }

        if (previous.Experience > rebuilt.Experience)
        {
            rebuilt.GrantExperience(previous.Experience - rebuilt.Experience);
        }

        rebuilt.SetUnspentStatPoints(rebuilt.UnspentStatPoints + previous.UnspentStatPoints);
        foreach (var (stat, value) in previous.GetBaseStats())
        {
            var current = rebuilt.GetBaseStat(stat);
            if (value > current)
            {
                rebuilt.AddBaseStat(stat, value - current);
            }
        }

        foreach (var skill in previous.GetExternalSkills())
        {
            var existing = rebuilt.GetExternalSkills().FirstOrDefault(candidate => candidate.Id == skill.Id);
            if (existing is null || skill.Level > existing.Level)
            {
                rebuilt.SetExternalSkillState(skill.Definition, skill.Level, skill.Exp, skill.IsActive);
            }
        }

        foreach (var skill in previous.GetInternalSkills())
        {
            var existing = rebuilt.GetInternalSkills().FirstOrDefault(candidate => candidate.Id == skill.Id);
            if (existing is null || skill.Level > existing.Level)
            {
                rebuilt.SetInternalSkillState(skill.Definition, skill.Level, skill.Exp);
            }
        }

        if (previous.EquippedInternalSkillId is { } equippedInternal)
        {
            rebuilt.EquipInternalSkill(equippedInternal);
        }

        foreach (var talent in previous.UnlockedTalents)
        {
            rebuilt.LearnTalent(talent);
        }

        foreach (var special in previous.GetSpecialSkills())
        {
            if (rebuilt.GetSpecialSkills().All(candidate => candidate.Id != special.Id))
            {
                rebuilt.LearnSpecialSkill(special.Definition, special.IsActive);
            }
        }

        // 身上的装备跟人走; 新模板在同一槽位自带的装备是重建时临时生成的, 直接丢弃.
        foreach (var slot in previous.EquippedItems.Keys.ToArray())
        {
            var item = previous.RemoveEquipment(slot);
            if (rebuilt.GetEquipment(slot) is not null)
            {
                rebuilt.RemoveEquipment(slot);
            }

            rebuilt.AddEquipmentInstance(item);
        }

        rebuilt.RebuildSnapshot();
        rebuilt.RestoreBattleResources();
    }
}
