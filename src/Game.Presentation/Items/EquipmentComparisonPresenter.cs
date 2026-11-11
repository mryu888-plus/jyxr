using System.Text.Json;
using Game.Core.Affix;
using Game.Core.Definitions;
using Game.Core.Model;
using Game.Core.Model.Character;
using Game.Core.Serialization;

namespace Game.Presentation.Items;

public enum EquipmentAffixChange
{
    /// <summary>新装备有、当前装备没有。</summary>
    Gained,
    /// <summary>当前装备有、新装备没有，换上后会失去。</summary>
    Lost,
    /// <summary>两件都有。</summary>
    Kept,
}

public sealed record EquipmentAffixLine(AffixDefinition Affix, EquipmentAffixChange Change);

public sealed record EquipmentStatDelta(StatType Stat, double Current, double Candidate)
{
    public double Delta => Candidate - Current;
}

/// <summary>选装时候选装备与该槽位当前装备的比较结果。</summary>
public sealed record EquipmentComparison(
    EquipmentDefinition Candidate,
    EquipmentInstance? Current,
    IReadOnlyList<EquipmentStatDelta> StatDeltas,
    IReadOnlyList<EquipmentAffixLine> AffixLines)
{
    /// <summary>槽位已有装备时，换装需要玩家确认替换。</summary>
    public bool RequiresReplacementConfirmation => Current is not null;

    public IEnumerable<EquipmentAffixLine> GainedAffixes => AffixLines.Where(static line => line.Change == EquipmentAffixChange.Gained);

    public IEnumerable<EquipmentAffixLine> LostAffixes => AffixLines.Where(static line => line.Change == EquipmentAffixChange.Lost);
}

/// <summary>
/// 装备选择的比较规则：固定数值（加法属性）逐项比较，其余词条按内容比对标记新增/失去/保留。
/// 只依赖 Core；文案由宿主用 AffixFormatter 格式化。
/// </summary>
public static class EquipmentComparisonPresenter
{
    public static EquipmentComparison Compare(CharacterInstance character, InventoryEntry candidate)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(candidate);
        var (definition, candidateAffixes) = candidate switch
        {
            EquipmentInstanceInventoryEntry instance => (instance.Equipment.Definition, instance.Equipment.Affixes),
            StackInventoryEntry { Item: EquipmentDefinition equipment } => (equipment, equipment.Affixes),
            _ => throw new ArgumentException("Candidate entry is not equipment.", nameof(candidate)),
        };

        var current = character.GetEquipment(definition.SlotType);
        var currentAffixes = current?.Affixes ?? [];
        return new EquipmentComparison(
            definition,
            current,
            CompareStats(currentAffixes, candidateAffixes),
            CompareAffixes(currentAffixes, candidateAffixes));
    }

    private static IReadOnlyList<EquipmentStatDelta> CompareStats(
        IReadOnlyList<AffixDefinition> current,
        IReadOnlyList<AffixDefinition> candidate)
    {
        var currentTotals = SumAdditiveStats(current);
        var candidateTotals = SumAdditiveStats(candidate);
        return currentTotals.Keys.Union(candidateTotals.Keys)
            .OrderBy(static stat => stat)
            .Select(stat => new EquipmentStatDelta(
                stat,
                currentTotals.GetValueOrDefault(stat),
                candidateTotals.GetValueOrDefault(stat)))
            .Where(static delta => delta.Delta != 0d)
            .ToArray();
    }

    private static Dictionary<StatType, double> SumAdditiveStats(IEnumerable<AffixDefinition> affixes)
    {
        var totals = new Dictionary<StatType, double>();
        foreach (var affix in affixes.OfType<StatModifierAffix>())
        {
            if (affix.Value.Op != ModifierOp.Add)
            {
                continue;
            }

            totals[affix.Stat] = totals.GetValueOrDefault(affix.Stat) + affix.Value.Delta;
        }

        return totals;
    }

    private static IReadOnlyList<EquipmentAffixLine> CompareAffixes(
        IReadOnlyList<AffixDefinition> current,
        IReadOnlyList<AffixDefinition> candidate)
    {
        // 来源信息（SourceKind/SourceId）不参与序列化，按词条内容比对。
        var remainingCurrent = current.Select(affix => (Affix: affix, Key: Key(affix))).ToList();
        var lines = new List<EquipmentAffixLine>();
        foreach (var affix in candidate)
        {
            var key = Key(affix);
            var index = remainingCurrent.FindIndex(entry => entry.Key == key);
            if (index >= 0)
            {
                remainingCurrent.RemoveAt(index);
                lines.Add(new EquipmentAffixLine(affix, EquipmentAffixChange.Kept));
            }
            else
            {
                lines.Add(new EquipmentAffixLine(affix, EquipmentAffixChange.Gained));
            }
        }

        lines.AddRange(remainingCurrent.Select(static entry => new EquipmentAffixLine(entry.Affix, EquipmentAffixChange.Lost)));
        return lines;
    }

    private static string Key(AffixDefinition affix) => JsonSerializer.Serialize(affix, GameJson.Default);
}
