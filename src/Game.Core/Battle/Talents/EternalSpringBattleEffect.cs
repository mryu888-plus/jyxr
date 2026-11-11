using Game.Core.Affix;

namespace Game.Core.Battle.Talents;

public sealed record EternalSpringBattleEffectParameters(
    [property: NotWhiteSpace] string EnhancedTalentId,
    [property: Probability] double Chance,
    [property: Probability] double EnhancedChance,
    [property: Probability] double RecoveryFactor,
    string FloatText);

internal sealed class EternalSpringBattleEffectHandler
    : CustomBattleEffectHandler<EternalSpringBattleEffectParameters, IDamageApplicationEffectContext>
{
    public override IReadOnlySet<HookTiming> SupportedTimings { get; } =
        new HashSet<HookTiming> { HookTiming.BeforeDamageApplied };

    public override void Execute(
        IDamageApplicationEffectContext context,
        EternalSpringBattleEffectParameters parameters)
    {
        if (context.DamageAmount <= 0)
        {
            return;
        }

        var chance = context.Unit.Character.HasEffectiveTalent(parameters.EnhancedTalentId)
            ? parameters.EnhancedChance
            : parameters.Chance;
        if (!Probability.RollChance(context.Random, chance))
        {
            return;
        }

        var recovery = (int)(context.DamageAmount * parameters.RecoveryFactor);
        var actual = context.ApplyHpRecovery(context.Unit, recovery, "不老长春");
        context.RequestFloatText(
            context.Unit,
            $"{parameters.FloatText}{actual}",
            BattleFloatTextStyle.Recovery);
        context.CancelDamage(suppressHitEffects: true);
    }
}
