using Game.Core.Model;
using Game.Core.Model.Skills;

namespace Game.Core.Battle;

/// <summary>
/// 单次 AI 决策内共享的只读快照与缓存：存活单位的位置索引、敌方位置、可达格、
/// 技能对目标的伤害估算缓存，以及候选评估复用的缓冲区。
/// 生命周期只覆盖一次决策（一个单位的一次行动），不跨决策或跨战斗保存可变状态。
/// 行动单位视为“已移动到候选落点”：它不占据原位置，位置查询由落点参数决定。
/// </summary>
public sealed class BattleAiEvaluationContext
{
    private readonly Dictionary<GridPosition, BattleUnit> _unitsByPosition = new();
    private readonly Dictionary<(SkillInstance Skill, BattleUnit Target), int> _damageEstimates =
        new(SkillTargetComparer.Instance);
    private readonly BattleDamageEstimator _damageEstimator;
    private readonly GridPosition[] _enemyPositions;
    private IReadOnlyDictionary<GridPosition, int>? _reachablePositions;

    public BattleAiEvaluationContext(
        BattleEngine engine,
        BattleState state,
        BattleUnit unit,
        BattleDamageEstimator? damageEstimator = null)
    {
        Engine = engine ?? throw new ArgumentNullException(nameof(engine));
        State = state ?? throw new ArgumentNullException(nameof(state));
        Unit = unit ?? throw new ArgumentNullException(nameof(unit));
        _damageEstimator = damageEstimator ?? new BattleDamageEstimator();

        var enemyPositions = new List<GridPosition>();
        foreach (var other in state.Units)
        {
            if (!other.IsAlive)
            {
                continue;
            }

            if (!ReferenceEquals(other, unit))
            {
                _unitsByPosition[other.Position] = other;
            }

            if (state.AreEnemies(unit, other))
            {
                enemyPositions.Add(other.Position);
            }
        }

        _enemyPositions = enemyPositions.ToArray();
        SkillDamageEstimator = EstimateSkillDamage;
    }

    /// <summary>缓存的伤害估算委托，避免每个候选重新分配委托。</summary>
    internal Func<SkillInstance, BattleUnit, int> SkillDamageEstimator { get; }

    public BattleEngine Engine { get; }

    public BattleState State { get; }

    public BattleUnit Unit { get; }

    /// <summary>候选评估复用的主要目标缓冲区；评分器不得在调用结束后持有。</summary>
    internal List<BattleUnit> PrimaryTargetBuffer { get; } = [];

    /// <summary>技能执行预览复用的结果缓冲区；评分器不得在调用结束后持有。</summary>
    internal List<BattleSkillPreviewEffect> PreviewBuffer { get; } = [];

    /// <summary>行动单位本回合可到达的落点及移动消耗（同一决策内只计算一次）。</summary>
    public IReadOnlyDictionary<GridPosition, int> ReachablePositions =>
        _reachablePositions ??= Engine.GetReachablePositions(State, Unit.Id);

    /// <summary>行动单位站在 <paramref name="unitPosition"/> 时，<paramref name="position"/> 上的存活单位。</summary>
    public BattleUnit? GetUnitAt(GridPosition position, GridPosition unitPosition)
    {
        if (position == unitPosition)
        {
            return Unit;
        }

        return _unitsByPosition.GetValueOrDefault(position);
    }

    public int GetDistanceToNearestEnemy(GridPosition position)
    {
        if (_enemyPositions.Length == 0)
        {
            return 0;
        }

        var nearest = int.MaxValue;
        foreach (var enemyPosition in _enemyPositions)
        {
            nearest = Math.Min(nearest, position.ManhattanDistanceTo(enemyPosition));
        }

        return nearest;
    }

    /// <summary>
    /// 普通技能伤害估算只取决于双方属性与技能，与站位无关，因此同一决策内按（技能，目标）缓存。
    /// </summary>
    public int EstimateSkillDamage(SkillInstance skill, BattleUnit target)
    {
        ArgumentNullException.ThrowIfNull(skill);
        ArgumentNullException.ThrowIfNull(target);

        if (!_damageEstimates.TryGetValue((skill, target), out var damage))
        {
            damage = _damageEstimator.Estimate(Unit, target, skill).Damage;
            _damageEstimates[(skill, target)] = damage;
        }

        return damage;
    }

    private sealed class SkillTargetComparer : IEqualityComparer<(SkillInstance Skill, BattleUnit Target)>
    {
        public static SkillTargetComparer Instance { get; } = new();

        public bool Equals((SkillInstance Skill, BattleUnit Target) x, (SkillInstance Skill, BattleUnit Target) y) =>
            ReferenceEquals(x.Skill, y.Skill) && ReferenceEquals(x.Target, y.Target);

        public int GetHashCode((SkillInstance Skill, BattleUnit Target) obj) =>
            HashCode.Combine(
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Skill),
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Target));
    }
}
