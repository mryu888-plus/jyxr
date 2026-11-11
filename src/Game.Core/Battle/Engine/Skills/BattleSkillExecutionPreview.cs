using Game.Core.Definitions;
using Game.Core.Definitions.Skills;
using Game.Core.Model;
using Game.Core.Model.Skills;

namespace Game.Core.Battle;

public enum BattleSkillPreviewEffectKind
{
    /// <summary>预计造成的气血伤害（Amount 为伤害值）。</summary>
    Damage,
    /// <summary>预计实际回复的气血（Amount 为回复值，已按缺口截断）。</summary>
    HpRecovery,
    /// <summary>预计实际回复的内力（Amount 为回复值，已按缺口截断）。</summary>
    MpRecovery,
    /// <summary>预计施加状态（Buff/Level/Duration/Chance 有效）。</summary>
    ApplyBuff,
    /// <summary>预计移除一个已存在的状态（Buff 为被移除状态的定义，Level 为其当前等级）。</summary>
    RemoveBuff,
    /// <summary>怒气变化（Amount 为实际增减量）。</summary>
    RageChange,
    /// <summary>集气条变化（Amount 为实际增减量）。</summary>
    ActionGaugeChange,
}

/// <summary>技能执行预览中的一项结果：作用到某个单位上的一次预计变化。</summary>
public readonly record struct BattleSkillPreviewEffect(
    BattleUnit Unit,
    BattleSkillPreviewEffectKind Kind,
    int Amount = 0,
    BuffDefinition? Buff = null,
    int BuffLevel = 0,
    int BuffDuration = 0,
    double Chance = 1d);

public sealed partial class BattleEngine
{
    /// <summary>
    /// 按与 <see cref="CastSkill"/> 相同的执行计划预览一次施放：施法者位于 <paramref name="sourcePosition"/>，
    /// 主要目标为 <paramref name="primaryTargets"/>；选择器（如 all_enemies、nearby_allies）按施法者所在位置解析出实际作用单位，
    /// 并汇总伤害、回复、状态、怒气与集气变化。不修改战斗状态，也不消耗随机数。
    /// </summary>
    /// <param name="estimateSkillDamage">普通伤害步骤对（技能，目标）的伤害估算（调用方可缓存）。</param>
    public void PreviewSkillExecution(
        BattleState state,
        BattleUnit source,
        GridPosition sourcePosition,
        SkillInstance skill,
        IReadOnlyList<BattleUnit> primaryTargets,
        Func<SkillInstance, BattleUnit, int> estimateSkillDamage,
        ICollection<BattleSkillPreviewEffect> output)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(skill);
        ArgumentNullException.ThrowIfNull(primaryTargets);
        ArgumentNullException.ThrowIfNull(estimateSkillDamage);
        ArgumentNullException.ThrowIfNull(output);

        var plan = BattleSkillExecutionPlanFactory.Create(skill);
        foreach (var step in plan.Steps)
        {
            switch (step)
            {
                case ResolveSkillDamageStep:
                    foreach (var target in primaryTargets)
                    {
                        var damage = estimateSkillDamage(skill, target);
                        if (damage > 0)
                        {
                            output.Add(new BattleSkillPreviewEffect(target, BattleSkillPreviewEffectKind.Damage, damage));
                        }

                        PreviewSkillBuffs(source, target, skill.Buffs, output);
                    }
                    break;
                case ApplySkillBuffsStep:
                    foreach (var target in primaryTargets)
                    {
                        PreviewSkillBuffs(source, target, skill.Buffs, output);
                    }
                    break;
                case ApplyDefinedSkillEffectsStep definedEffects:
                    foreach (var effect in definedEffects.Effects)
                    {
                        PreviewDefinedEffect(state, source, sourcePosition, skill, primaryTargets, effect, output);
                    }
                    break;
                case AttemptAttackRageGainStep:
                    break;
                default:
                    throw new NotSupportedException($"Unsupported skill execution step '{step.GetType().Name}'.");
            }
        }
    }

    private static void PreviewSkillBuffs(
        BattleUnit source,
        BattleUnit target,
        IReadOnlyList<SkillBuffDefinition> buffs,
        ICollection<BattleSkillPreviewEffect> output)
    {
        foreach (var buff in buffs)
        {
            // 与 ApplySkillBuffs 一致: 减益施加给目标, 增益施加给施法者
            var buffTarget = buff.Buff.IsDebuff ? target : source;
            output.Add(new BattleSkillPreviewEffect(
                buffTarget,
                BattleSkillPreviewEffectKind.ApplyBuff,
                Buff: buff.Buff,
                BuffLevel: buff.Level,
                BuffDuration: buff.Duration,
                Chance: BattleSkillBuffChanceResolver.Resolve(buff, source, target)));
        }
    }

    private void PreviewDefinedEffect(
        BattleState state,
        BattleUnit source,
        GridPosition sourcePosition,
        SkillInstance skill,
        IReadOnlyList<BattleUnit> primaryTargets,
        BattleEffectDefinition effect,
        ICollection<BattleSkillPreviewEffect> output)
    {
        IReadOnlyList<BattleUnit> ResolveTargets(BattleUnitSelectorDefinition selector) =>
            BattleUnitSelectorResolver.Resolve(state, source, sourcePosition, source, primaryTargets, selector);

        switch (effect)
        {
            case CustomAbilityBattleEffectDefinition custom:
                var sourceCost = custom.EstimateAbilitySourceDamage(
                    new BattleAbilityDamageEstimateContext(state, source, source, skill, sourcePosition));
                if (sourceCost > 0)
                {
                    output.Add(new BattleSkillPreviewEffect(source, BattleSkillPreviewEffectKind.Damage, sourceCost));
                }

                foreach (var target in ResolveTargets(custom.Target))
                {
                    var estimateContext = new BattleAbilityDamageEstimateContext(state, source, target, skill, sourcePosition);
                    if (custom.EstimateAbilityDamage(estimateContext) is > 0 and var damage)
                    {
                        output.Add(new BattleSkillPreviewEffect(target, BattleSkillPreviewEffectKind.Damage, damage));
                    }

                    foreach (var buff in custom.PreviewAbilityBuffs(estimateContext))
                    {
                        if (TryResolveBuffDefinition(buff.BuffId) is { } definition)
                        {
                            output.Add(new BattleSkillPreviewEffect(
                                target,
                                BattleSkillPreviewEffectKind.ApplyBuff,
                                Buff: definition,
                                BuffLevel: buff.Level,
                                BuffDuration: buff.Duration,
                                Chance: buff.Chance));
                        }
                    }
                }
                break;
            case ApplyBuffBattleEffectDefinition applyBuff:
                var appliedBuff = TryResolveBuffDefinition(applyBuff);
                if (appliedBuff is null)
                {
                    break;
                }

                foreach (var target in ResolveTargets(applyBuff.Target))
                {
                    output.Add(new BattleSkillPreviewEffect(
                        target,
                        BattleSkillPreviewEffectKind.ApplyBuff,
                        Buff: appliedBuff,
                        BuffLevel: applyBuff.Level,
                        BuffDuration: applyBuff.Duration,
                        Chance: Math.Clamp(applyBuff.Chance / 100d, 0d, 1d)));
                }
                break;
            case RemoveBuffBattleEffectDefinition remove:
                foreach (var target in ResolveTargets(remove.Target))
                {
                    PreviewBuffRemoval(target, buff => string.Equals(buff.Definition.Id, remove.BuffId, StringComparison.Ordinal), output);
                }
                break;
            case RemoveNegativeBuffsBattleEffectDefinition removeNegative:
                foreach (var target in ResolveTargets(removeNegative.Target))
                {
                    PreviewBuffRemoval(target, static buff => buff.Definition.IsDebuff, output);
                }
                break;
            case RemovePositiveBuffsBattleEffectDefinition removePositive:
                foreach (var target in ResolveTargets(removePositive.Target))
                {
                    PreviewBuffRemoval(target, static buff => !buff.Definition.IsDebuff, output);
                }
                break;
            case AddRageBattleEffectDefinition addRage:
                foreach (var target in ResolveTargets(addRage.Target))
                {
                    AddResourceChange(target, BattleSkillPreviewEffectKind.RageChange,
                        Math.Clamp(target.Rage + addRage.Value, 0, BattleUnit.MaxRage) - target.Rage, output);
                }
                break;
            case SetRageBattleEffectDefinition setRage:
                foreach (var target in ResolveTargets(setRage.Target))
                {
                    AddResourceChange(target, BattleSkillPreviewEffectKind.RageChange,
                        Math.Clamp(setRage.Value, 0, BattleUnit.MaxRage) - target.Rage, output);
                }
                break;
            case AddActionGaugeBattleEffectDefinition addGauge:
                foreach (var target in ResolveTargets(addGauge.Target))
                {
                    AddResourceChange(target, BattleSkillPreviewEffectKind.ActionGaugeChange,
                        (int)Math.Round(Math.Max(0d, target.ActionGauge + addGauge.Value) - target.ActionGauge), output);
                }
                break;
            case SetActionGaugeBattleEffectDefinition setGauge:
                foreach (var target in ResolveTargets(setGauge.Target))
                {
                    AddResourceChange(target, BattleSkillPreviewEffectKind.ActionGaugeChange,
                        (int)Math.Round(setGauge.Value - target.ActionGauge), output);
                }
                break;
            case AddHpBattleEffectDefinition addHp:
                foreach (var target in ResolveTargets(addHp.Target))
                {
                    AddResourceChange(target, BattleSkillPreviewEffectKind.HpRecovery,
                        Math.Clamp(addHp.Value, 0, Math.Max(0, target.MaxHp - target.Hp)), output);
                }
                break;
            case AddMpBattleEffectDefinition addMp:
                foreach (var target in ResolveTargets(addMp.Target))
                {
                    AddResourceChange(target, BattleSkillPreviewEffectKind.MpRecovery,
                        Math.Clamp(addMp.Value, 0, Math.Max(0, target.MaxMp - target.Mp)), output);
                }
                break;
        }
    }

    private static void PreviewBuffRemoval(
        BattleUnit target,
        Func<BattleBuffInstance, bool> predicate,
        ICollection<BattleSkillPreviewEffect> output)
    {
        foreach (var buff in target.Buffs)
        {
            if (!buff.IsExpired && predicate(buff))
            {
                output.Add(new BattleSkillPreviewEffect(
                    target,
                    BattleSkillPreviewEffectKind.RemoveBuff,
                    Buff: buff.Definition,
                    BuffLevel: buff.Level,
                    BuffDuration: buff.RemainingTurns));
            }
        }
    }

    private static void AddResourceChange(
        BattleUnit target,
        BattleSkillPreviewEffectKind kind,
        int amount,
        ICollection<BattleSkillPreviewEffect> output)
    {
        if (amount != 0)
        {
            output.Add(new BattleSkillPreviewEffect(target, kind, amount));
        }
    }

    private BuffDefinition? TryResolveBuffDefinition(ApplyBuffBattleEffectDefinition effect)
    {
        try
        {
            return _battleBuffResolver.Resolve(effect);
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }

    private BuffDefinition? TryResolveBuffDefinition(string buffId)
    {
        try
        {
            return _battleBuffResolver.Resolve(buffId);
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }
}
