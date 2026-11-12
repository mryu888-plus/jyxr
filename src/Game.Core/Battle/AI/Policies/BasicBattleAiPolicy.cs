namespace Game.Core.Battle;

internal sealed class BasicBattleAiPolicy : IBattleAiPolicy
{
    public IEnumerable<BattleTurnCandidate> GenerateCandidates(
        BattleAiEvaluationContext context,
        BattleTurnCandidateGenerator generator)
    {
        return generator.Enumerate(context);
    }
}
