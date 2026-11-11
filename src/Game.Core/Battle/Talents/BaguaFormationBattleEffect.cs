using Game.Core.Affix;

namespace Game.Core.Battle.Talents;

public sealed record BaguaFormationBattleEffectParameters(
    [property: NonNegative] int Radius = 5,
    [property: Probability] double TransferFactor = 0.8d,
    string? Speech = null);

internal sealed class BaguaFormationBattleEffectHandler
    : CustomBattleEffectHandler<BaguaFormationBattleEffectParameters, IDamageApplicationEffectContext>
{
    public override IReadOnlySet<HookTiming> SupportedTimings { get; } =
        new HashSet<HookTiming> { HookTiming.BeforeDamageApplied };

    public override void Execute(
        IDamageApplicationEffectContext context,
        BaguaFormationBattleEffectParameters parameters)
    {
        if (context.DamageAmount <= 0)
        {
            return;
        }

        var candidates = context.State.GetLivingUnits()
            .Where(unit => unit.Team == context.Unit.Team)
            .Where(unit => unit.Id != context.Unit.Id)
            .Where(unit => unit.Position.ManhattanDistanceTo(context.Unit.Position) <= parameters.Radius)
            .ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        var defender = candidates[context.Random.Next(0, candidates.Count)];
        var transferredDamage = (int)(context.DamageAmount * parameters.TransferFactor);
        var retainedDamage = (int)(context.DamageAmount * (1d - parameters.TransferFactor));

        // 转移部分是同一次攻击的另一个承伤分量，保留攻击者、技能与归因。
        context.TransferDamage(defender, transferredDamage, "八卦阵");
        context.CapDamage(retainedDamage);
        if (!string.IsNullOrWhiteSpace(parameters.Speech))
        {
            context.RequestSpeech(defender, parameters.Speech);
        }
    }
}
