using Game.Core.Model;
using Game.Core.Model.Skills;

namespace Game.Core.Battle;

/// <param name="Targets">技能覆盖范围内的主要目标（与执行时的主要目标一致）；可能是复用缓冲区，评分器不得持有。</param>
/// <param name="Evaluation">本次决策的评估上下文，提供伤害缓存与技能执行预览所需的引擎。</param>
public sealed record BattleSkillAiContext(
    BattleState State,
    BattleUnit Source,
    SkillInstance Skill,
    GridPosition MoveDestination,
    GridPosition TargetPosition,
    IReadOnlyList<BattleUnit> Targets,
    BattleAiEvaluationContext? Evaluation = null);
