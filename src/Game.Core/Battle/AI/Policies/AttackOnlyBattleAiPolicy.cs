namespace Game.Core.Battle;

internal sealed class AttackOnlyBattleAiPolicy : IBattleAiPolicy
{
    public IEnumerable<BattleTurnCandidate> GenerateCandidates(
        BattleAiEvaluationContext context,
        BattleTurnCandidateGenerator generator)
    {
        return GenerateCore(context, generator);
    }

    private static IEnumerable<BattleTurnCandidate> GenerateCore(
        BattleAiEvaluationContext context,
        BattleTurnCandidateGenerator generator)
    {
        var hasAttack = false;
        foreach (var candidate in generator.Enumerate(
                     context,
                     new BattleTurnCandidateGenerationOptions(
                         AllowSkillCandidates: true,
                         AllowRestCandidates: false,
                         AllowMovement: true)))
        {
            hasAttack = true;
            yield return candidate;
        }

        if (hasAttack)
        {
            yield break;
        }

        foreach (var candidate in generator.Enumerate(
                     context,
                     new BattleTurnCandidateGenerationOptions(
                         AllowSkillCandidates: false,
                         AllowRestCandidates: true,
                         AllowMovement: true)))
        {
            yield return candidate;
        }
    }
}
