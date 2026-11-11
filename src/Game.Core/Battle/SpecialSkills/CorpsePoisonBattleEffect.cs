namespace Game.Core.Battle.SpecialSkills;

public sealed record CorpsePoisonBattleEffectParameters;

public sealed class CorpsePoisonBattleEffectHandler
    : CustomAbilityBattleEffectHandler<CorpsePoisonBattleEffectParameters>
{
    public override void Execute(
        IBattleAbilityEffectContext context,
        CorpsePoisonBattleEffectParameters parameters)
    {
        foreach (var target in context.Targets)
        {
            if (target.TryGetBuff(BattleContentIds.Poison) is not { } poison)
            {
                continue;
            }

            var baseDamage = (poison.Level + 1) * poison.RemainingTurns * 50;
            var damage = (int)(baseDamage * (1d + context.Random.NextDouble()));
            context.DealDamage(target, damage);
        }
    }

    public override int? EstimateDamage(
        BattleAbilityDamageEstimateContext context,
        CorpsePoisonBattleEffectParameters parameters)
    {
        if (context.Target.TryGetBuff(BattleContentIds.Poison) is not { } poison)
        {
            return 0;
        }

        return (int)((poison.Level + 1) * poison.RemainingTurns * 50 * 1.5d);
    }
}
