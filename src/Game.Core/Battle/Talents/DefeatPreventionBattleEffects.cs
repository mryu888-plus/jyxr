using Game.Core.Affix;

namespace Game.Core.Battle.Talents;

public sealed record SurviveAtOneHpBattleEffectParameters(
    [property: NotWhiteSpace] string AbilityId,
    [property: Probability] double Chance,
    string? FloatText = null,
    string? Speech = null);

public sealed class SurviveAtOneHpBattleEffectHandler
    : CustomBattleEffectHandler<SurviveAtOneHpBattleEffectParameters, IDefeatPreventionEffectContext>
{
    public override IReadOnlySet<HookTiming> SupportedTimings { get; } =
        new HashSet<HookTiming> { HookTiming.BeforeDefeated };

    public override void Execute(
        IDefeatPreventionEffectContext context,
        SurviveAtOneHpBattleEffectParameters parameters)
    {
        if (context.IsDefeatPrevented || !Probability.RollChance(context.Random, parameters.Chance))
        {
            return;
        }

        context.TryPreventDefeat(new BattleDefeatPrevention(parameters.AbilityId)
        {
            FloatText = parameters.FloatText,
            Speech = parameters.Speech,
        });
    }
}

public sealed record QiShieldDefeatPreventionBattleEffectParameters(
    [property: NotWhiteSpace] string AbilityId,
    [property: Probability] double Chance,
    [property: Positive] int MpCostPerDamage,
    string? FloatText = null,
    string? Speech = null);

public sealed class QiShieldDefeatPreventionBattleEffectHandler
    : CustomBattleEffectHandler<QiShieldDefeatPreventionBattleEffectParameters, IDefeatPreventionEffectContext>
{
    public override IReadOnlySet<HookTiming> SupportedTimings { get; } =
        new HashSet<HookTiming> { HookTiming.BeforeDefeated };

    public override void Execute(
        IDefeatPreventionEffectContext context,
        QiShieldDefeatPreventionBattleEffectParameters parameters)
    {
        var mpCost = checked(context.IncomingDamageAmount * parameters.MpCostPerDamage);
        if (context.IsDefeatPrevented ||
            context.Unit.Mp < mpCost ||
            !Probability.RollChance(context.Random, parameters.Chance))
        {
            return;
        }

        context.TryPreventDefeat(new BattleDefeatPrevention(parameters.AbilityId)
        {
            MpCost = mpCost,
            FloatText = parameters.FloatText,
            Speech = parameters.Speech,
        });
    }
}

public sealed record EndlessFightingSpiritBattleEffectParameters(
    [property: NotWhiteSpace] string AbilityId,
    [property: NotWhiteSpace] string GuaranteedFirstTalentId,
    [property: Probability] double Chance,
    string? FloatText = null,
    string? Speech = null);

public sealed class EndlessFightingSpiritBattleEffectHandler
    : CustomBattleEffectHandler<EndlessFightingSpiritBattleEffectParameters, IDefeatPreventionEffectContext>
{
    public override IReadOnlySet<HookTiming> SupportedTimings { get; } =
        new HashSet<HookTiming> { HookTiming.BeforeDefeated };

    public override void Execute(
        IDefeatPreventionEffectContext context,
        EndlessFightingSpiritBattleEffectParameters parameters)
    {
        if (context.IsDefeatPrevented)
        {
            return;
        }

        var guaranteed = context.GetAbilityUsageCount(parameters.AbilityId) == 0 &&
            context.Unit.Character.HasTalent(parameters.GuaranteedFirstTalentId);
        if (!guaranteed && !Probability.RollChance(context.Random, parameters.Chance))
        {
            return;
        }

        context.TryPreventDefeat(new BattleDefeatPrevention(parameters.AbilityId)
        {
            // 以 1 HP 存活后恢复到满。
            HpRecovery = Math.Max(0, context.Unit.MaxHp - 1),
            MpRecovery = Math.Max(0, context.Unit.MaxMp - context.Unit.Mp),
            Rage = BattleUnit.MaxRage,
            FloatText = parameters.FloatText,
            Speech = parameters.Speech,
        });
    }
}
