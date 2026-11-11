using Game.Core.Affix;

namespace Game.Core.Battle.Talents;

public sealed record FiveElementsFormationDamageShareParameters(
    [property: NotWhiteSpace] string TalentId,
    [property: NotWhiteSpace] string Speech,
    [property: NonNegative] int Radius = 5,
    [property: Probability] double Chance = 0.5d);

internal sealed class FiveElementsFormationDamageShareHandler
    : CustomBattleEffectHandler<FiveElementsFormationDamageShareParameters, IDamageApplicationEffectContext>
{
    public override IReadOnlySet<HookTiming> SupportedTimings { get; } =
        new HashSet<HookTiming> { HookTiming.BeforeDamageApplied };

    public override void Execute(
        IDamageApplicationEffectContext context,
        FiveElementsFormationDamageShareParameters parameters)
    {
        if (context.DamageAmount <= 0 || context.Target is null) return;
        var target = context.Target;
        var participants = context.State.GetLivingUnits()
            .Where(unit => unit.Team == target.Team && unit.Id != target.Id)
            .Where(unit => unit.Position.ManhattanDistanceTo(target.Position) <= parameters.Radius)
            .Where(unit => unit.Character.HasEffectiveTalent(parameters.TalentId))
            .Where(_ => Probability.RollChance(context.Random, parameters.Chance))
            .ToList();
        if (participants.Count == 0) return;

        var share = context.DamageAmount / (participants.Count + 1);
        var targetShare = context.DamageAmount - share * participants.Count;
        foreach (var participant in participants)
        {
            context.ShareDamage(participant, share, parameters.TalentId);
            context.RequestSpeech(participant, parameters.Speech);
        }
        context.CapDamage(targetShare);
    }
}
