using Game.Core.Abstractions;
using Game.Core.Affix;
using Game.Core.Model;
using Game.Core.Model.Skills;

namespace Game.Core.Battle;

public interface IBattleEffectContext
{
    BattleState State { get; }
    HookTiming Timing { get; }
    BattleUnit Unit { get; }
    BattleUnit? Source { get; }
    BattleUnit? Provider { get; }
    BattleUnit? Target { get; }
    BattleBuffInstance? Buff { get; }
    SkillInstance? Skill { get; }
    IRandomService Random { get; }
    bool IsPreview { get; }
    BattleExecutionScope? ExecutionScope { get; }
    void RequestSpeech(BattleUnit speaker, string text);
    void RequestFloatText(BattleUnit target, string text, BattleFloatTextStyle style = BattleFloatTextStyle.Normal);
}

public interface IBattleAbilityEffectContext
{
    BattleUnit Source { get; }
    SkillInstance Skill { get; }
    IReadOnlyList<BattleUnit> Targets { get; }
    IRandomService Random { get; }
    BattleExecutionScope? ExecutionScope { get; }

    bool IsCellAvailable(GridPosition position, BattleUnit movingUnit);
    bool TryRelocate(BattleUnit target, GridPosition destination);
    BattleRelocationResult Push(BattleUnit target, int distance);
    BattleRelocationResult Pull(BattleUnit target, int distance);
    BattleRelocationResult SwapWithSource(BattleUnit target);
    bool ApplyBuff(BattleUnit target, string buffId, int level, int duration);
    int ApplyHpRecovery(BattleUnit target, int amount);

    /// <summary>
    /// 以能力自身公式算好的数值对目标造成伤害，经公共伤害管线结算。默认按技能判定命中（legacy 特殊技能同样会被闪避）、无视护甲、可被目标侧落血前反制；
    /// <paramref name="mitigation"/> 为 True 时是真实伤害，<paramref name="hitMode"/> 为 Guaranteed 时必中。
    /// </summary>
    int DealDamage(
        BattleUnit target,
        int amount,
        BattleDamageMitigation mitigation = BattleDamageMitigation.IgnoreDefence,
        BattleDamageHitMode hitMode = BattleDamageHitMode.Judged);

    /// <summary>施法者支付 HP 代价：不可防御，仍触发施法者的承伤后响应与战败阶段，不回怒、不吸血。</summary>
    int PayHpCost(int amount);
}

public interface IDamageCalculationEffectContext : IBattleEffectContext
{
    BattleDamageCalculationContext DamageCalculation { get; }
}

public interface IHitResultEffectContext : IBattleEffectContext
{
    BattleHitState HitState { get; set; }
    int DamageAmount { get; set; }
    bool SuppressHitEffects { get; set; }
    void ResetUnitSkillCooldowns(string? detail = null);
}

public interface IHitConfirmedEffectContext : IBattleEffectContext
{
    bool IsCellAvailable(GridPosition position, BattleUnit movingUnit);
    bool TryRelocate(BattleUnit target, GridPosition destination);
    bool ApplyBuff(
        BattleUnit target,
        string buffId,
        int level,
        int duration);

    /// <summary>扣减目标内力，产出 MpDamaged 事实，返回实际扣减量。</summary>
    int DamageMp(BattleUnit target, int amount, string? detail = null);

    /// <summary>经恢复 resolver（含 BeforeRecoveryResolved）恢复目标内力，产出 MpRecovered 事实，返回实际恢复量。</summary>
    int RecoverMp(BattleUnit target, int amount, string? detail = null);
}

public interface IDamageApplicationEffectContext : IBattleEffectContext
{
    int DamageAmount { get; }
    BattleDamageOrigin DamageOrigin { get; }
    BattleDamageMitigation DamageMitigation { get; }
    void CapDamage(int maximum);
    void CancelDamage(bool suppressHitEffects);
    void RedirectDamage(BattleUnit target, double damageFactor);

    /// <summary>
    /// 把本次伤害中的 <paramref name="amount"/> 转移给 <paramref name="target"/>，作为同一次攻击的另一个承伤分量：
    /// 保留原攻击者、技能与归因；承伤者各自触发承伤后响应和战败阶段，攻击者侧 OnDamageDealt 与吸血按全部分量汇总只执行一次。
    /// </summary>
    void TransferDamage(BattleUnit target, int amount, string? detail = null);

    /// <summary>把伤害反弹给 <paramref name="target"/>：反弹者为来源的独立真实伤害，不能再被改写。</summary>
    int ReflectDamage(BattleUnit target, int amount, string? detail = null);

    /// <summary>分摊给其他单位：归因于原攻击者的独立伤害，不能再被改写，不吸血。</summary>
    int ShareDamage(BattleUnit target, int amount, string? detail = null);

    int ApplyHpRecovery(BattleUnit target, int amount, string? detail = null);
    int DamageMp(BattleUnit target, int amount, string? detail = null);
}

public interface IDamageTakenEffectContext : IBattleEffectContext
{
    int ActualDamageAmount { get; }
    bool IsCritical { get; }
}

public interface IDamageDealtEffectContext : IBattleEffectContext
{
    int ActualDamageAmount { get; }
    bool IsCritical { get; }
    double LifestealRate { get; set; }
}

public interface IDefeatPreventionEffectContext : IBattleEffectContext
{
    int IncomingDamageAmount { get; }
    int ActualDamageAmount { get; }
    bool IsCritical { get; }
    bool IsDefeatPrevented { get; }

    /// <summary>本场战斗中该能力已成功生效的次数。</summary>
    int GetAbilityUsageCount(string abilityId);

    /// <summary>
    /// 原子地阻止败北：已被其他效果阻止、或内力不足以支付代价时返回 false 且不改变任何状态；
    /// 否则以 1 HP 存活，经 resolver 依次提交内力代价、气血/内力恢复和怒气设置并产出事实，最后登记能力次数。
    /// </summary>
    bool TryPreventDefeat(BattleDefeatPrevention prevention);
}

/// <summary>一次败北阻止要提交的全部资源变化。</summary>
public sealed record BattleDefeatPrevention
{
    public BattleDefeatPrevention(string abilityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(abilityId);
        AbilityId = abilityId;
    }

    public string AbilityId { get; }

    /// <summary>以 1 HP 存活后额外恢复的气血（经恢复 resolver）。</summary>
    public int HpRecovery { get; init => field = NonNegative(value); }

    public int MpRecovery { get; init => field = NonNegative(value); }

    /// <summary>阻止败北需要支付的内力；不足时阻止失败。</summary>
    public int MpCost { get; init => field = NonNegative(value); }

    /// <summary>设置后的怒气；为 null 时不改变。</summary>
    public int? Rage { get; init; }

    public string? FloatText { get; init; }

    public string? Speech { get; init; }

    private static int NonNegative(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        return value;
    }
}

public interface IDefeatedEffectContext : IBattleEffectContext;

public interface IRecoveryEffectContext : IBattleEffectContext
{
    BattleRecoveryKind RecoveryKind { get; }
    int RecoveryAmount { get; set; }
}

public interface ISkillCostEffectContext : IBattleEffectContext
{
    int MpCost { get; set; }
}

public interface IBuffApplicationEffectContext : IBattleEffectContext
{
    BattleBuffInstance AppliedBuff { get; }
    bool Cancel { get; set; }
}

public interface IActionStartEffectContext : IBattleEffectContext
{
    void SkipCurrentAction(string? reason = null);
    int SetRage(int value, string? detail = null);
    int ApplyHpRecovery(BattleUnit target, int amount, string? detail = null);

    /// <summary>扣减目标内力，产出 MpDamaged 事实，返回实际扣减量。</summary>
    int DamageMp(BattleUnit target, int amount, string? detail = null);
}

public interface IActionReadinessEffectContext : IBattleEffectContext
{
    int RemoveNegativeBuffs();
}
