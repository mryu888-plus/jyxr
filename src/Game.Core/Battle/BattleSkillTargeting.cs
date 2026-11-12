using Game.Core.Affix;
using Game.Core.Definitions.Skills;
using Game.Core.Model;
using Game.Core.Model.Skills;

namespace Game.Core.Battle;

public static class BattleSkillTargeting
{
    public static int ResolveEffectiveCastSize(BattleUnit source, SkillInstance skill)
    {
        var castSize = source.GetSkillTargetingValue(
            skill,
            SkillTargetingField.CastSize,
            skill.CastSize);
        return ApplyBlindPenalty(source, castSize);
    }

    public static int ResolveEffectiveImpactSize(BattleUnit source, SkillInstance skill)
    {
        var impactSize = source.GetSkillTargetingValue(
            skill,
            SkillTargetingField.ImpactSize,
            skill.ImpactSize);
        return ApplyBlindPenalty(source, impactSize);
    }

    public static IReadOnlyList<BattleUnit> ResolveEffectiveTargets(
        BattleState state,
        BattleUnit source,
        SkillInstance skill,
        IReadOnlySet<GridPosition> impactedPositions)
    {
        return state.Units
            .Where(targetUnit => targetUnit.IsAlive && impactedPositions.Contains(targetUnit.Position))
            .Where(targetUnit => CanAffect(state, source, targetUnit, skill))
            .ToList();
    }

    /// <summary>
    /// 覆盖范围内的存活单位是否成为技能的主要目标；技能执行与 AI 预览共用这一判定。
    /// CanImpactSelf 表达“施法者本人一定承受这个技能”（legacy HitSelf）：施法者在覆盖范围内时直接成为目标，
    /// 不再经过阵营、难度和 AvoidFriendlyFire 的友伤过滤；其它友军仍按友伤规则判定。
    /// </summary>
    public static bool CanAffect(
        BattleState state,
        BattleUnit source,
        BattleUnit target,
        SkillInstance skill) =>
        IsCaster(source, target)
            ? skill.CanImpactSelf
            : ShouldIncludeTarget(state, source, target, skill);

    private static bool IsCaster(BattleUnit source, BattleUnit target) =>
        string.Equals(target.Id, source.Id, StringComparison.Ordinal);

    private static bool ShouldIncludeTarget(
        BattleState state,
        BattleUnit source,
        BattleUnit target,
        SkillInstance skill)
    {
        if (skill is LegendSkillInstance)
        {
            return state.AreEnemies(source, target);
        }

        var isOffensive = skill is ExternalSkillInstance or FormSkillInstance ||
                          skill is SpecialSkillInstance
                          {
                              Definition.Intent: SpecialSkillIntent.Offensive
                          };
        if (!isOffensive || state.AreEnemies(source, target))
        {
            return true;
        }

        return state.RuleSettings.Difficulty != GameDifficulty.Normal &&
               !source.HasTrait(TraitId.AvoidFriendlyFire);
    }

    public static IReadOnlySet<GridPosition> EnumerateCastTargets(
        GridPosition sourcePosition,
        int castSize,
        bool canCastAtSelf,
        BattleGrid grid)
    {
        var targets = new HashSet<GridPosition>();
        for (var y = sourcePosition.Y - castSize; y <= sourcePosition.Y + castSize; y++)
        {
            for (var x = sourcePosition.X - castSize; x <= sourcePosition.X + castSize; x++)
            {
                var position = new GridPosition(x, y);
                if (CanCastAt(sourcePosition, position, castSize, canCastAtSelf, grid))
                {
                    targets.Add(position);
                }
            }
        }

        return targets;
    }

    public static bool CanCastAt(
        GridPosition sourcePosition,
        GridPosition targetPosition,
        int castSize,
        bool canCastAtSelf,
        BattleGrid grid)
    {
        return grid.Contains(targetPosition) &&
               sourcePosition.ManhattanDistanceTo(targetPosition) <= castSize &&
               (sourcePosition != targetPosition || canCastAtSelf);
    }

    private static int ApplyBlindPenalty(BattleUnit source, int originalSize)
    {
        if (originalSize <= 0 ||
            source.HasTrait(TraitId.MindEye) ||
            source.TryGetBuff(BattleContentIds.Blind) is not { } blind)
        {
            return originalSize;
        }

        var penalty = (int)(blind.Level * 1.5d);
        return Math.Max(1, originalSize - penalty);
    }

}
