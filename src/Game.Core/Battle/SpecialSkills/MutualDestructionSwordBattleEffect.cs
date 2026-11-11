namespace Game.Core.Battle.SpecialSkills;

public sealed record MutualDestructionSwordBattleEffectParameters;

public sealed class MutualDestructionSwordBattleEffectHandler
    : CustomAbilityBattleEffectHandler<MutualDestructionSwordBattleEffectParameters>
{
    private const int SelfDamage = 1000;

    public override void Execute(
        IBattleAbilityEffectContext context,
        MutualDestructionSwordBattleEffectParameters parameters)
    {
        foreach (var target in context.Targets)
        {
            var damage = 1000 + (int)(target.MaxHp * context.Random.NextDouble() * 0.2d);
            context.DealDamage(target, damage);
        }

        context.PayHpCost(SelfDamage);
    }

    public override int? EstimateDamage(
        BattleAbilityDamageEstimateContext context,
        MutualDestructionSwordBattleEffectParameters parameters) =>
        1000 + (int)(context.Target.MaxHp * 0.1d);

    public override int EstimateSourceDamage(
        BattleAbilityDamageEstimateContext context,
        MutualDestructionSwordBattleEffectParameters parameters) =>
        SelfDamage;
}
