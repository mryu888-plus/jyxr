using Game.Content.Loading;
using Game.Core.Abstractions;
using Game.Core.Battle;
using Game.Core.Definitions;
using Game.Core.Definitions.Skills;
using Game.Core.Model;
using Game.Core.Model.Character;

namespace Game.AiBenchmarks;

/// <summary>
/// 战斗 AI 的固定基准战场。只用纯伤害外功（无 Buff、无特殊技能），
/// 既给 benchmark 测耗时与分配量，也给决策回归测试锁定候选排序语义。
/// 该文件同时以链接方式编译进 Game.Tests。
/// </summary>
public static class AiBattleScenarios
{
    public static IReadOnlyList<AiBattleScenario> All { get; } =
    [
        new("small", 6, 6, 1, 2, 2, 3),
        new("regular", 11, 8, 4, 4, 4, 4),
        new("dense", 16, 12, 8, 8, 8, 7),
    ];

    public static AiBattleScenarioInstance Create(AiBattleScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        var skills = CreateSkills();
        var characters = new List<CharacterDefinition>();
        var placements = new List<(string Id, int Team, GridPosition Position)>();
        var random = new Random(scenario.Name.Length * 7919 + scenario.Width * 31 + scenario.Height);
        var occupied = new HashSet<GridPosition>();
        for (var team = 1; team <= 2; team++)
        {
            var count = team == 1 ? scenario.TeamOneCount : scenario.TeamTwoCount;
            for (var index = 0; index < count; index++)
            {
                var id = $"{scenario.Name}-t{team}-{index}";
                var skillEntries = Enumerable.Range(0, scenario.SkillsPerUnit)
                    .Select(offset => new InitialExternalSkillEntryDefinition(
                        skills[(index * 3 + offset + team) % skills.Count].Id,
                        Level: 1 + (index + offset) % 10))
                    .ToList();
                var stats = new Dictionary<StatType, int>
                {
                    [StatType.MaxHp] = 400 + random.Next(0, 800),
                    [StatType.MaxMp] = 2000,
                    [StatType.Movement] = scenario.Movement,
                    [StatType.Speed] = 10,
                    [StatType.Quanzhang] = 40 + random.Next(0, 80),
                    [StatType.Jianfa] = 40 + random.Next(0, 80),
                    [StatType.Daofa] = 40 + random.Next(0, 80),
                    [StatType.Qimen] = 40 + random.Next(0, 80),
                    [StatType.Bili] = 30 + random.Next(0, 60),
                    [StatType.Gengu] = 30 + random.Next(0, 60),
                };
                characters.Add(new CharacterDefinition(
                    id,
                    id,
                    stats,
                    skillEntries,
                    [],
                    [],
                    [],
                    Level: 1 + random.Next(0, 20)));

                GridPosition position;
                do
                {
                    var xMin = team == 1 ? 0 : scenario.Width / 2;
                    position = new GridPosition(
                        random.Next(xMin, xMin + scenario.Width / 2),
                        random.Next(0, scenario.Height));
                }
                while (!occupied.Add(position));
                placements.Add((id, team, position));
            }
        }

        var repository = new JsonContentLoader().LoadFromPackage(new ContentPackage
        {
            ExternalSkills = skills.ToList(),
            Characters = characters,
        });
        var equipmentFactory = new EquipmentInstanceFactory();
        var units = placements
            .Select(placement =>
            {
                var definition = repository.GetCharacter(placement.Id);
                var character = CharacterMapper.CreateInitial(placement.Id, definition, equipmentFactory);
                var unit = new BattleUnit(placement.Id, character, placement.Team, placement.Position);
                // 半数单位带伤, 让击杀与低血量分支都会出现
                return random.Next(0, 2) == 0
                    ? new BattleUnit(
                        placement.Id,
                        character,
                        placement.Team,
                        placement.Position,
                        hp: Math.Max(1, unit.MaxHp / (2 + random.Next(0, 4))))
                    : unit;
            })
            .ToList();
        return new AiBattleScenarioInstance(
            scenario,
            new BattleState(new BattleGrid(scenario.Width, scenario.Height), units),
            units);
    }

    private static IReadOnlyList<ExternalSkillDefinition> CreateSkills() =>
    [
        Skill("single_punch", WeaponType.Quanzhang, SkillImpactType.Single, castSize: 1, impactSize: 0, powerBase: 6),
        Skill("long_dart", WeaponType.Qimen, SkillImpactType.Single, castSize: 5, impactSize: 0, powerBase: 3),
        Skill("cross_palm", WeaponType.Quanzhang, SkillImpactType.Plus, castSize: 3, impactSize: 1, powerBase: 4),
        Skill("square_blade", WeaponType.Daofa, SkillImpactType.Square, castSize: 2, impactSize: 1, powerBase: 4),
        Skill("line_sword", WeaponType.Jianfa, SkillImpactType.Line, castSize: 1, impactSize: 4, powerBase: 5),
        Skill("fan_sword", WeaponType.Jianfa, SkillImpactType.Fan, castSize: 1, impactSize: 3, powerBase: 4),
        Skill("star_staff", WeaponType.Qimen, SkillImpactType.Star, castSize: 4, impactSize: 2, powerBase: 3),
        Skill("cleave_blade", WeaponType.Daofa, SkillImpactType.Cleave, castSize: 1, impactSize: 1, powerBase: 5),
        Skill("ring_palm", WeaponType.Quanzhang, SkillImpactType.Ring, castSize: 3, impactSize: 2, powerBase: 3),
        Skill("x_sword", WeaponType.Jianfa, SkillImpactType.X, castSize: 4, impactSize: 2, powerBase: 3),
    ];

    private static ExternalSkillDefinition Skill(
        string id,
        WeaponType type,
        SkillImpactType impactType,
        int castSize,
        int impactSize,
        double powerBase) =>
        new()
        {
            Id = id,
            Name = id,
            Type = type,
            Hard = 1d,
            PowerBase = powerBase,
            PowerStep = 0.5d,
            Cost = new SkillCostDefinition(Mp: 10),
            Targeting = new SkillTargetingDefinition(
                CastSize: castSize,
                ImpactType: impactType,
                ImpactSize: impactSize),
        };
}

public sealed record AiBattleScenario(
    string Name,
    int Width,
    int Height,
    int TeamOneCount,
    int TeamTwoCount,
    int SkillsPerUnit,
    int Movement);

public sealed record AiBattleScenarioInstance(
    AiBattleScenario Scenario,
    BattleState State,
    IReadOnlyList<BattleUnit> Units)
{
    /// <summary>让指定单位成为当前行动者并返回其决策描述, 用于回归比对。</summary>
    public static string Describe(BattleTurnPlan plan) =>
        plan.MainAction.Kind == BattleMainActionKind.CastSkill
            ? $"{plan.UnitId}>({plan.MoveDestination.X},{plan.MoveDestination.Y}) cast {plan.MainAction.SkillId}@({plan.MainAction.TargetPosition!.Value.X},{plan.MainAction.TargetPosition.Value.Y})"
            : $"{plan.UnitId}>({plan.MoveDestination.X},{plan.MoveDestination.Y}) {plan.MainAction.Kind}";
}

/// <summary>固定种子的随机源, 保证基准与回归可复现。</summary>
public sealed class SeededRandomService(int seed) : IRandomService
{
    private readonly Random _random = new(seed);

    public double NextDouble() => _random.NextDouble();

    public int Next(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);
}
