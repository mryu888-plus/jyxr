using Game.Core.Affix;
using Game.Core.Model;

namespace Game.Core.Battle;

/// <summary>
/// 战斗内物品可用性：规则执行（<see cref="BattleEngine.UseItem"/>）与 UI（物品按钮、目标高亮）共用同一套判断，
/// 宿主不重复难度、冷却与天赋判断。物品目标规则与具体物品无关，所以只按行动者与目标判断。
/// </summary>
public sealed record BattleItemUseAvailability(
    IReadOnlyList<BattleUnit> AvailableTargets,
    BattleCommandFailure? BlockingFailure)
{
    /// <summary>至少有一个合法目标时才允许打开物品面板。</summary>
    public bool CanOpen => AvailableTargets.Count > 0;
}

public static class BattleItemAvailability
{
    public const int AllyItemRange = 2;

    /// <summary>物品冷却在普通难度下按规则设置关闭。</summary>
    public static bool IsItemCooldownEnabled(BattleRuleSettings ruleSettings)
    {
        ArgumentNullException.ThrowIfNull(ruleSettings);
        return !ruleSettings.EnableDifficultyItemCooldownRules ||
            ruleSettings.Difficulty != GameDifficulty.Normal;
    }

    /// <summary>判断 <paramref name="unit"/> 能否对 <paramref name="target"/> 使用物品；可用时返回 null。</summary>
    public static BattleCommandFailure? ValidateTarget(BattleState state, BattleUnit unit, BattleUnit? target)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(unit);
        if (target is null || !target.IsAlive)
        {
            return new BattleCommandFailure(BattleCommandFailureReason.InvalidItemTarget);
        }

        if (target.Id != unit.Id)
        {
            if (state.AreEnemies(unit, target))
            {
                return new BattleCommandFailure(BattleCommandFailureReason.ItemCannotTargetEnemy);
            }

            if (!unit.HasTrait(TraitId.CanUseItemOnAlly))
            {
                return new BattleCommandFailure(BattleCommandFailureReason.ItemCannotTargetAlly);
            }

            if (unit.Position.ManhattanDistanceTo(target.Position) > AllyItemRange)
            {
                return new BattleCommandFailure(BattleCommandFailureReason.AllyItemTargetOutOfRange);
            }
        }

        if (IsItemCooldownEnabled(state.RuleSettings) &&
            target.ItemCooldown > 0 &&
            !unit.HasTrait(TraitId.IgnoreItemCooldown))
        {
            return new BattleCommandFailure(BattleCommandFailureReason.ItemOnCooldown, target.ItemCooldown);
        }

        return null;
    }

    /// <summary>
    /// 列出当前可用的物品目标。没有任何可用目标时给出阻止原因：只能对自己用药时就是自己的原因（通常是冷却），
    /// 否则优先报告自己的原因，再报告第一个候选队友的原因。
    /// </summary>
    public static BattleItemUseAvailability Evaluate(BattleState state, BattleUnit unit)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(unit);
        var candidates = new List<BattleUnit> { unit };
        if (unit.HasTrait(TraitId.CanUseItemOnAlly))
        {
            candidates.AddRange(state.Units.Where(candidate =>
                candidate.Id != unit.Id &&
                candidate.IsAlive &&
                !state.AreEnemies(unit, candidate) &&
                unit.Position.ManhattanDistanceTo(candidate.Position) <= AllyItemRange));
        }

        var available = new List<BattleUnit>();
        BattleCommandFailure? firstFailure = null;
        foreach (var candidate in candidates)
        {
            var failure = ValidateTarget(state, unit, candidate);
            if (failure is null)
            {
                available.Add(candidate);
            }
            else
            {
                firstFailure ??= failure;
            }
        }

        return new BattleItemUseAvailability(available, available.Count == 0 ? firstFailure : null);
    }
}
