namespace Game.Core.Battle;

public interface IBattleAiPolicy
{
    /// <summary>流式产出候选；调用方只遍历一次并在遍历中择优。</summary>
    IEnumerable<BattleTurnCandidate> GenerateCandidates(
        BattleAiEvaluationContext context,
        BattleTurnCandidateGenerator generator);
}
