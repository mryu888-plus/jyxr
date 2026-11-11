namespace Game.Core.Battle;

internal sealed class RestOnlyBattleAiPolicy : IBattleAiPolicy
{
    public IEnumerable<BattleTurnCandidate> GenerateCandidates(
        BattleAiEvaluationContext context,
        BattleTurnCandidateGenerator generator)
    {
        ArgumentNullException.ThrowIfNull(generator);

        return generator.Enumerate(
            context,
            new BattleTurnCandidateGenerationOptions(
                AllowSkillCandidates: false,
                AllowRestCandidates: true,
                AllowMovement: false));
    }
}
