namespace Game.Core.Battle;

/// <summary>
/// 把技能执行预览折算成 AI 评分。伤害与击杀沿用原版 AI 的口径（敌方伤害总和、友伤扣分），
/// 非伤害收益（状态、驱散、回复、怒气、集气）折算成“等价气血”记入 <see cref="BattleSkillAiEvaluation.UtilityScore"/>：
/// 对敌方有害、对己方有利为正，反之为负。数值刻意保守，使原版以伤害为主的决策倾向不被改变。
/// </summary>
internal static class BattleAiOutcomeValuation
{
    /// <summary>一个 0 级、持续 2 回合状态的最低价值（等价气血）。</summary>
    private const double MinimumStatusValue = 80d;

    /// <summary>状态价值随目标气血上限增长的比例：越重要的目标，控制/增益越值钱。</summary>
    private const double StatusValuePerMaxHp = 0.04d;

    private const double StatusValuePerLevel = 0.15d;
    private const int StatusDurationCap = 4;
    private const double RageValuePerPoint = 120d;
    private const double ActionGaugeValuePerPoint = 4d;
    private const double MpRecoveryValueFactor = 0.5d;

    public static BattleSkillAiEvaluation Evaluate(
        BattleState state,
        BattleUnit source,
        IReadOnlyList<BattleSkillPreviewEffect> effects)
    {
        var enemyDamage = 0;
        var allyDamage = 0;
        var enemyKills = 0;
        var allyKills = 0;
        var enemyHitCount = 0;
        var utility = 0d;

        for (var index = 0; index < effects.Count; index++)
        {
            var effect = effects[index];
            if (effect.Kind == BattleSkillPreviewEffectKind.Damage)
            {
                // 同一单位可能被多个效果命中: 汇总后再判定击杀与命中
                if (IsFirstDamageEntry(effects, index))
                {
                    var damage = SumDamage(effects, index);
                    var isKill = damage >= effect.Unit.Hp;
                    if (state.AreEnemies(source, effect.Unit))
                    {
                        enemyDamage += damage;
                        enemyHitCount += damage > 0 ? 1 : 0;
                        enemyKills += isKill ? 1 : 0;
                    }
                    else
                    {
                        allyDamage += damage;
                        allyKills += isKill ? 1 : 0;
                    }
                }

                continue;
            }

            utility += EvaluateUtility(state, source, effect);
        }

        return new BattleSkillAiEvaluation(
            enemyDamage,
            allyDamage,
            enemyKills,
            allyKills,
            enemyHitCount,
            utility);
    }

    private static double EvaluateUtility(BattleState state, BattleUnit source, BattleSkillPreviewEffect effect)
    {
        var target = effect.Unit;
        var isEnemy = state.AreEnemies(source, target);
        switch (effect.Kind)
        {
            case BattleSkillPreviewEffectKind.ApplyBuff when effect.Buff is { } buff:
                if (IsRedundant(target, buff.Id, effect.BuffLevel, effect.BuffDuration) ||
                    buff.IsDebuff && target.HasBuff(BattleContentIds.HolyWar))
                {
                    return 0d;
                }

                var applyValue = StatusValue(target, effect.BuffLevel, effect.BuffDuration) * effect.Chance;
                return buff.IsDebuff == isEnemy ? applyValue : -applyValue;
            case BattleSkillPreviewEffectKind.RemoveBuff when effect.Buff is { } removed:
                var removeValue = StatusValue(target, effect.BuffLevel, effect.BuffDuration);
                // 驱散敌方增益、解除己方减益为正
                return removed.IsDebuff != isEnemy ? removeValue : -removeValue;
            case BattleSkillPreviewEffectKind.RageChange:
                return (isEnemy ? -effect.Amount : effect.Amount) * RageValuePerPoint;
            case BattleSkillPreviewEffectKind.ActionGaugeChange:
                return (isEnemy ? -effect.Amount : effect.Amount) * ActionGaugeValuePerPoint;
            case BattleSkillPreviewEffectKind.HpRecovery:
                return isEnemy ? -effect.Amount : effect.Amount;
            case BattleSkillPreviewEffectKind.MpRecovery:
                return (isEnemy ? -effect.Amount : effect.Amount) * MpRecoveryValueFactor;
            default:
                return 0d;
        }
    }

    private static double StatusValue(BattleUnit target, int level, int duration) =>
        Math.Max(MinimumStatusValue, StatusValuePerMaxHp * target.MaxHp) *
        (1d + StatusValuePerLevel * Math.Max(0, level)) *
        Math.Clamp(duration, 1, StatusDurationCap) / 2d;

    /// <summary>目标已有同名且等级、剩余回合都不低于新状态时，施加不会覆盖（与状态替换规则一致）。</summary>
    private static bool IsRedundant(BattleUnit target, string buffId, int level, int duration) =>
        target.TryGetBuff(buffId) is { IsExpired: false } existing &&
        existing.Level >= level &&
        existing.RemainingTurns >= duration;

    private static bool IsFirstDamageEntry(IReadOnlyList<BattleSkillPreviewEffect> effects, int index)
    {
        var unit = effects[index].Unit;
        for (var previous = 0; previous < index; previous++)
        {
            if (effects[previous].Kind == BattleSkillPreviewEffectKind.Damage &&
                ReferenceEquals(effects[previous].Unit, unit))
            {
                return false;
            }
        }

        return true;
    }

    private static int SumDamage(IReadOnlyList<BattleSkillPreviewEffect> effects, int firstIndex)
    {
        var unit = effects[firstIndex].Unit;
        var total = 0;
        for (var index = firstIndex; index < effects.Count; index++)
        {
            if (effects[index].Kind == BattleSkillPreviewEffectKind.Damage &&
                ReferenceEquals(effects[index].Unit, unit))
            {
                total += effects[index].Amount;
            }
        }

        return total;
    }
}
