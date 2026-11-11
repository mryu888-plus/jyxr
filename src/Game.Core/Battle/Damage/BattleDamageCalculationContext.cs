using Game.Core.Affix;
using Game.Core.Model.Skills;

namespace Game.Core.Battle;

public sealed class BattleDamageCalculationContext
{
    private readonly Dictionary<BattleDamageContextField, BattleDamageModifierBucket> _modifiers = [];

    public BattleDamageCalculationContext(BattleUnit source, BattleUnit target, SkillInstance skill)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(skill);

        Source = source;
        Target = target;
        Skill = skill;
    }

    public BattleUnit Source { get; }

    public BattleUnit Target { get; }

    public SkillInstance Skill { get; }

    public double AttackLow { get; set; }

    public double AttackHigh { get; set; }

    public double CriticalChance { get; set; }

    public double CriticalMultiplier { get; set; } = 1d;

    public double Defence { get; set; }

    public double AdditionalLifestealRate { get; set; }

    /// <summary>
    /// 不受落血前封顶影响的追加伤害（攀云乘龙、无形剑气，legacy 在封顶之后加入）。
    /// 不进入普通伤害公式；在 BeforeDamageApplied 改写之后加到主承伤者，伤害被取消时一并作废。
    /// </summary>
    public int UncappedBonusDamage { get; private set; }

    public void AddUncappedBonusDamage(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        UncappedBonusDamage = checked(UncappedBonusDamage + amount);
    }

    public void AddModifier(BattleDamageContextField field, ModifierOp op, double value)
    {
        var bucket = _modifiers.TryGetValue(field, out var existing)
            ? existing
            : BattleDamageModifierBucket.Empty;
        bucket = bucket.Apply(op, value);
        _modifiers[field] = bucket;
    }

    public double Evaluate(BattleDamageContextField field, double baseValue) =>
        _modifiers.TryGetValue(field, out var bucket) ? bucket.Evaluate(baseValue) : baseValue;

    private readonly record struct BattleDamageModifierBucket(
        double Add,
        double Increase,
        double Multiplier,
        double PostAdd,
        double? Override)
    {
        public static BattleDamageModifierBucket Empty => new(0d, 0d, 1d, 0d, null);

        public BattleDamageModifierBucket Apply(ModifierOp op, double value) =>
            op switch
            {
                ModifierOp.Add => this with { Add = Add + value },
                ModifierOp.Increase => this with { Increase = Increase + value },
                ModifierOp.More => this with { Multiplier = Multiplier * value },
                ModifierOp.PostAdd => this with { PostAdd = PostAdd + value },
                ModifierOp.Override => this with { Override = value },
                _ => throw new ArgumentOutOfRangeException(nameof(op), op, null)
            };

        public double Evaluate(double baseValue) => Override ?? (baseValue + Add) * (1d + Increase) * Multiplier + PostAdd;
    }
}
