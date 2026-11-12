namespace Game.Core.Model;

public sealed class GameProfile
{
    private readonly HashSet<string> _unlockedAchievementIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _skillMaxLevelBonuses = new(StringComparer.Ordinal);
    private readonly HashSet<string> _consumedSkillMaxLevelKeys = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> UnlockedAchievementIds => _unlockedAchievementIds;
    public IReadOnlyDictionary<string, int> SkillMaxLevelBonuses => _skillMaxLevelBonuses;
    public IReadOnlyCollection<string> ConsumedSkillMaxLevelKeys => _consumedSkillMaxLevelKeys;

    public int DeathCount { get; private set; }

    public int KillCount { get; private set; }

    public int SaveCount { get; private set; }

    public int CompletionCount { get; private set; }

    public int HighestRound { get; private set; }

    public int ZhenlongqijuLevel { get; private set; }

    public int Yuanbao { get; private set; }

    public long TotalPlayTimeSeconds { get; private set; }

    public bool IsAchievementUnlocked(string achievementId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(achievementId);
        return _unlockedAchievementIds.Contains(achievementId);
    }

    public bool UnlockAchievement(string achievementId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(achievementId);
        return _unlockedAchievementIds.Add(achievementId);
    }

    public int GetSkillMaxLevelBonus(string skillId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skillId);
        return _skillMaxLevelBonuses.GetValueOrDefault(skillId);
    }

    public void AddSkillMaxLevelBonus(string skillId, int levels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skillId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(levels);
        _skillMaxLevelBonuses[skillId] = checked(GetSkillMaxLevelBonus(skillId) + levels);
    }

    public bool TryAddSkillMaxLevelBonusOnce(string skillId, int levels, string? onceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skillId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(levels);
        if (string.IsNullOrWhiteSpace(onceKey))
        {
            AddSkillMaxLevelBonus(skillId, levels);
            return true;
        }

        if (_consumedSkillMaxLevelKeys.Contains(onceKey))
        {
            return false;
        }

        AddSkillMaxLevelBonus(skillId, levels);
        _consumedSkillMaxLevelKeys.Add(onceKey);
        return true;
    }

    public void AddDeaths(int count = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        DeathCount = checked(DeathCount + count);
    }

    public void AddKills(int count = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        KillCount = checked(KillCount + count);
    }

    public void AddSaves(int count = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        SaveCount = checked(SaveCount + count);
    }

    public void RecordCompletion(int completedRound)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(completedRound, 1);
        CompletionCount = checked(CompletionCount + 1);
        RecordRoundReached(completedRound);
    }

    public bool RecordRoundReached(int round)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(round, 1);
        if (round <= HighestRound)
        {
            return false;
        }

        HighestRound = round;
        return true;
    }

    public void SetZhenlongqijuLevel(int level)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        ZhenlongqijuLevel = level;
    }

    public void AdvanceZhenlongqijuLevel() => ZhenlongqijuLevel++;

    public void ChangeYuanbao(int delta)
    {
        if (delta >= 0)
        {
            AddYuanbao(delta);
            return;
        }

        ArgumentOutOfRangeException.ThrowIfEqual(delta, int.MinValue);
        SpendYuanbao(-delta);
    }

    public void AddYuanbao(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        Yuanbao = checked(Yuanbao + amount);
    }

    public void SpendYuanbao(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        if (!CanSpendYuanbao(amount))
        {
            throw new InvalidOperationException("Not enough yuanbao.");
        }

        Yuanbao -= amount;
    }

    public bool CanSpendYuanbao(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        return Yuanbao >= amount;
    }

    public void SetYuanbao(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        Yuanbao = amount;
    }

    public void SetTotalPlayTimeSeconds(long seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);
        TotalPlayTimeSeconds = seconds;
    }

    public void AddPlayTimeSeconds(long seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);
        TotalPlayTimeSeconds = checked(TotalPlayTimeSeconds + seconds);
    }

    public void SetUnlockedAchievementIds(IEnumerable<string> achievementIds)
    {
        _unlockedAchievementIds.Clear();
        foreach (var achievementId in achievementIds)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(achievementId);
            _unlockedAchievementIds.Add(achievementId);
        }
    }

    public void SetSkillMaxLevelBonuses(IReadOnlyDictionary<string, int> bonuses)
    {
        _skillMaxLevelBonuses.Clear();
        foreach (var (skillId, bonus) in bonuses)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(skillId);
            ArgumentOutOfRangeException.ThrowIfNegative(bonus);
            if (bonus > 0)
            {
                _skillMaxLevelBonuses[skillId] = bonus;
            }
        }
    }

    public void SetConsumedSkillMaxLevelKeys(IEnumerable<string> keys)
    {
        _consumedSkillMaxLevelKeys.Clear();
        foreach (var key in keys)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            _consumedSkillMaxLevelKeys.Add(key);
        }
    }

    public void SetLifetimeStats(int deathCount, int killCount, int saveCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(deathCount);
        ArgumentOutOfRangeException.ThrowIfNegative(killCount);
        ArgumentOutOfRangeException.ThrowIfNegative(saveCount);

        DeathCount = deathCount;
        KillCount = killCount;
        SaveCount = saveCount;
    }

    public void SetCompletionStats(int completionCount, int highestRound)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(completionCount);
        ArgumentOutOfRangeException.ThrowIfNegative(highestRound);

        CompletionCount = completionCount;
        HighestRound = highestRound;
    }
}
