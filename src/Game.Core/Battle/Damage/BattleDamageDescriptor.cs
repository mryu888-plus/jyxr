using Game.Core.Affix;
using Game.Core.Model.Skills;

namespace Game.Core.Battle;

/// <summary>伤害如何处理命中。</summary>
public enum BattleDamageHitMode
{
    /// <summary>调用方已经完成命中判定（普通技能的命中步骤）。</summary>
    Resolved,
    /// <summary>结算前按技能语义判定命中：闪避、目标侧命中反制等照常生效。</summary>
    Judged,
    /// <summary>必定命中，不进行命中判定。</summary>
    Guaranteed,
}

/// <summary>伤害数值经过了哪种减伤；结算管线只读取它决定是否允许落血前改写。</summary>
public enum BattleDamageMitigation
{
    /// <summary>普通攻防公式。</summary>
    Normal,
    /// <summary>独立伤害公式，不参与普通攻防，但仍是一次可被反制的攻击。</summary>
    IgnoreDefence,
    /// <summary>真实伤害：不参与任何防御改写（封顶、取消、反弹、转移、分摊）。</summary>
    True,
}

/// <summary>伤害归因。</summary>
public enum BattleDamageOrigin
{
    /// <summary>普通技能命中。</summary>
    Skill,
    /// <summary>追加攻击（左右互搏、神速攻击等）。</summary>
    ExtraStrike,
    /// <summary>特殊技能等能力以独立公式造成的伤害。</summary>
    Ability,
    /// <summary>反弹给攻击者的伤害（斗转星移）。</summary>
    Reflection,
    /// <summary>落血前分摊给其他单位、作为独立伤害结算的部分（五行阵）。</summary>
    Share,
    /// <summary>周期伤害（中毒等）。</summary>
    Periodic,
    /// <summary>施法代价造成的自伤（同归剑法、天地同寿）。</summary>
    SelfCost,
}

/// <summary>伤害允许触发的结算阶段。</summary>
[Flags]
public enum BattleDamageReactions
{
    None = 0,
    /// <summary>落血前改写：`BeforeDamageApplied` 的封顶、取消、转移、反弹、分摊、吸收。</summary>
    PreApplication = 1,
    /// <summary>承伤者的 `OnDamageTaken`。</summary>
    TakenResponses = 2,
    /// <summary>承伤者的受击回怒。</summary>
    TakenRage = 4,
    /// <summary>来源的 `OnDamageDealt`（整次伤害只触发一次）。</summary>
    DealtResponses = 8,
    /// <summary>来源吸血（基于全部承伤分量的实际 HP 损失只结算一次）。</summary>
    Lifesteal = 16,
}

/// <summary>
/// 一次伤害的不可变描述。数值已由来源算好；公共结算管线（<see cref="BattleDamageResolver"/>）只按描述的
/// 强类型字段决定经过哪些阶段，不按技能类别分支。战败阶段不受开关影响：承伤者 HP 归零时总会经过
/// `BeforeDefeated` / `OnDefeated`。
/// </summary>
public sealed record BattleDamageDescriptor
{
    private BattleDamageDescriptor(
        BattleUnit source,
        BattleUnit target,
        int amount,
        BattleDamageOrigin origin,
        BattleDamageHitMode hitMode,
        BattleDamageMitigation mitigation,
        BattleDamageReactions reactions,
        SkillInstance? skill)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        if (hitMode == BattleDamageHitMode.Judged && skill is null)
        {
            throw new ArgumentException("Judged damage requires a skill to resolve the hit.", nameof(hitMode));
        }

        if (mitigation == BattleDamageMitigation.True && reactions.HasFlag(BattleDamageReactions.PreApplication))
        {
            throw new ArgumentException("True damage cannot be rewritten before application.", nameof(reactions));
        }

        Source = source;
        Target = target;
        Amount = amount;
        Origin = origin;
        HitMode = hitMode;
        Mitigation = mitigation;
        Reactions = reactions;
        Skill = skill;
    }

    public BattleUnit Source { get; }

    public BattleUnit Target { get; }

    /// <summary>落血前改写看到的伤害值。</summary>
    public int Amount { get; }

    public BattleDamageOrigin Origin { get; }

    public BattleDamageHitMode HitMode { get; }

    public BattleDamageMitigation Mitigation { get; }

    public BattleDamageReactions Reactions { get; }

    public SkillInstance? Skill { get; }

    public bool IsCritical { get; init; }

    /// <summary>
    /// 不受落血前封顶影响、但随伤害取消一起作废的追加伤害（攀云乘龙、无形剑气）。
    /// 在落血前改写之后加到主承伤者身上，不参与转移比例。
    /// </summary>
    public int UncappedBonusAmount
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }

    /// <summary>本次攻击额外的吸血倍率（伤害计算阶段累积）。</summary>
    public double LifestealRateBonus { get; init; }

    /// <summary>承伤后至少保留的 HP（周期伤害不致死）。</summary>
    public int MinimumRemainingHp
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }

    /// <summary>Damaged 事实的 detail；默认为来源单位 ID。</summary>
    public string? Detail { get; init; }

    public HookTiming? EventTiming { get; init; }

    public bool Allows(BattleDamageReactions reaction) => (Reactions & reaction) == reaction;

    private const BattleDamageReactions AttackReactions =
        BattleDamageReactions.PreApplication |
        BattleDamageReactions.TakenResponses |
        BattleDamageReactions.TakenRage |
        BattleDamageReactions.DealtResponses |
        BattleDamageReactions.Lifesteal;

    /// <summary>普通技能命中：命中已判定，全部阶段。</summary>
    public static BattleDamageDescriptor ForSkill(
        BattleUnit source,
        BattleUnit target,
        int amount,
        SkillInstance skill) =>
        new(source, target, amount, BattleDamageOrigin.Skill, BattleDamageHitMode.Resolved,
            BattleDamageMitigation.Normal, AttackReactions, skill ?? throw new ArgumentNullException(nameof(skill)));

    /// <summary>追加攻击：沿用触发它的那次命中，全部阶段。</summary>
    public static BattleDamageDescriptor ForExtraStrike(
        BattleUnit source,
        BattleUnit target,
        int amount,
        SkillInstance? skill) =>
        new(source, target, amount, BattleDamageOrigin.ExtraStrike, BattleDamageHitMode.Resolved,
            BattleDamageMitigation.Normal, AttackReactions, skill);

    /// <summary>
    /// 能力以独立公式造成的伤害。默认按技能判定命中、无视护甲，允许目标侧落血前反制和承伤后响应；
    /// 不吸血（legacy 特殊技能不吸血）。<paramref name="mitigation"/> 为 True 时关闭落血前改写。
    /// </summary>
    public static BattleDamageDescriptor ForAbility(
        BattleUnit source,
        BattleUnit target,
        int amount,
        SkillInstance skill,
        BattleDamageHitMode hitMode = BattleDamageHitMode.Judged,
        BattleDamageMitigation mitigation = BattleDamageMitigation.IgnoreDefence)
    {
        if (hitMode == BattleDamageHitMode.Resolved)
        {
            throw new ArgumentException("Ability damage must either judge or guarantee its hit.", nameof(hitMode));
        }

        if (mitigation == BattleDamageMitigation.Normal)
        {
            throw new ArgumentException("Ability damage uses its own formula and cannot claim normal mitigation.", nameof(mitigation));
        }

        var reactions = BattleDamageReactions.TakenResponses |
            BattleDamageReactions.TakenRage |
            BattleDamageReactions.DealtResponses;
        if (mitigation != BattleDamageMitigation.True)
        {
            reactions |= BattleDamageReactions.PreApplication;
        }

        return new(source, target, amount, BattleDamageOrigin.Ability, hitMode, mitigation, reactions, skill);
    }

    /// <summary>
    /// 反弹：来源是反弹者，不能再被改写（防止反弹链），承伤者有承伤后响应，但不受击回怒；
    /// 反弹者不触发 `OnDamageDealt`、不吸血。
    /// </summary>
    public static BattleDamageDescriptor ForReflection(BattleUnit reflector, BattleUnit target, int amount) =>
        new(reflector, target, amount, BattleDamageOrigin.Reflection, BattleDamageHitMode.Guaranteed,
            BattleDamageMitigation.True, BattleDamageReactions.TakenResponses, skill: null);

    /// <summary>
    /// 分摊给其他单位的独立伤害：归因于原攻击者但不携带技能，不能再被改写；
    /// 承伤者有承伤后响应和受击回怒，攻击者触发 `OnDamageDealt` 但不吸血。
    /// </summary>
    public static BattleDamageDescriptor ForShare(BattleUnit attacker, BattleUnit target, int amount) =>
        new(attacker, target, amount, BattleDamageOrigin.Share, BattleDamageHitMode.Guaranteed,
            BattleDamageMitigation.True,
            BattleDamageReactions.TakenResponses | BattleDamageReactions.TakenRage | BattleDamageReactions.DealtResponses,
            skill: null);

    /// <summary>周期伤害：真实伤害，关闭全部可选阶段，至少保留 1 HP。</summary>
    public static BattleDamageDescriptor ForPeriodic(BattleUnit source, BattleUnit target, int amount) =>
        new(source, target, amount, BattleDamageOrigin.Periodic, BattleDamageHitMode.Guaranteed,
            BattleDamageMitigation.True, BattleDamageReactions.None, skill: null)
        {
            MinimumRemainingHp = 1,
        };

    /// <summary>
    /// 施法代价：不可防御（不能被封顶、取消、转移、反弹），仍是真实的 HP 损失，触发施法者自己的承伤后响应
    /// 和战败阶段；不受击回怒、不触发 OnDamageDealt、不吸血。
    /// </summary>
    public static BattleDamageDescriptor ForSelfCost(BattleUnit caster, int amount, SkillInstance? skill) =>
        new(caster, caster, amount, BattleDamageOrigin.SelfCost, BattleDamageHitMode.Guaranteed,
            BattleDamageMitigation.True, BattleDamageReactions.TakenResponses, skill);
}

/// <summary>一次伤害结算的结果。</summary>
public sealed record BattleDamageResolution(
    BattleUnit Target,
    int ActualAmount,
    int TotalActualAmount,
    bool IsHitCancelled,
    bool IsDamageCancelled,
    bool SuppressHitEffects);
