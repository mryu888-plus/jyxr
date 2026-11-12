namespace Game.Core.Battle;

internal sealed class RestOnlyBattleAiPolicy : IBattleAiPolicy
{
    public IEnumerable<BattleTurnCandidate> GenerateCandidates(
        BattleAiEvaluationContext context,
        BattleTurnCandidateGenerator generator)
    {
        return generator.Enumerate(
            context,
            new BattleTurnCandidateGenerationOptions(
                AllowSkillCandidates: false,
                AllowRestCandidates: true,
                AllowMovement: false));
    }
}
