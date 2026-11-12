using Game.Core.Model;

namespace Game.Core.Persistence;

public sealed record GameProfileRecord(
    int Version,
    IReadOnlyList<string> UnlockedAchievementIds,
    int DeathCount,
    int KillCount,
    int ZhenlongqijuLevel = 0,
    int Yuanbao = 0,
    IReadOnlyDictionary<string, int>? SkillMaxLevelBonuses = null,
    IReadOnlyList<string>? ConsumedSkillMaxLevelKeys = null,
    int CompletionCount = 0,
    int HighestRound = 0,
    int SaveCount = 0,
    long TotalPlayTimeSeconds = 0)
{
    public const int CurrentVersion = 6;

    public static GameProfileRecord Create(GameProfile profile)
    {
        return new GameProfileRecord(
            CurrentVersion,
            profile.UnlockedAchievementIds.OrderBy(static id => id, StringComparer.Ordinal).ToList(),
            profile.DeathCount,
            profile.KillCount,
            profile.ZhenlongqijuLevel,
            profile.Yuanbao,
            profile.SkillMaxLevelBonuses
                .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
                .ToDictionary(static entry => entry.Key, static entry => entry.Value, StringComparer.Ordinal),
            profile.ConsumedSkillMaxLevelKeys
                .OrderBy(static key => key, StringComparer.Ordinal)
                .ToList(),
            profile.CompletionCount,
            profile.HighestRound,
            profile.SaveCount,
            profile.TotalPlayTimeSeconds);
    }

    public GameProfile Restore()
    {
        var profile = new GameProfile();
        profile.SetUnlockedAchievementIds(UnlockedAchievementIds);
        profile.SetLifetimeStats(DeathCount, KillCount, SaveCount);
        profile.SetCompletionStats(CompletionCount, HighestRound);
        profile.SetZhenlongqijuLevel(ZhenlongqijuLevel);
        profile.SetYuanbao(Yuanbao);
        profile.SetTotalPlayTimeSeconds(TotalPlayTimeSeconds);
        profile.SetSkillMaxLevelBonuses(SkillMaxLevelBonuses ?? new Dictionary<string, int>(StringComparer.Ordinal));
        profile.SetConsumedSkillMaxLevelKeys(ConsumedSkillMaxLevelKeys ?? []);
        return profile;
    }
}
