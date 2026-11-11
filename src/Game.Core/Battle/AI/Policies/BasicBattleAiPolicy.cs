namespace Game.Core.Battle;

internal sealed class BasicBattleAiPolicy : IBattleAiPolicy
{
    public IEnumerable<BattleTurnCandidate> GenerateCandidates(
        BattleAiEvaluationContext context,
        BattleTurnCandidateGenerator generator)
    {
        ArgumentNullException.ThrowIfNull(generator);
        return generator.Enumerate(context);
    }
}
