using Game.AiBenchmarks;
using Game.Core.Battle;

namespace Game.Tests;

/// <summary>
/// 战斗 AI 决策回归: 固定战场 (tests/Game.AiBenchmarks/AiBattleScenarios.cs) 上每个单位的决策
/// 必须与优化前记录的基线一致, 保证候选生成/评估重构不改变排序语义。
/// 基线由 Game.AiBenchmarks 在重构前输出; 有意改变 AI 规则时需重新生成。
/// </summary>
public sealed class BattleAiRegressionTests
{
    private static readonly IReadOnlyDictionary<string, string[]> ExpectedDecisions = new Dictionary<string, string[]>
    {
        ["small"] =
        [
            "small-t1-0>(0,4) cast cross_palm@(3,4)",
            "small-t2-0>(0,0) cast cross_palm@(0,3)",
            "small-t2-1>(3,0) cast fan_sword@(3,1)",
        ],
        ["regular"] =
        [
            "regular-t1-0>(4,7) cast line_sword@(5,7)",
            "regular-t1-1>(0,5) Rest",
            "regular-t1-2>(0,6) Rest",
            "regular-t1-3>(3,0) cast square_blade@(5,0)",
            "regular-t2-0>(10,7) Rest",
            "regular-t2-1>(4,4) cast fan_sword@(3,4)",
            "regular-t2-2>(10,6) Rest",
            "regular-t2-3>(4,1) cast line_sword@(3,1)",
        ],
        ["dense"] =
        [
            "dense-t1-0>(6,3) cast star_staff@(10,3)",
            "dense-t1-1>(6,3) cast star_staff@(10,3)",
            "dense-t1-2>(0,5) Rest",
            "dense-t1-3>(5,6) cast fan_sword@(6,6)",
            "dense-t1-4>(10,7) cast line_sword@(11,7)",
            "dense-t1-5>(0,0) Rest",
            "dense-t1-6>(7,1) cast star_staff@(11,1)",
            "dense-t1-7>(6,11) cast star_staff@(10,11)",
            "dense-t2-0>(10,7) cast star_staff@(6,7)",
            "dense-t2-1>(7,1) cast x_sword@(4,2)",
            "dense-t2-2>(11,7) cast line_sword@(10,7)",
            "dense-t2-3>(4,8) cast star_staff@(4,4)",
            "dense-t2-4>(15,0) Rest",
            "dense-t2-5>(6,4) cast x_sword@(2,4)",
            "dense-t2-6>(15,0) Rest",
            "dense-t2-7>(15,3) Rest",
        ],
    };

    public static IEnumerable<object[]> ScenarioNames =>
        AiBattleScenarios.All.Select(static scenario => new object[] { scenario.Name });

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public void Decide_MatchesRecordedBaseline(string scenarioName)
    {
        var scenario = AiBattleScenarios.All.Single(candidate => candidate.Name == scenarioName);
        var actorIds = AiBattleScenarios.Create(scenario).Units.Select(static unit => unit.Id).ToArray();

        var decisions = actorIds.Select(actorId => Decide(scenario, actorId)).ToArray();

        Assert.Equal(ExpectedDecisions[scenarioName], decisions);
    }

    /// <summary>
    /// 分配量基准: 优化前常规战场每次决策约 13.8 MB, 密集战场约 23.4 MB; 现在约 1 MB。
    /// 上限留足余量, 只拦截“回到每个候选都分配集合/重复估算伤害”这类退化。
    /// </summary>
    [Theory]
    [InlineData("regular", 4_000_000)]
    [InlineData("dense", 5_000_000)]
    public void Decide_StaysWithinAllocationBudget(string scenarioName, long maxBytesPerDecision)
    {
        var scenario = AiBattleScenarios.All.Single(candidate => candidate.Name == scenarioName);
        var actorIds = AiBattleScenarios.Create(scenario).Units.Select(static unit => unit.Id).ToArray();
        long total = 0;
        foreach (var actorId in actorIds)
        {
            var instance = AiBattleScenarios.Create(scenario);
            var engine = new BattleEngine(random: new SeededRandomService(17));
            instance.State.GetUnit(actorId).ActionGauge = 100;
            engine.BeginAction(instance.State, actorId);
            var agent = new BasicEnemyBattleAgent(
                new BattleTurnCandidateGenerator(engine),
                new BattleAiPolicyResolver(_ => 10));
            agent.Decide(instance.State, actorId);

            var before = GC.GetAllocatedBytesForCurrentThread();
            agent.Decide(instance.State, actorId);
            total += GC.GetAllocatedBytesForCurrentThread() - before;
        }

        var average = total / actorIds.Length;
        Assert.True(average <= maxBytesPerDecision, $"average allocation {average:N0} B exceeds {maxBytesPerDecision:N0} B");
    }

    internal static string Decide(AiBattleScenario scenario, string actorId)
    {
        var instance = AiBattleScenarios.Create(scenario);
        var engine = new BattleEngine(random: new SeededRandomService(17));
        instance.State.GetUnit(actorId).ActionGauge = 100;
        engine.BeginAction(instance.State, actorId);
        var agent = new BasicEnemyBattleAgent(
            new BattleTurnCandidateGenerator(engine),
            new BattleAiPolicyResolver(_ => 10));
        return AiBattleScenarioInstance.Describe(agent.Decide(instance.State, actorId));
    }
}
