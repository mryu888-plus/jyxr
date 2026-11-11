using Game.Core.Definitions;

namespace Game.Core.Battle;

public sealed class BattleBuffInstance
{
    public const int TimelineTicksPerRound = 50;

    public BattleBuffInstance(
        BuffDefinition definition,
        int level,
        int remainingTurns,
        string sourceUnitId,
        long appliedAtActionSerial)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        ArgumentOutOfRangeException.ThrowIfLessThan(remainingTurns, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceUnitId);

        Definition = definition;
        Level = level;
        RemainingTurns = remainingTurns;
        SourceUnitId = sourceUnitId;
        AppliedAtActionSerial = appliedAtActionSerial;
        TicksUntilRound = TimelineTicksPerRound;
    }

    public BuffDefinition Definition { get; }

    public int Level { get; private set; }

    public int RemainingTurns { get; private set; }

    public string SourceUnitId { get; }

    public long AppliedAtActionSerial { get; }

    public int TicksUntilRound { get; private set; }

    public bool IsExpired => RemainingTurns <= 0;

    /// <summary>挂在单位身上后由单位订阅：等级或剩余回合变化时通知单位刷新 active buff 快照与投影。</summary>
    private Action? _changed;

    internal void AttachTo(Action changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        _changed = changed;
    }

    internal void Detach() => _changed = null;

    public void Strengthen(int levelDelta, int turnDelta)
    {
        if (levelDelta < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(levelDelta));
        }

        if (turnDelta < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(turnDelta));
        }

        Level = checked(Level + levelDelta);
        RemainingTurns = checked(RemainingTurns + turnDelta);
        _changed?.Invoke();
    }

    internal void Reduce(int levelReduction, int turnReduction)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(levelReduction);
        ArgumentOutOfRangeException.ThrowIfNegative(turnReduction);

        Level = Math.Max(0, Level - levelReduction);
        RemainingTurns = Math.Max(0, RemainingTurns - turnReduction);
        _changed?.Invoke();
    }

    internal bool AdvanceTimeline()
    {
        TicksUntilRound--;
        if (TicksUntilRound > 0)
        {
            return false;
        }

        TicksUntilRound = TimelineTicksPerRound;
        return true;
    }

    internal void ConsumeRound()
    {
        RemainingTurns--;
        _changed?.Invoke();
    }
}
