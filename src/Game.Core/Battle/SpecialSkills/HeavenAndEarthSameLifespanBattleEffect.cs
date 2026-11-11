namespace Game.Core.Battle.SpecialSkills;

public sealed record HeavenAndEarthSameLifespanBattleEffectParameters;

public sealed class HeavenAndEarthSameLifespanBattleEffectHandler
    : CustomAbilityBattleEffectHandler<HeavenAndEarthSameLifespanBattleEffectParameters>
{
    public override void Execute(
        IBattleAbilityEffectContext context,
        HeavenAndEarthSameLifespanBattleEffectParameters parameters)
    {
        var sourceHp = context.Source.Hp;
        foreach (var target in context.Targets)
        {
            var damage = (int)(sourceHp * (0.5d + context.Random.NextDouble() * 0.5d));
            context.DealDamage(target, damage);
        }

        context.PayHpCost(sourceHp);
    }

    public override int? EstimateDamage(
        BattleAbilityDamageEstimateContext context,
        HeavenAndEarthSameLifespanBattleEffectParameters parameters) =>
        (int)(context.Source.Hp * 0.75d);

    public override int EstimateSourceDamage(
        BattleAbilityDamageEstimateContext context,
        HeavenAndEarthSameLifespanBattleEffectParameters parameters) =>
        context.Source.Hp;
}
