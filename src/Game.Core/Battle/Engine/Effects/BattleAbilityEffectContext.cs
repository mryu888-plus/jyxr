using Game.Core.Abstractions;
using Game.Core.Model;
using Game.Core.Model.Skills;

namespace Game.Core.Battle;

internal sealed class BattleAbilityEffectContext(
    BattleEngine engine,
    BattleState state,
    BattleUnit source,
    IReadOnlyList<BattleUnit> targets,
    SkillInstance skill,
    IRandomService random) : IBattleAbilityEffectContext
{
    public BattleUnit Source { get; } = source;

    public SkillInstance Skill { get; } = skill;

    public IReadOnlyList<BattleUnit> Targets { get; } = targets;

    public IRandomService Random { get; } = random;

    public BattleExecutionScope? ExecutionScope => state.CurrentExecutionScope;

    public bool IsCellAvailable(GridPosition position, BattleUnit movingUnit) =>
        state.Grid.IsWalkable(position) && !state.IsOccupied(position, movingUnit.Id);

    public bool TryRelocate(BattleUnit target, GridPosition destination) =>
        engine.TryRelocateByEffect(state, Source, target, destination);

    public BattleRelocationResult Push(BattleUnit target, int distance) =>
        engine.RelocationResolver.Push(state, Source, target, distance);

    public BattleRelocationResult Pull(BattleUnit target, int distance) =>
        engine.RelocationResolver.Pull(state, Source, target, distance);

    public BattleRelocationResult SwapWithSource(BattleUnit target) =>
        engine.RelocationResolver.Swap(state, Source, target).Second;

    public bool ApplyBuff(BattleUnit target, string buffId, int level, int duration) =>
        engine.ApplyBuffByEffect(state, Source, target, buffId, level, duration);

    public int ApplyHpRecovery(BattleUnit target, int amount) =>
        engine.ApplyDirectHpRecovery(state, Source, target, amount);

    public int DealDamage(
        BattleUnit target,
        int amount,
        BattleDamageMitigation mitigation = BattleDamageMitigation.IgnoreDefence,
        BattleDamageHitMode hitMode = BattleDamageHitMode.Judged) =>
        engine.ResolveDamage(state, BattleDamageDescriptor.ForAbility(
            Source,
            target,
            amount,
            Skill,
            hitMode,
            mitigation) with
        {
            Detail = Skill.Id,
        }).ActualAmount;

    public int PayHpCost(int amount) =>
        engine.ResolveDamage(state, BattleDamageDescriptor.ForSelfCost(Source, amount, Skill) with
        {
            Detail = Skill.Id,
        }).ActualAmount;
}
