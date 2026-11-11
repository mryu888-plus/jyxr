using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Game.Core.Affix;
using Game.Core.Definitions;

namespace Game.Core.Battle.Talents;

public sealed record RandomDebuffBattleEffectParameters(
    [property: JsonPropertyName("buffIds"), Required, MinLength(1)] IReadOnlyList<ContentReference<BuffDefinition>> BuffReferences,
    [property: NonNegative] int Level,
    [property: Positive] int Duration)
{
    [JsonIgnore]
    public IReadOnlyList<string> BuffIds => BuffReferences.Select(static reference => reference.Id).ToList();
}

internal sealed class RandomDebuffBattleEffectHandler
    : CustomBattleEffectHandler<RandomDebuffBattleEffectParameters, IHitConfirmedEffectContext>
{
    public override IReadOnlySet<HookTiming> SupportedTimings { get; } =
        new HashSet<HookTiming> { HookTiming.OnHitConfirmed };

    public override void Validate(RandomDebuffBattleEffectParameters parameters)
    {
        if (parameters.BuffIds.Distinct(StringComparer.Ordinal).Count() != parameters.BuffIds.Count)
            throw new InvalidOperationException("Random debuff IDs cannot contain duplicate values.");
    }

    public override void Execute(
        IHitConfirmedEffectContext context,
        RandomDebuffBattleEffectParameters parameters)
    {
        if (context.Source is null || context.Target is null ||
            !context.State.AreEnemies(context.Source, context.Target))
        {
            return;
        }

        var buffId = parameters.BuffIds[context.Random.Next(0, parameters.BuffIds.Count)];
        context.ApplyBuff(context.Target, buffId, parameters.Level, parameters.Duration);
    }
}
