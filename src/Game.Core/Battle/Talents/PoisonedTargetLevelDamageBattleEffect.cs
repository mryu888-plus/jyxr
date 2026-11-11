using Game.Core.Definitions;
using System.Text.Json.Serialization;
using Game.Core.Affix;

namespace Game.Core.Battle.Talents;

public sealed record PoisonedTargetLevelDamageBattleEffectParameters(
    [property: JsonPropertyName("buffId")] ContentReference<BuffDefinition> BuffReference,
    [property: NonNegative] double MaximumBonus,
    [property: Positive] int ReferenceLevel)
{
    [JsonIgnore]
    public string BuffId => BuffReference.Id;
}

internal sealed class PoisonedTargetLevelDamageBattleEffectHandler
    : CustomBattleEffectHandler<PoisonedTargetLevelDamageBattleEffectParameters, IDamageCalculationEffectContext>
{
    public override IReadOnlySet<HookTiming> SupportedTimings { get; } =
        new HashSet<HookTiming> { HookTiming.BeforeDamageCalculation };

    public override void Execute(
        IDamageCalculationEffectContext context,
        PoisonedTargetLevelDamageBattleEffectParameters parameters)
    {
        if (context.Source is null || context.Target is null ||
            !string.Equals(context.Unit.Id, context.Source.Id, StringComparison.Ordinal) ||
            !context.Target.HasBuff(parameters.BuffId))
        {
            return;
        }

        TalentDamageModifier.MultiplyAttack(
            context,
            1d + parameters.MaximumBonus * context.Source.Character.Level / parameters.ReferenceLevel);
    }
}
