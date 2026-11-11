using Game.Core.Model.Skills;

namespace Game.Core.Battle;

public sealed partial class BattleEngine
{
    /// <summary>所有伤害的唯一入口：按描述经公共结算管线扣血。</summary>
    internal BattleDamageResolution ResolveDamage(BattleState state, BattleDamageDescriptor damage) =>
        _damageResolver.Resolve(state, damage);

    /// <summary>按技能语义判定一次命中（闪避、目标侧命中反制、来源侧破闪避），供 Judged 伤害使用。</summary>
    internal (bool IsCancelled, bool SuppressHitEffects) JudgeHit(
        BattleState state,
        BattleUnit source,
        BattleUnit target,
        SkillInstance skill)
    {
        var check = ResolveSkillHit(state, source, target, skill);
        return (check.IsCancelled, check.SuppressHitEffects);
    }
}
