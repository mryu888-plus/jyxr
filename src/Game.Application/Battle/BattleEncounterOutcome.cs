namespace Game.Application;

/// <summary>
/// 遭遇战结果回写世界状态的方式（BattleResultProjection）。
/// 地图遭遇战沿用地点事件模型：入口是带 <c>battle(...)</c> action 的地图事件，触发条件是事件的
/// <c>when</c>（原版概率遇敌即 <c>chance(p)</c>）与 <c>repeatMode</c>；战斗结束后按本规则回写：
/// 胜利时若配置了胜利剧情就执行它；失败时若配置了失败剧情就执行它，否则与原版一致判定游戏结束。
/// </summary>
public sealed record BattleResultProjection(string? WinStoryId = null, string? LoseStoryId = null)
{
    public static BattleResultProjection Default { get; } = new();

    public static BattleResultProjection FromCommandArguments(string? winStoryId, string? loseStoryId) =>
        new(Normalize(winStoryId), Normalize(loseStoryId));

    public BattleEncounterFollowUp Resolve(bool isWin) =>
        isWin
            ? WinStoryId is null ? BattleEncounterFollowUp.None : BattleEncounterFollowUp.RunStory(WinStoryId)
            : LoseStoryId is null ? BattleEncounterFollowUp.GameOver : BattleEncounterFollowUp.RunStory(LoseStoryId);

    private static string? Normalize(string? storyId) =>
        string.IsNullOrWhiteSpace(storyId) ? null : storyId.Trim();
}

public enum BattleEncounterFollowUpKind
{
    None,
    RunStory,
    GameOver,
}

public sealed record BattleEncounterFollowUp(BattleEncounterFollowUpKind Kind, string? StoryId = null)
{
    public static BattleEncounterFollowUp None { get; } = new(BattleEncounterFollowUpKind.None);

    public static BattleEncounterFollowUp GameOver { get; } = new(BattleEncounterFollowUpKind.GameOver);

    public static BattleEncounterFollowUp RunStory(string storyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storyId);
        return new(BattleEncounterFollowUpKind.RunStory, storyId);
    }
}
