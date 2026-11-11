using System.Diagnostics;
using Game.AiBenchmarks;
using Game.Core.Battle;

if (args.Length > 1 && args[0] == "--content")
{
    // 冒烟: 用真实 MOD 数据 (天赋、特殊技能、Buff 效果) 跑 AI 决策并实际执行选出的计划
    return ContentSmoke.Run(args[1], args.Length > 2 ? int.Parse(args[2]) : 40);
}

// 战斗 AI 固定基准: dotnet run -c Release --project tests/Game.AiBenchmarks [-- 迭代次数]
// 真实数据冒烟:   dotnet run -c Release --project tests/Game.AiBenchmarks -- --content mods/jyxr-base [回合数]
// 输出每个战场的平均决策耗时、每次决策分配字节数、候选数量, 以及决策摘要 (用于和回归测试核对)。
var iterations = args.Length > 0 ? int.Parse(args[0]) : 20;
foreach (var scenario in AiBattleScenarios.All)
{
    var probe = AiBattleScenarios.Create(scenario);
    var actorIds = probe.Units.Select(static unit => unit.Id).ToArray();
    long allocated = 0;
    var elapsed = TimeSpan.Zero;
    var candidates = 0;
    var decisions = new List<string>();
    foreach (var actorId in actorIds)
    {
        for (var iteration = 0; iteration <= iterations; iteration++)
        {
            var instance = AiBattleScenarios.Create(scenario);
            var engine = new BattleEngine(random: new SeededRandomService(17));
            var actor = instance.State.GetUnit(actorId);
            actor.ActionGauge = 100;
            engine.BeginAction(instance.State, actorId);
            var generator = new BattleTurnCandidateGenerator(engine);
            var agent = new BasicEnemyBattleAgent(generator, new BattleAiPolicyResolver(_ => 10));
            if (iteration == 0)
            {
                // 预热 + 记录决策与候选数量
                candidates += generator.Generate(instance.State, actorId).Count;
                decisions.Add(AiBattleScenarioInstance.Describe(agent.Decide(instance.State, actorId)));
                continue;
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            agent.Decide(instance.State, actorId);
            watch.Stop();
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            elapsed += watch.Elapsed;
        }
    }

    var decideCount = actorIds.Length * iterations;
    Console.WriteLine(
        $"{scenario.Name,-8} units={actorIds.Length,2} candidates/decision={candidates / actorIds.Length,6} " +
        $"avg={elapsed.TotalMilliseconds / decideCount,8:F3} ms alloc={allocated / decideCount,10:N0} B");
    foreach (var decision in decisions)
    {
        Console.WriteLine("    " + decision);
    }
}

return 0;
