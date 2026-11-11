using Game.Core.Definitions.Skills;
using Game.Core.Model.Character;

namespace Game.Core.Model.Skills;

/// <summary>
/// 技能来源类别，与 <see cref="SkillKind"/>（技能形态）正交：招式和奥义保留自身形态，来源继承父技能。
/// </summary>
public enum SkillSourceKind
{
    [System.Text.Json.Serialization.JsonStringEnumMemberName("external")]
    External,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("internal")]
    Internal,
    [System.Text.Json.Serialization.JsonStringEnumMemberName("special")]
    Special,
}

public enum SkillKind
{
    External,
    Internal,
    Form,
    Special,
    Legend,
}

public abstract class SkillInstance(CharacterInstance owner)
{
    private int _level = 1;
    private int _exp;

    public CharacterInstance Owner { get; } = owner ?? throw new ArgumentNullException(nameof(owner));

    public virtual double Bonus => Owner.GetSkillBonusValue(Id);
    /// <summary>技能自带的吸血倍率，和角色吸血属性、本次攻击的吸血加成合并后结算。</summary>
    public virtual double IntrinsicLifestealRate => 0d;
    public abstract string Id { get; }
    public virtual string SourceSkillId => Id;
    public abstract string Name { get; }
    public virtual string Description => string.Empty;
    public virtual string Icon => string.Empty;
    public abstract string Animation { get; }
    public abstract string Audio { get; }
    public virtual int Level
    {
        get => _level;
        set
        {
            ValidateLevel(value);
            _level = value;
        }
    }

    public virtual int Exp
    {
        get => _exp;
        set
        {
            ValidateExperience(value);
            _exp = value;
        }
    }

    public virtual string CooldownKey => Id;
    public abstract int CurrentCooldown { get; set; }
    public abstract SkillKind SkillKind { get; }
    public abstract SkillSourceKind SourceKind { get; }
    public abstract WeaponType WeaponType { get; }
    public abstract double Power { get; }
    public abstract int MpCost { get; }
    public abstract int RageCost { get; }
    public abstract int Cooldown { get; }
    public abstract bool CanCastAtSelf { get; }
    public abstract bool CanImpactSelf { get; }
    public abstract int CastSize { get; }
    public abstract SkillImpactType ImpactType { get; }
    public abstract int ImpactSize { get; }
    public abstract IReadOnlyList<SkillBuffDefinition> Buffs { get; }
    public abstract bool IsHarmony { get; }
    public abstract double Affinity { get; }
    public abstract bool IsActive { get; }

    public virtual void ResetBattleCooldown()
    {
        CurrentCooldown = 0;
    }

    protected static void ValidateLevel(int level) => ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);

    protected static void ValidateExperience(int exp) => ArgumentOutOfRangeException.ThrowIfNegative(exp);

    protected static void ValidateMaxLevel(int maxLevel) => ArgumentOutOfRangeException.ThrowIfLessThan(maxLevel, 1);
}
