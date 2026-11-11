using Game.Core.Abstractions;
using Game.Core.Affix;
using Game.Core.Model;
using Game.Core.Model.Skills;

namespace Game.Core.Battle;

public enum BattleHookExecutionMode
{
    Execute,
    Preview,
}

public sealed class BattleHookContext :
    IBattleEffectContext,
    IDamageCalculationEffectContext,
    IHitResultEffectContext,
    IHitConfirmedEffectContext,
    IDamageApplicationEffectContext,
    IDamageTakenEffectContext,
    IDamageDealtEffectContext,
    IDefeatPreventionEffectContext,
    IDefeatedEffectContext,
    IRecoveryEffectContext,
    ISkillCostEffectContext,
    IBuffApplicationEffectContext,
    IActionReadinessEffectContext,
    IActionStartEffectContext
{
    internal BattleHookContext(
        BattleEngine engine,
        BattleState state,
        HookTiming timing,
        BattleUnit unit,
        IRandomService random,
        BattleHookExecutionMode executionMode)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(random);

        Engine = engine;
        State = state;
        Timing = timing;
        Unit = unit;
        Random = random;
        ExecutionMode = executionMode;
    }

    internal BattleEngine Engine { get; }

    public BattleState State { get; }

    public HookTiming Timing { get; }

    public BattleUnit Unit { get; }

    public IRandomService Random { get; }

    public BattleHookExecutionMode ExecutionMode { get; }

    public bool IsPreview => ExecutionMode == BattleHookExecutionMode.Preview;

    public BattleExecutionScope? ExecutionScope => State.CurrentExecutionScope;

    public BattleUnit? Source { get; internal set; }

    public BattleUnit? Provider { get; internal set; }

    public BattleUnit? Target { get; internal set; }

    public BattleBuffInstance? Buff { get; internal set; }

    public SkillInstance? Skill { get; internal set; }

    public BattleDamageCalculationContext? DamageCalculation { get; internal set; }

    public int? MpCost { get; internal set; }

    public int? DamageAmount { get; internal set; }

    public int? IncomingDamageAmount { get; internal set; }

    public double? LifestealRate { get; internal set; }

    /// <summary>BeforeDamageApplied 期间正在结算的伤害描述。</summary>
    internal BattleDamageDescriptor? DamageBeingApplied { get; set; }

    /// <summary>BeforeDamageApplied 中是否有效果取消了本次伤害（取消的伤害不再追加不受封顶影响的额外伤害）。</summary>
    internal bool IsDamageCancelled { get; private set; }

    private List<BattleDamageComponent>? _pendingDamageTransfers;

    /// <summary>BeforeDamageApplied 中经 TransferDamage 产生、尚未结算的承伤分量。</summary>
    internal IReadOnlyList<BattleDamageComponent> PendingDamageTransfers =>
        _pendingDamageTransfers ?? (IReadOnlyList<BattleDamageComponent>)[];

    BattleDamageCalculationContext IDamageCalculationEffectContext.DamageCalculation =>
        DamageCalculation ?? throw MissingCapability(nameof(IDamageCalculationEffectContext));

    int IDamageApplicationEffectContext.DamageAmount =>
        DamageAmount ?? throw MissingCapability(nameof(IDamageApplicationEffectContext));
    BattleDamageOrigin IDamageApplicationEffectContext.DamageOrigin =>
        DamageBeingApplied?.Origin ?? throw MissingCapability(nameof(IDamageApplicationEffectContext));
    BattleDamageMitigation IDamageApplicationEffectContext.DamageMitigation =>
        DamageBeingApplied?.Mitigation ?? throw MissingCapability(nameof(IDamageApplicationEffectContext));

    BattleHitState IHitResultEffectContext.HitState { get => HitState; set => HitState = value; }
    int IHitResultEffectContext.DamageAmount
    {
        get => DamageAmount ?? throw MissingCapability(nameof(IHitResultEffectContext));
        set => DamageAmount = value;
    }
    bool IHitResultEffectContext.SuppressHitEffects
    {
        get => SuppressHitEffects;
        set => SuppressHitEffects = value;
    }
    void IHitResultEffectContext.ResetUnitSkillCooldowns(string? detail) =>
        ResetUnitSkillCooldowns(detail);
    int IDamageTakenEffectContext.ActualDamageAmount =>
        DamageAmount ?? throw MissingCapability(nameof(IDamageTakenEffectContext));
    bool IDamageTakenEffectContext.IsCritical => IsCritical;
    int IDamageDealtEffectContext.ActualDamageAmount =>
        DamageAmount ?? throw MissingCapability(nameof(IDamageDealtEffectContext));
    bool IDamageDealtEffectContext.IsCritical => IsCritical;
    double IDamageDealtEffectContext.LifestealRate
    {
        get => LifestealRate ?? throw MissingCapability(nameof(IDamageDealtEffectContext));
        set => LifestealRate = value;
    }
    int IDefeatPreventionEffectContext.IncomingDamageAmount =>
        IncomingDamageAmount ?? throw MissingCapability(nameof(IDefeatPreventionEffectContext));
    int IDefeatPreventionEffectContext.ActualDamageAmount =>
        DamageAmount ?? throw MissingCapability(nameof(IDefeatPreventionEffectContext));
    bool IDefeatPreventionEffectContext.IsCritical => IsCritical;
    bool IDefeatPreventionEffectContext.IsDefeatPrevented => IsDefeatPrevented;
    BattleRecoveryKind IRecoveryEffectContext.RecoveryKind =>
        RecoveryKind ?? throw MissingCapability(nameof(IRecoveryEffectContext));
    int IRecoveryEffectContext.RecoveryAmount
    {
        get => RecoveryAmount ?? throw MissingCapability(nameof(IRecoveryEffectContext));
        set => RecoveryAmount = value;
    }
    int ISkillCostEffectContext.MpCost
    {
        get => MpCost ?? throw MissingCapability(nameof(ISkillCostEffectContext));
        set => MpCost = value;
    }
    BattleBuffInstance IBuffApplicationEffectContext.AppliedBuff =>
        Buff ?? throw MissingCapability(nameof(IBuffApplicationEffectContext));
    bool IBuffApplicationEffectContext.Cancel { get => Cancel; set => Cancel = value; }

    public BattleRecoveryKind? RecoveryKind { get; internal set; }

    public int? RecoveryAmount { get; internal set; }

    public bool IsCritical { get; internal set; }

    public BattleHitState HitState { get; internal set; } = BattleHitState.Hit;

    /// <summary>
    /// 命中结算第一阶段：钩子只落地改变命中状态的效果（cancel_hit / set_hit_success），
    /// 其余效果、飘字与台词登记为待提交结果，等最终命中状态确定后再按一致性提交。
    /// </summary>
    internal bool DeferHitOutcomes { get; set; }

    internal List<PendingHitOutcome> PendingHitOutcomes { get; } = [];

    public bool SuppressHitEffects { get; internal set; }

    public bool Cancel { get; internal set; }

    public bool IsDefeatPrevented { get; private set; }

    public bool IsActionSkipRequested { get; private set; }

    public string? ActionSkipReason { get; private set; }

    public void SkipCurrentAction(string? reason = null)
    {
        if (Timing != HookTiming.BeforeActionStart)
        {
            throw new InvalidOperationException(
                $"An action can only be skipped during '{HookTiming.BeforeActionStart}'.");
        }

        IsActionSkipRequested = true;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            ActionSkipReason ??= reason;
        }
    }

    public int SetRage(int value, string? detail = null)
    {
        if (Timing != HookTiming.BeforeActionStart)
        {
            throw new InvalidOperationException(
                $"Rage can only be set through an action-start effect during '{HookTiming.BeforeActionStart}'.");
        }

        return BattleResourceResolver.SetRage(State, Unit, value, Timing, detail);
    }

    public int RemoveNegativeBuffs()
    {
        if (Timing != HookTiming.BeforeActionReadiness)
        {
            throw new InvalidOperationException(
                $"Negative buffs can only be removed through an action-readiness effect during '{HookTiming.BeforeActionReadiness}'.");
        }

        return Engine.BuffResolver.Remove(
            State,
            Unit,
            Unit,
            static buff => buff.Definition.IsDebuff,
            Timing).Count;
    }

    public void RequestSpeech(BattleUnit speaker, string text)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        BattleSpeechRuntime.TryEmit(State, speaker, text, Timing);
    }

    public void RequestFloatText(
        BattleUnit target,
        string text,
        BattleFloatTextStyle style = BattleFloatTextStyle.Normal)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        State.AddMessage(new BattleCue(
            BattleCueKind.FloatTextRequested,
            target.Id,
            Timing,
            floatText: new BattleFloatTextCue(text, style)));
    }

    public void RedirectDamage(BattleUnit target, double damageFactor)
    {
        if (Timing != HookTiming.BeforeDamageApplied)
        {
            throw new InvalidOperationException(
                $"Damage can only be redirected during '{HookTiming.BeforeDamageApplied}'.");
        }

        ArgumentNullException.ThrowIfNull(target);
        ArgumentOutOfRangeException.ThrowIfNegative(damageFactor);
        Target = target;
        DamageAmount = Math.Max(0, (int)((DamageAmount ?? 0) * damageFactor));
    }

    public void CapDamage(int maximum)
    {
        if (Timing != HookTiming.BeforeDamageApplied)
        {
            throw new InvalidOperationException(
                $"Damage can only be capped during '{HookTiming.BeforeDamageApplied}'.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(maximum);
        DamageAmount = Math.Min(DamageAmount ?? 0, maximum);
    }

    public void CancelDamage(bool suppressHitEffects)
    {
        if (Timing != HookTiming.BeforeDamageApplied)
        {
            throw new InvalidOperationException(
                $"Damage can only be cancelled during '{HookTiming.BeforeDamageApplied}'.");
        }

        DamageAmount = 0;
        IsDamageCancelled = true;
        SuppressHitEffects |= suppressHitEffects;
    }

    public void TransferDamage(BattleUnit target, int amount, string? detail = null)
    {
        EnsureTiming(HookTiming.BeforeDamageApplied, "Damage can only be transferred");
        ArgumentNullException.ThrowIfNull(target);
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        var current = DamageAmount ?? 0;
        var transferred = Math.Min(amount, current);
        if (transferred <= 0)
        {
            return;
        }

        DamageAmount = current - transferred;
        (_pendingDamageTransfers ??= []).Add(new BattleDamageComponent(target, transferred, detail, IsTransfer: true));
    }

    public int ReflectDamage(BattleUnit target, int amount, string? detail = null)
    {
        EnsureTiming(HookTiming.BeforeDamageApplied, "Damage can only be reflected");
        ArgumentNullException.ThrowIfNull(target);
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        return Engine.ResolveDamage(State, BattleDamageDescriptor.ForReflection(Unit, target, amount) with
        {
            Detail = detail,
            EventTiming = Timing,
        }).ActualAmount;
    }

    public int ShareDamage(BattleUnit target, int amount, string? detail = null)
    {
        EnsureTiming(HookTiming.BeforeDamageApplied, "Damage can only be shared");
        ArgumentNullException.ThrowIfNull(target);
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        return Engine.ResolveDamage(State, BattleDamageDescriptor.ForShare(Source ?? Unit, target, amount) with
        {
            Detail = detail,
            EventTiming = Timing,
        }).ActualAmount;
    }

    private void EnsureTiming(HookTiming expected, string operation)
    {
        if (Timing != expected)
        {
            throw new InvalidOperationException($"{operation} during '{expected}'.");
        }
    }

    private void ResetUnitSkillCooldowns(string? detail)
    {
        if (Timing != HookTiming.BeforeHitResolved)
        {
            throw new InvalidOperationException(
                $"Skill cooldowns can only be reset during '{HookTiming.BeforeHitResolved}'.");
        }

        Unit.Character.ResetSkillCooldowns();
        State.AddMessage(new BattleFact(
            BattleFactKind.SkillCooldownsReset,
            Unit.Id,
            Timing,
            detail: detail));
    }

    int IDefeatPreventionEffectContext.GetAbilityUsageCount(string abilityId) =>
        Unit.GetAbilityUsageCount(abilityId);

    bool IDefeatPreventionEffectContext.TryPreventDefeat(BattleDefeatPrevention prevention)
    {
        EnsureTiming(HookTiming.BeforeDefeated, "Defeat can only be prevented");
        ArgumentNullException.ThrowIfNull(prevention);
        if (IsDefeatPrevented || Unit.IsAlive || Unit.Mp < prevention.MpCost)
        {
            return false;
        }

        // 阻止败北本身：以 1 HP 存活。它不是恢复，不经过恢复修正，否则重伤等会让阻止失效。
        Unit.RestoreHp(1);
        IsDefeatPrevented = true;
        State.AddMessage(new BattleFact(
            BattleFactKind.DefeatPrevented,
            Unit.Id,
            Timing,
            detail: prevention.AbilityId));
        if (prevention.MpCost > 0)
        {
            BattleResourceResolver.SpendMp(State, Unit, prevention.MpCost, Timing, prevention.AbilityId);
        }

        if (prevention.HpRecovery > 0)
        {
            RestoreHpCore(Unit, Unit, prevention.HpRecovery, prevention.AbilityId);
        }

        if (prevention.MpRecovery > 0)
        {
            RecoverMpCore(Unit, Unit, prevention.MpRecovery, prevention.AbilityId);
        }

        if (prevention.Rage is { } rage)
        {
            BattleResourceResolver.SetRage(State, Unit, rage, Timing, prevention.AbilityId);
        }

        Unit.RecordAbilityUsage(prevention.AbilityId);
        if (!string.IsNullOrWhiteSpace(prevention.FloatText))
        {
            RequestFloatText(Unit, prevention.FloatText, BattleFloatTextStyle.Special);
        }

        if (!string.IsNullOrWhiteSpace(prevention.Speech))
        {
            RequestSpeech(Unit, prevention.Speech);
        }

        return true;
    }

    private InvalidOperationException MissingCapability(string capability) =>
        new($"Timing '{Timing}' did not provide required capability '{capability}'.");

    private int RestoreHpCore(BattleUnit source, BattleUnit target, int amount, string? detail)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        var restored = Engine.RecoveryResolver.Apply(
            State,
            source,
            target,
            BattleRecoveryKind.Hp,
            amount).ActualAmount;
        State.AddMessage(new BattleFact(
            BattleFactKind.Healed,
            target.Id,
            Timing,
            detail: detail ?? restored.ToString()));
        return restored;
    }

    private int RecoverMpCore(BattleUnit source, BattleUnit target, int amount, string? detail)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        var restored = Engine.RecoveryResolver.Apply(
            State,
            source,
            target,
            BattleRecoveryKind.Mp,
            amount).ActualAmount;
        if (restored > 0)
        {
            State.AddMessage(new BattleFact(
                BattleFactKind.MpRecovered,
                target.Id,
                Timing,
                detail: detail ?? restored.ToString()));
        }

        return restored;
    }

    private int DamageMpCore(BattleUnit target, int amount, string? detail)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentOutOfRangeException.ThrowIfNegative(amount);

        return BattleResourceResolver.DamageMp(State, target, amount, Timing, detail);
    }

    int IDamageApplicationEffectContext.ApplyHpRecovery(BattleUnit target, int amount, string? detail)
    {
        EnsureTiming(HookTiming.BeforeDamageApplied, "Damage-application recovery can only occur");
        return RestoreHpCore(Source ?? Unit, target, amount, detail);
    }

    int IDamageApplicationEffectContext.DamageMp(BattleUnit target, int amount, string? detail)
    {
        EnsureTiming(HookTiming.BeforeDamageApplied, "Damage-application MP damage can only occur");
        return DamageMpCore(target, amount, detail);
    }

    int IActionStartEffectContext.ApplyHpRecovery(BattleUnit target, int amount, string? detail)
    {
        EnsureTiming(HookTiming.BeforeActionStart, "Action-start recovery can only occur");
        return RestoreHpCore(Source ?? Unit, target, amount, detail);
    }

    int IActionStartEffectContext.DamageMp(BattleUnit target, int amount, string? detail)
    {
        EnsureTiming(HookTiming.BeforeActionStart, "Action-start MP damage can only occur");
        return DamageMpCore(target, amount, detail);
    }

    int IHitConfirmedEffectContext.DamageMp(BattleUnit target, int amount, string? detail)
    {
        EnsureTiming(HookTiming.OnHitConfirmed, "Hit-confirmed MP damage can only occur");
        return DamageMpCore(target, amount, detail);
    }

    int IHitConfirmedEffectContext.RecoverMp(BattleUnit target, int amount, string? detail)
    {
        EnsureTiming(HookTiming.OnHitConfirmed, "Hit-confirmed MP recovery can only occur");
        return RecoverMpCore(Source ?? Unit, target, amount, detail);
    }

    bool IHitConfirmedEffectContext.IsCellAvailable(GridPosition position, BattleUnit movingUnit)
    {
        if (Timing != HookTiming.OnHitConfirmed)
        {
            throw new InvalidOperationException(
                $"Hit-confirmed relocation can only inspect cells during '{HookTiming.OnHitConfirmed}'.");
        }

        ArgumentNullException.ThrowIfNull(movingUnit);
        return State.Grid.IsWalkable(position) && !State.IsOccupied(position, movingUnit.Id);
    }

    bool IHitConfirmedEffectContext.TryRelocate(BattleUnit target, GridPosition destination)
    {
        if (Timing != HookTiming.OnHitConfirmed)
        {
            throw new InvalidOperationException(
                $"Hit-confirmed relocation can only occur during '{HookTiming.OnHitConfirmed}'.");
        }

        return Engine.TryRelocateByEffect(State, target, destination);
    }

    bool IHitConfirmedEffectContext.ApplyBuff(
        BattleUnit target,
        string buffId,
        int level,
        int duration)
    {
        if (Timing != HookTiming.OnHitConfirmed)
        {
            throw new InvalidOperationException(
                $"A hit-confirmed buff can only be applied during '{HookTiming.OnHitConfirmed}'.");
        }

        var source = Source ?? Unit;
        return Engine.ApplyBuffByEffect(
            State,
            source,
            target,
            buffId,
            level,
            duration,
            Timing);
    }



}
