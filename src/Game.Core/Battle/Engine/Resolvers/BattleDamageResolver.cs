using Game.Core.Affix;
using Game.Core.Model;
using Game.Core.Model.Skills;

namespace Game.Core.Battle;

/// <summary>同一次伤害的一个承伤分量（主承伤者或经 TransferDamage 转移出去的部分）。</summary>
internal sealed record BattleDamageComponent(BattleUnit Target, int Amount, string? Detail, bool IsTransfer);

/// <summary>
/// 公共伤害结算管线。所有扣血都由 <see cref="BattleDamageDescriptor"/> 描述并经过这里，阶段为：
/// 命中（按描述）→ 落血前改写（BeforeDamageApplied，可选）→ 各承伤分量扣血
/// → 每个分量的承伤后响应（OnDamageTaken、受击回怒，可选）与战败阶段（必经）
/// → 来源侧 OnDamageDealt 与吸血（可选，按全部分量实际 HP 损失汇总后只执行一次）。
/// </summary>
internal sealed class BattleDamageResolver(BattleEngine engine)
{
    public BattleDamageResolution Resolve(BattleState state, BattleDamageDescriptor damage)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(damage);

        if (damage.HitMode == BattleDamageHitMode.Judged)
        {
            var hit = engine.JudgeHit(state, damage.Source, damage.Target, damage.Skill!);
            if (hit.IsCancelled)
            {
                BattleEngine.AddMessage(state, new BattleFact(
                    BattleFactKind.Damaged,
                    damage.Target.Id,
                    damage.EventTiming,
                    detail: damage.Source.Id,
                    damage: new BattleDamageEvent(0, SourceUnitId: damage.Source.Id)));
                return new BattleDamageResolution(damage.Target, 0, 0, IsHitCancelled: true,
                    IsDamageCancelled: false, hit.SuppressHitEffects);
            }
        }

        var primaryTarget = damage.Target;
        var primaryAmount = damage.Amount;
        var suppressHitEffects = false;
        var damageCancelled = false;
        IReadOnlyList<BattleDamageComponent> transfers = [];
        if (damage.Allows(BattleDamageReactions.PreApplication))
        {
            var application = engine.TriggerHooks(
                state,
                HookTiming.BeforeDamageApplied,
                damage.Target,
                context =>
                {
                    context.Source = damage.Source;
                    context.Target = damage.Target;
                    context.Skill = damage.Skill;
                    context.DamageAmount = damage.Amount;
                    context.IsCritical = damage.IsCritical;
                    context.DamageBeingApplied = damage;
                });
            primaryAmount = Math.Max(0, application.DamageAmount ?? damage.Amount);
            primaryTarget = application.Target ?? damage.Target;
            suppressHitEffects = application.SuppressHitEffects;
            damageCancelled = application.IsDamageCancelled;
            transfers = application.PendingDamageTransfers;
        }

        if (!damageCancelled)
        {
            primaryAmount = checked(primaryAmount + damage.UncappedBonusAmount);
        }

        // legacy 八卦阵先扣替挡者，再扣原目标；分量按此顺序结算。
        var components = transfers
            .Append(new BattleDamageComponent(primaryTarget, primaryAmount, damage.Detail, IsTransfer: false))
            .ToList();
        var primaryActual = 0;
        var totalActual = 0;
        foreach (var component in components)
        {
            var actual = ApplyComponent(state, damage, component);
            totalActual = checked(totalActual + actual);
            if (!component.IsTransfer)
            {
                primaryActual = actual;
            }
        }

        if (totalActual > 0)
        {
            ApplyDealtResponses(state, damage, primaryTarget, totalActual);
        }

        return new BattleDamageResolution(
            primaryTarget,
            primaryActual,
            totalActual,
            IsHitCancelled: false,
            damageCancelled,
            suppressHitEffects);
    }

    private int ApplyComponent(BattleState state, BattleDamageDescriptor damage, BattleDamageComponent component)
    {
        var target = component.Target;
        var amount = component.Amount;
        if (damage.MinimumRemainingHp > 0)
        {
            amount = Math.Min(amount, Math.Max(0, target.Hp - damage.MinimumRemainingHp));
        }

        var actualAmount = target.TakeDamage(amount);
        BattleEngine.AddMessage(state, new BattleFact(
            BattleFactKind.Damaged,
            target.Id,
            damage.EventTiming,
            detail: component.Detail ?? damage.Source.Id,
            damage: new BattleDamageEvent(actualAmount, damage.IsCritical, damage.Source.Id)));
        if (actualAmount <= 0)
        {
            return 0;
        }

        if (damage.Allows(BattleDamageReactions.TakenResponses))
        {
            engine.TriggerHooks(state, HookTiming.OnDamageTaken, target, context =>
            {
                context.Source = damage.Source;
                context.Target = target;
                context.Skill = damage.Skill;
                context.DamageAmount = actualAmount;
                context.IsCritical = damage.IsCritical;
            });
        }

        if (damage.Allows(BattleDamageReactions.TakenRage))
        {
            TryGainRageFromTakingDamage(state, damage.Source, target);
        }

        if (!target.IsAlive)
        {
            ResolveDefeat(state, damage, target, amount, actualAmount);
        }

        return actualAmount;
    }

    private void ResolveDefeat(
        BattleState state,
        BattleDamageDescriptor damage,
        BattleUnit target,
        int incomingAmount,
        int actualAmount)
    {
        engine.TriggerHooks(state, HookTiming.BeforeDefeated, target, context =>
        {
            context.Source = damage.Source;
            context.Target = target;
            context.Skill = damage.Skill;
            context.IncomingDamageAmount = incomingAmount;
            context.DamageAmount = actualAmount;
            context.IsCritical = damage.IsCritical;
        });

        if (target.IsAlive)
        {
            return;
        }

        state.AddMessage(new BattleFact(
            BattleFactKind.UnitDefeated,
            target.Id,
            HookTiming.OnDefeated,
            damage.Source.Id));
        engine.TriggerHooks(state, HookTiming.OnDefeated, target, context =>
        {
            context.Source = damage.Source;
            context.Target = target;
            context.Skill = damage.Skill;
            context.IncomingDamageAmount = incomingAmount;
            context.DamageAmount = actualAmount;
            context.IsCritical = damage.IsCritical;
        });
        state.ScopedEffects.NotifyUnitDefeated(state, target);
    }

    private void ApplyDealtResponses(
        BattleState state,
        BattleDamageDescriptor damage,
        BattleUnit primaryTarget,
        int totalActual)
    {
        if (!damage.Allows(BattleDamageReactions.DealtResponses))
        {
            return;
        }

        var lifestealRate = Math.Max(
            0d,
            damage.Source.GetStat(StatType.Lifesteal) +
            (damage.Skill?.IntrinsicLifestealRate ?? 0d) +
            damage.LifestealRateBonus);
        var dealt = engine.TriggerHooks(state, HookTiming.OnDamageDealt, damage.Source, context =>
        {
            context.Source = damage.Source;
            context.Target = primaryTarget;
            context.Skill = damage.Skill;
            context.DamageAmount = totalActual;
            context.IsCritical = damage.IsCritical;
            context.LifestealRate = lifestealRate;
        });

        if (damage.Allows(BattleDamageReactions.Lifesteal))
        {
            ApplyLifesteal(state, damage, primaryTarget, totalActual, dealt.LifestealRate ?? lifestealRate);
        }
    }

    private void TryGainRageFromTakingDamage(BattleState state, BattleUnit source, BattleUnit target)
    {
        if (!state.AreEnemies(source, target))
        {
            return;
        }

        // 攻击级语义（legacy AttackLogic）：一次主动技能施放内，每个受击单位最多判定一次受击回怒，
        // 招式追加的多段伤害、直接伤害等不再各自判定；技能之外的伤害（无施放上下文）仍逐次判定。
        if (state.ActiveSkillExecution is { } execution && !execution.TryClaimDamagedRageRoll(target))
        {
            return;
        }

        BattleRageGainResolver.TryGain(
            state,
            target,
            engine.RandomService,
            HookTiming.OnDamageTaken,
            "damaged");
    }

    private void ApplyLifesteal(
        BattleState state,
        BattleDamageDescriptor damage,
        BattleUnit primaryTarget,
        int totalActual,
        double rate)
    {
        if (damage.Skill is null ||
            !damage.Source.IsAlive ||
            damage.Source.Team == primaryTarget.Team)
        {
            return;
        }

        var requestedAmount = (int)Math.Floor(totalActual * Math.Max(0d, rate));
        if (requestedAmount <= 0)
        {
            return;
        }

        var actualAmount = engine.RecoveryResolver.Apply(
            state,
            damage.Source,
            damage.Source,
            BattleRecoveryKind.Hp,
            requestedAmount).ActualAmount;
        if (actualAmount > 0)
        {
            BattleEngine.AddMessage(state, new BattleFact(
                BattleFactKind.Lifesteal,
                damage.Source.Id,
                HookTiming.OnDamageDealt,
                lifesteal: new BattleLifestealEvent(actualAmount)));
        }
    }
}
