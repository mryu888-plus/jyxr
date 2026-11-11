using Game.Core.Affix;

namespace Game.Core.Battle.Talents;

public sealed record ShiftingStarsReflectionBattleEffectParameters(
    [property: NotWhiteSpace] string FamilyTalentId,
    [property: Probability] double Chance,
    [property: Probability] double FamilyChance,
    [property: Probability] double DamageFactor,
    string FloatText,
    string Speech);

internal sealed class ShiftingStarsReflectionBattleEffectHandler
    : CustomBattleEffectHandler<ShiftingStarsReflectionBattleEffectParameters, IDamageApplicationEffectContext>
{
    public override IReadOnlySet<HookTiming> SupportedTimings { get; } =
        new HashSet<HookTiming> { HookTiming.BeforeDamageApplied };

    public override void Execute(
        IDamageApplicationEffectContext context,
        ShiftingStarsReflectionBattleEffectParameters parameters)
    {
        var source = context.Source;
        if (source is null || context.DamageAmount <= 0 ||
            !context.State.AreEnemies(context.Unit, source))
        {
            return;
        }

        var chance = context.Unit.Character.HasTalent(parameters.FamilyTalentId)
            ? parameters.FamilyChance
            : parameters.Chance;
        if (!Probability.RollChance(context.Random, chance))
        {
            return;
        }

        var reflectedDamage = (int)(context.DamageAmount * parameters.DamageFactor);
        if (reflectedDamage > 0)
        {
            context.ReflectDamage(source, reflectedDamage, "斗转星移");
        }

        context.RequestFloatText(context.Unit, parameters.FloatText, BattleFloatTextStyle.Special);
        context.RequestSpeech(context.Unit, parameters.Speech);
        context.CancelDamage(suppressHitEffects: true);
    }
}
