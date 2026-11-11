namespace Game.Core.Battle;

/// <param name="UtilityScore">非伤害收益折算的等价气血（状态、驱散、回复、怒气、集气），正值有利。</param>
public sealed record BattleSkillAiEvaluation(
    int EnemyDamage,
    int AllyDamage,
    int EnemyKills,
    int AllyKills,
    int EnemyHitCount,
    double UtilityScore = 0d);
