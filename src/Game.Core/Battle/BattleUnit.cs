using Game.Core;
using Game.Core.Affix;
using Game.Core.Model;
using Game.Core.Model.Character;

namespace Game.Core.Battle;

public sealed class BattleUnit
{
    public const int MaxRage = CharacterInstance.MaxBattleRage;

    private readonly List<BattleBuffInstance> _buffs = [];
    private readonly HashSet<string> _disabledSkillIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _abilityUsageCounts = new(StringComparer.Ordinal);
    private BattleEffectProjectionResolver? _projectionResolver;
    private AffixProjection? _localProjection;
    private IReadOnlyList<BattleBuffInstance>? _activeBuffs;
    private Dictionary<string, BattleBuffInstance>? _activeBuffsById;

    public BattleUnit(
        string id,
        CharacterInstance character,
        int team,
        GridPosition position,
        BattleFacing facing = BattleFacing.Right,
        int? hp = null,
        int? mp = null,
        int rage = 0,
        bool? receivesPersistentGrowth = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentOutOfRangeException.ThrowIfNegative(rage);

        Id = id;
        Character = character;
        Team = team;
        Position = position;
        Facing = facing;
        AiType = character.AiType;
        // 是否把战斗中的成长 (武学经验、人物经验、升级) 写回持久角色, 由构建战斗的一方 (应用层) 决定;
        // 未显式指定时沿用旧规则: 1 号阵营.
        ReceivesPersistentGrowth = receivesPersistentGrowth ?? team == 1;
        InitialCharacterLevel = character.Level;

        var initialMaxHp = MaxHp;
        var initialMaxMp = MaxMp;
        Hp = Math.Clamp(hp ?? initialMaxHp, 0, initialMaxHp);
        Mp = Math.Clamp(mp ?? initialMaxMp, 0, initialMaxMp);
        Rage = Math.Clamp(rage, 0, MaxRage);
    }

    public string Id { get; }

    public CharacterInstance Character { get; }

    public int Team { get; }

    /// <summary>战斗中的成长是否属于持久角色。由战斗构建方决定，引擎只读取。</summary>
    public bool ReceivesPersistentGrowth { get; }

    /// <summary>进入战斗时角色的等级，用于战斗结束后汇总升级。</summary>
    public int InitialCharacterLevel { get; }

    public GridPosition Position { get; internal set; }

    public BattleFacing Facing { get; internal set; }

    public BattleAiType AiType { get; private set; }

    public int MaxHp => ResolveMaxHp();

    public int Hp { get; private set; }

    public int MaxMp => ResolveMaxMp();

    public int Mp { get; private set; }

    public int Rage { get; private set; }

    public double ActionSpeed => GetActionSpeed();

    public int MovePower => GetMovePower();

    public double ActionGauge { get; set; }

    public int ItemCooldown { get; private set; }

    public string? LastUsedSkillId { get; private set; }

    public bool IsAlive => Hp > 0;

    public IReadOnlyList<BattleBuffInstance> Buffs => _buffs;

    /// <summary>未过期 buff 的快照；buff 增删或等级/回合变化时刷新，读取路径不再重复过滤分配。</summary>
    public IReadOnlyList<BattleBuffInstance> ActiveBuffs => _activeBuffs ??= _buffs.Where(static buff => !buff.IsExpired).ToList();

    public IReadOnlySet<string> DisabledSkillIds => _disabledSkillIds;

    public IReadOnlyDictionary<string, int> AbilityUsageCounts => _abilityUsageCounts;

    public void SetAiType(BattleAiType aiType)
    {
        AiType = aiType;
    }

    public bool HasTrait(TraitId traitId) => _projectionResolver is null
        ? DetachedProjection.Traits.Contains(traitId)
        : Resolver.HasTrait(this, traitId);

    internal void BindProjectionResolver(BattleEffectProjectionResolver resolver) =>
        _projectionResolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

    public void AddDisabledSkill(string skillId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skillId);
        _disabledSkillIds.Add(skillId);
    }

    public bool RemoveDisabledSkill(string skillId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skillId);
        return _disabledSkillIds.Remove(skillId);
    }

    public int GetAbilityUsageCount(string abilityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(abilityId);
        return _abilityUsageCounts.GetValueOrDefault(abilityId);
    }

    public int RecordAbilityUsage(string abilityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(abilityId);
        var count = checked(GetAbilityUsageCount(abilityId) + 1);
        _abilityUsageCounts[abilityId] = count;
        return count;
    }

    internal void ClampResourcesToLimits()
    {
        Hp = Math.Clamp(Hp, 0, MaxHp);
        Mp = Math.Clamp(Mp, 0, MaxMp);
    }

    public void SpendMp(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        if (Mp < amount)
        {
            throw new InvalidOperationException($"Unit '{Id}' does not have enough MP.");
        }

        Mp -= amount;
    }

    public void SpendRage(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        if (Rage < amount)
        {
            throw new InvalidOperationException($"Unit '{Id}' does not have enough rage.");
        }

        Rage -= amount;
    }

    public int RestoreHp(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        var before = Hp;
        Hp = Math.Min(MaxHp, Hp + amount);
        return Hp - before;
    }

    public int RestoreMp(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        var before = Mp;
        Mp = Math.Min(MaxMp, Mp + amount);
        return Mp - before;
    }

    public int DamageMp(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        var before = Mp;
        Mp = Math.Max(0, Mp - amount);
        return before - Mp;
    }

    public void AddRage(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        Rage = Math.Min(MaxRage, Rage + amount);
    }

    public void SetRage(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Rage = Math.Clamp(value, 0, MaxRage);
    }

    public void SetActionGauge(double value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ActionGauge = value;
    }

    public void AddItemCooldown(int cooldown)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cooldown);
        ItemCooldown = checked(ItemCooldown + cooldown);
    }

    public void RecoverItemCooldown()
    {
        if (ItemCooldown > 0)
        {
            ItemCooldown--;
        }
    }

    public void RecordUsedSkill(string skillId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skillId);
        LastUsedSkillId = skillId;
    }

    public int TakeDamage(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        var before = Hp;
        Hp = Math.Max(0, Hp - amount);
        return before - Hp;
    }

    public bool TryApplyBuff(BattleBuffInstance buff)
    {
        var existing = _buffs.FirstOrDefault(existingBuff =>
            string.Equals(existingBuff.Definition.Id, buff.Definition.Id, StringComparison.Ordinal));
        if (existing is not null &&
            !existing.IsExpired &&
            (buff.Level < existing.Level ||
             buff.Level == existing.Level && buff.RemainingTurns < existing.RemainingTurns))
        {
            return false;
        }

        if (existing is not null)
        {
            _buffs.Remove(existing);
            existing.Detach();
        }

        _buffs.Add(buff);
        buff.AttachTo(OnBuffsChanged);
        OnBuffsChanged();
        return true;
    }

    public IReadOnlyList<BattleBuffInstance> RemoveBuffs(Func<BattleBuffInstance, bool> predicate)
    {
        var removed = _buffs
            .Where(predicate)
            .ToList();
        _buffs.RemoveAll(buff => removed.Contains(buff));
        foreach (var buff in removed)
        {
            buff.Detach();
        }

        if (removed.Count > 0)
        {
            OnBuffsChanged();
        }
        else
        {
            ClampResourcesToLimits();
        }
        return removed
            .Where(static buff => !buff.IsExpired)
            .ToList();
    }

    internal bool RemoveBuff(BattleBuffInstance buff)
    {
        if (!_buffs.Remove(buff))
        {
            return false;
        }

        buff.Detach();
        OnBuffsChanged();
        return true;
    }

    public double GetStat(StatType statType) =>
        _projectionResolver is null
            ? GetBucket(DetachedProjection.StatModifierBuckets, statType).Evaluate(Character.GetBaseStat(statType))
            : Resolver.GetStat(this, statType);

    public double GetWeaponBonusValue(WeaponType weaponType, double baseValue) =>
        _projectionResolver is null
            ? GetBucket(DetachedProjection.WeaponModifierBuckets, weaponType).Evaluate(baseValue)
            : Resolver.GetWeaponBonus(this, weaponType, baseValue);

    public int GetSkillTargetingValue(Model.Skills.SkillInstance skill, SkillTargetingField field, int baseValue) =>
        _projectionResolver is null
            ? DetachedProjection.EvaluateSkillTargeting(skill, field, baseValue)
            : Resolver.GetSkillTargeting(this, skill, field, baseValue);

    public double GetActionSpeed()
    {
        if (HasTrait(TraitId.Ghost))
        {
            return 3.5d;
        }

        var speed = GetStat(StatType.Shenfa) / 100d + GetStat(StatType.Gengu) / 130d;
        speed = Math.Clamp(speed, 1d, 2.2d);
        speed += GetStat(StatType.Speed);

        if (TryGetBuff(BattleContentIds.Paralysis) is { Level: > 0 } paralysis)
        {
            speed -= paralysis.Level * 0.2d;
        }

        if (TryGetBuff(BattleContentIds.Swift) is { } swift)
        {
            speed += swift.Level * 0.2d;
        }

        speed = Math.Clamp(speed, 0.8d, 2.5d);
        if (HasBuff(BattleContentIds.Stun))
        {
            return 0d;
        }

        return speed;
    }

    public int GetMovePower()
    {
        if (HasTrait(TraitId.CannotMove))
        {
            return 0;
        }

        var movePower = 2;
        var shenfa = GetStat(StatType.Shenfa);
        if (shenfa > 100d)
        {
            movePower++;
        }

        if (shenfa > 180d)
        {
            movePower++;
        }

        if (shenfa > 250d)
        {
            movePower++;
        }

        movePower += (int)GetStat(StatType.Movement);
        return Math.Clamp(movePower, 1, 5);
    }

    public bool HasBuff(string buffId) => TryGetBuff(buffId) is not null;

    public BattleBuffInstance? TryGetBuff(string buffId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buffId);

        _activeBuffsById ??= ActiveBuffs.ToDictionary(static buff => buff.Definition.Id, StringComparer.Ordinal);
        return _activeBuffsById.GetValueOrDefault(buffId);
    }

    internal IReadOnlyList<BattleBuffInstance> RemoveExpiredBuffs()
    {
        var expired = _buffs.Where(static buff => buff.IsExpired).ToList();
        _buffs.RemoveAll(static buff => buff.IsExpired);
        foreach (var buff in expired)
        {
            buff.Detach();
        }

        if (expired.Count > 0)
        {
            OnBuffsChanged();
        }
        else
        {
            ClampResourcesToLimits();
        }

        return expired;
    }

    internal AffixProjection GetLocalBattleProjection() => _localProjection ??= AffixProjectionBuilder.Build(
        ActiveBuffs.SelectMany(buff =>
            buff.Definition.Affixes.Select((affix, order) => new ActiveAffixEntry(
                affix,
                new BuffAffixOrigin(buff.Definition.Id, buff.AppliedAtActionSerial),
                Provider: null,
                SourceLevel: buff.Level,
                AffixOrder: order,
                SourceSequence: buff.AppliedAtActionSerial))));

    /// <summary>buff 集合或某个 buff 的等级/剩余回合变化后的唯一刷新入口。</summary>
    private void OnBuffsChanged()
    {
        _activeBuffs = null;
        _activeBuffsById = null;
        _localProjection = null;
        ClampResourcesToLimits();
    }

    private BattleEffectProjectionResolver Resolver => _projectionResolver ??
        throw new InvalidOperationException($"Battle unit '{Id}' is not attached to a battle state.");

    private AffixProjection DetachedProjection =>
        AffixProjectionCombiner.Combine(Character.Projection.Affixes, GetLocalBattleProjection());

    private static ModifierBucket GetBucket<TKey>(IReadOnlyDictionary<TKey, ModifierBucket> buckets, TKey key)
        where TKey : notnull => buckets.TryGetValue(key, out var bucket) ? bucket : ModifierBucket.Empty;

    private int ResolveMaxHp() =>
        ResolvePositiveStat(StatType.MaxHp, 1);

    private int ResolveMaxMp() =>
        ResolvePositiveStat(StatType.MaxMp, 0);

    private int ResolvePositiveStat(StatType statType, int fallback)
    {
        var value = (int)Math.Round(GetStat(statType));
        return value > 0 ? value : fallback;
    }
}
