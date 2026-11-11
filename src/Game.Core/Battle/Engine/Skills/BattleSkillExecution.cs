using Game.Core.Model.Skills;

namespace Game.Core.Battle;

/// <summary>
/// 一次主动技能施放（攻击级结算上下文）。
/// 记录本次施放实际作用到的单位（技能伤害、技能附带状态、技能定义效果解析出的目标），
/// 主动攻击回怒据此判断是否作用于敌方；受击回怒在同一次施放内对每个目标最多判定一次（legacy AttackLogic 语义）。
/// </summary>
internal sealed class BattleSkillExecution(BattleUnit source, SkillInstance skill)
{
    private readonly List<BattleUnit> _affectedUnits = [];
    private readonly HashSet<string> _affectedUnitIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _damagedRageRolledUnitIds = new(StringComparer.Ordinal);

    public BattleUnit Source { get; } = source ?? throw new ArgumentNullException(nameof(source));

    public SkillInstance Skill { get; } = skill ?? throw new ArgumentNullException(nameof(skill));

    public IReadOnlyList<BattleUnit> AffectedUnits => _affectedUnits;

    public void RecordAffected(BattleUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (_affectedUnitIds.Add(unit.Id))
        {
            _affectedUnits.Add(unit);
        }
    }

    public void RecordAffected(IEnumerable<BattleUnit> units)
    {
        ArgumentNullException.ThrowIfNull(units);
        foreach (var unit in units)
        {
            RecordAffected(unit);
        }
    }

    public bool AffectedEnemy(BattleState state) =>
        _affectedUnits.Any(unit => state.AreEnemies(Source, unit));

    /// <summary>同一次施放内，每个受击单位只领取一次受击回怒判定。</summary>
    public bool TryClaimDamagedRageRoll(BattleUnit target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return _damagedRageRolledUnitIds.Add(target.Id);
    }
}
