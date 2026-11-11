using Game.Core.Affix;
using Game.Core.Model.Skills;

namespace Game.Core.Battle;

public sealed partial class BattleEngine
{
    internal int ApplyDirectHpRecovery(
        BattleState state,
        BattleUnit source,
        BattleUnit target,
        int amount)
    {
        var actual = _recoveryResolver.Apply(state, source, target, BattleRecoveryKind.Hp, amount).ActualAmount;
        AddMessage(state, new BattleFact(BattleFactKind.Healed, target.Id, detail: actual.ToString()));
        return actual;
    }

    internal bool ApplyBuffByEffect(
        BattleState state,
        BattleUnit source,
        BattleUnit target,
        string buffId,
        int level,
        int duration,
        HookTiming? timing = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(buffId);
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(duration);

        return _battleBuffResolver.Apply(
            state,
            source,
            target,
            _battleBuffResolver.Resolve(buffId),
            level,
            duration,
            timing);
    }

    internal BattleHookContext TriggerHooks(
        BattleState state,
        HookTiming timing,
        BattleUnit unit,
        Action<BattleHookContext>? configure = null,
        BattleHookExecutionMode executionMode = BattleHookExecutionMode.Execute,
        bool recordEvents = true,
        Func<HookAffix, bool>? hookFilter = null)
        => _hookRunner.Run(state, timing, unit, configure, executionMode, recordEvents, hookFilter);

    private int ResolveSkillMpCostPreview(BattleState state, BattleUnit unit, SkillInstance skill) =>
        ResolveSkillMpCost(state, unit, skill, BattleHookExecutionMode.Preview);

    private int ResolveSkillMpCostExecute(BattleState state, BattleUnit unit, SkillInstance skill) =>
        ResolveSkillMpCost(state, unit, skill, BattleHookExecutionMode.Execute);

    private int ResolveSkillMpCost(
        BattleState state,
        BattleUnit unit,
        SkillInstance skill,
        BattleHookExecutionMode executionMode)
    {
        var context = TriggerHooks(
            state,
            HookTiming.BeforeSkillCost,
            unit,
            hookContext =>
            {
                hookContext.Skill = skill;
                hookContext.MpCost = skill.MpCost;
            },
            executionMode,
            recordEvents: false);
        return Math.Max(0, context.MpCost ?? skill.MpCost);
    }

}
