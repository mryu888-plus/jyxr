using Game.Content.Loading;
using Game.Core.Battle;
using Game.Core.Definitions;
using Game.Core.Definitions.Skills;
using Game.Core.Model;
using Game.Core.Model.Character;

namespace Game.AiBenchmarks;

/// <summary>
/// 真实内容冒烟: 4 对 4, 每个单位带全部特殊技能与若干真实外功、真实天赋,
/// AI 决策后通过引擎实际执行 (移动 + 施放/休息), 统计选中的技能并报告执行失败或异常。
/// </summary>
internal static class ContentSmoke
{
    public static int Run(string modDirectory, int turns)
    {
        var repository = new JsonContentLoader().LoadFromMods([new ModContentInput("jyxr-base", modDirectory, Required: true)]);
        var specialSkillIds = repository.SpecialSkills.Keys.OrderBy(static id => id, StringComparer.Ordinal).ToList();
        var externalSkillIds = repository.ExternalSkills.Keys.OrderBy(static id => id, StringComparer.Ordinal).ToList();
        var talentIds = repository.Talents.Keys.OrderBy(static id => id, StringComparer.Ordinal).ToList();
        var random = new Random(20261112);
        var equipment = new EquipmentInstanceFactory();
        var units = new List<BattleUnit>();
        var occupied = new HashSet<GridPosition>();
        for (var index = 0; index < 8; index++)
        {
            var team = index < 4 ? 1 : 2;
            var definition = new CharacterDefinition(
                $"smoke{index}",
                $"smoke{index}",
                new Dictionary<StatType, int>
                {
                    [StatType.MaxHp] = 30000,
                    [StatType.MaxMp] = 5000,
                    [StatType.Movement] = 3,
                    [StatType.Quanzhang] = 100,
                    [StatType.Jianfa] = 100,
                    [StatType.Daofa] = 100,
                    [StatType.Qimen] = 100,
                    [StatType.Bili] = 80,
                    [StatType.Fuyuan] = 80,
                },
                Enumerable.Range(0, 4)
                    .Select(_ => new InitialExternalSkillEntryDefinition(externalSkillIds[random.Next(externalSkillIds.Count)], 10))
                    .DistinctBy(static entry => entry.Id)
                    .ToList(),
                [],
                Enumerable.Range(0, 3).Select(_ => talentIds[random.Next(talentIds.Count)]).Distinct()
                    .Select(static id => new ContentReference<TalentDefinition>(id)).ToList(),
                [],
                Level: 20,
                SpecialSkillReferences: specialSkillIds.Select(static id => new ContentReference<SpecialSkillDefinition>(id)).ToList());
            ContentReferenceGraph.Resolve(definition, repository);
            var character = CharacterMapper.CreateInitial(definition.Id, definition, equipment);
            GridPosition position;
            do
            {
                position = new GridPosition(random.Next(team == 1 ? 0 : 6, team == 1 ? 5 : 11), random.Next(0, 8));
            }
            while (!occupied.Add(position));
            units.Add(new BattleUnit(definition.Id, character, team, position, rage: random.Next(0, 6)));
        }

        var state = new BattleState(new BattleGrid(11, 8), units);
        var engine = new BattleEngine(
            random: new SeededRandomService(7),
            buffResolver: repository.GetBuff,
            legendSkillsProvider: repository.GetLegendSkillsFor,
            skillMaxLevelResolver: _ => 20);
        var agent = new BasicEnemyBattleAgent(new BattleTurnCandidateGenerator(engine), new BattleAiPolicyResolver(_ => 20));
        engine.StartBattle(state);
        var chosen = new Dictionary<string, int>(StringComparer.Ordinal);
        var failures = 0;
        for (var turn = 0; turn < turns; turn++)
        {
            var advance = engine.AdvanceUntilNextAction(state);
            if (!advance.Success)
            {
                Console.WriteLine($"timeline stopped at turn {turn}: {advance.Failure}");
                break;
            }

            var actor = advance.Value!;
            engine.BeginAction(state, actor.Id);
            var plan = agent.Decide(state, actor.Id);
            if (plan.MoveDestination != actor.Position && !engine.MoveTo(state, actor.Id, plan.MoveDestination).Success)
            {
                failures++;
                Console.WriteLine($"move failed: {plan}");
            }

            bool success;
            if (plan.MainAction.Kind == BattleMainActionKind.CastSkill)
            {
                var skill = BattleSkillCatalog.CollectSelectableSkills(actor).First(skill => skill.Id == plan.MainAction.SkillId);
                success = engine.CastSkill(state, actor.Id, skill, plan.MainAction.TargetPosition!.Value).Success;
                var key = skill is Game.Core.Model.Skills.SpecialSkillInstance ? "特技:" + skill.Id : "外功";
                chosen[key] = chosen.GetValueOrDefault(key) + 1;
            }
            else
            {
                success = engine.Rest(state, actor.Id).Success;
                chosen["休息"] = chosen.GetValueOrDefault("休息") + 1;
            }

            if (!success)
            {
                failures++;
                Console.WriteLine($"action failed: {plan}");
            }

            if (state.Units.Where(static unit => unit.IsAlive).Select(static unit => unit.Team).Distinct().Count() < 2)
            {
                break;
            }
        }

        foreach (var (key, count) in chosen.OrderByDescending(static pair => pair.Value))
        {
            Console.WriteLine($"{count,4} {key}");
        }

        Console.WriteLine(
            "alive: " + string.Join(", ", state.Units.GroupBy(static unit => unit.Team)
                .Select(static group => $"team{group.Key}={group.Count(static unit => unit.IsAlive)}")));
        Console.WriteLine($"failures={failures}");
        return failures == 0 ? 0 : 1;
    }
}
