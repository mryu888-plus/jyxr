using System.Text.Json.Serialization;
using Game.Core.Affix;
using Game.Core.Model;

namespace Game.Core.Battle;

/// <summary>效果造成的强制位置变化种类。玩家/AI 主动移动仍是 <see cref="BattleFactKind.Moved"/>。</summary>
public enum BattleRelocationKind
{
    /// <summary>沿“来源→目标”方向逐格推离来源。</summary>
    [JsonStringEnumMemberName("push")]
    Push,
    /// <summary>沿“目标→来源”方向逐格拉近，最多贴到来源身边。</summary>
    [JsonStringEnumMemberName("pull")]
    Pull,
    /// <summary>来源与目标互换位置。</summary>
    [JsonStringEnumMemberName("swap")]
    Swap,
    /// <summary>直接放到指定格（只供定制效果计算落点使用，数据效果不能使用）。</summary>
    [JsonStringEnumMemberName("teleport")]
    Teleport,
}

public enum BattleRelocationBlockReason
{
    None,
    /// <summary>单位带有不可移动特性，免疫强制位移。</summary>
    Immovable,
    /// <summary>落点越界、不可通行或被占用；推拉在第一个不可用格前停下。</summary>
    Blocked,
    /// <summary>目标已倒下或与来源相同等无效情况。</summary>
    InvalidTarget,
}

public sealed record BattleRelocationResult(
    string UnitId,
    GridPosition From,
    GridPosition To,
    BattleRelocationBlockReason BlockReason)
{
    public bool Moved => From != To;

    public int Cells => Math.Abs(To.X - From.X) + Math.Abs(To.Y - From.Y);
}

/// <summary>位置变化事实的载荷：画面据此播放可等待的位移演出后再提交最终位置。</summary>
public sealed record BattleRelocationEvent(
    GridPosition From,
    GridPosition To,
    BattleRelocationKind Kind,
    string? SourceUnitId);

/// <summary>
/// 强制位置变化的统一规则：
/// <list type="bullet">
/// <item>落点必须在棋盘内、可通行且没有其他存活单位；推拉逐格前进，遇到第一个不可用格即停，交换与传送要求落点可用。</item>
/// <item>带 <see cref="TraitId.CannotMove"/> 的单位免疫强制位移；强制位移不受控制区影响，也不消耗行动力。</item>
/// <item>每个实际移动的单位产生一条 <see cref="BattleFactKind.Relocated"/> 事实；不新增 hook 时机，同一效果后续步骤直接看到新位置。</item>
/// </list>
/// </summary>
internal sealed class BattleRelocationResolver
{
    public BattleRelocationResult Push(BattleState state, BattleUnit source, BattleUnit target, int distance) =>
        Slide(state, source, target, Direction(source.Position, target.Position), distance, BattleRelocationKind.Push);

    public BattleRelocationResult Pull(BattleState state, BattleUnit source, BattleUnit target, int distance)
    {
        var gap = Math.Max(0, source.Position.ManhattanDistanceTo(target.Position) - 1);
        return Slide(state, source, target, Direction(target.Position, source.Position), Math.Min(distance, gap),
            BattleRelocationKind.Pull);
    }

    public BattleRelocationResult Teleport(BattleState state, BattleUnit? source, BattleUnit target, GridPosition destination)
    {
        if (Reject(target) is { } reason)
        {
            return new BattleRelocationResult(target.Id, target.Position, target.Position, reason);
        }

        if (destination == target.Position)
        {
            return new BattleRelocationResult(target.Id, target.Position, target.Position, BattleRelocationBlockReason.None);
        }

        if (!IsCellAvailable(state, destination, target))
        {
            return new BattleRelocationResult(target.Id, target.Position, target.Position, BattleRelocationBlockReason.Blocked);
        }

        return Commit(state, source, target, destination, BattleRelocationKind.Teleport);
    }

    public (BattleRelocationResult First, BattleRelocationResult Second) Swap(BattleState state, BattleUnit first, BattleUnit second)
    {
        var reason = ReferenceEquals(first, second)
            ? BattleRelocationBlockReason.InvalidTarget
            : Reject(first) ?? Reject(second);
        if (reason is not null)
        {
            return (
                new BattleRelocationResult(first.Id, first.Position, first.Position, reason.Value),
                new BattleRelocationResult(second.Id, second.Position, second.Position, reason.Value));
        }

        var firstFrom = first.Position;
        var secondFrom = second.Position;
        first.Position = secondFrom;
        second.Position = firstFrom;
        AddFact(state, first, firstFrom, secondFrom, BattleRelocationKind.Swap, second);
        AddFact(state, second, secondFrom, firstFrom, BattleRelocationKind.Swap, first);
        return (
            new BattleRelocationResult(first.Id, firstFrom, secondFrom, BattleRelocationBlockReason.None),
            new BattleRelocationResult(second.Id, secondFrom, firstFrom, BattleRelocationBlockReason.None));
    }

    public static bool IsCellAvailable(BattleState state, GridPosition position, BattleUnit movingUnit) =>
        state.Grid.IsWalkable(position) && !state.IsOccupied(position, movingUnit.Id);

    private BattleRelocationResult Slide(
        BattleState state,
        BattleUnit source,
        BattleUnit target,
        (int X, int Y) direction,
        int distance,
        BattleRelocationKind kind)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(distance);
        if (ReferenceEquals(source, target) || direction == (0, 0))
        {
            return new BattleRelocationResult(target.Id, target.Position, target.Position, BattleRelocationBlockReason.InvalidTarget);
        }

        if (Reject(target) is { } reason)
        {
            return new BattleRelocationResult(target.Id, target.Position, target.Position, reason);
        }

        var destination = target.Position;
        var blocked = false;
        for (var step = 0; step < distance; step++)
        {
            var next = new GridPosition(destination.X + direction.X, destination.Y + direction.Y);
            if (!IsCellAvailable(state, next, target))
            {
                blocked = true;
                break;
            }

            destination = next;
        }

        var blockReason = blocked ? BattleRelocationBlockReason.Blocked : BattleRelocationBlockReason.None;
        return destination == target.Position
            ? new BattleRelocationResult(target.Id, target.Position, target.Position, blockReason)
            : Commit(state, source, target, destination, kind) with { BlockReason = blockReason };
    }

    private BattleRelocationResult Commit(
        BattleState state,
        BattleUnit? source,
        BattleUnit target,
        GridPosition destination,
        BattleRelocationKind kind)
    {
        var from = target.Position;
        target.Position = destination;
        AddFact(state, target, from, destination, kind, source);
        return new BattleRelocationResult(target.Id, from, destination, BattleRelocationBlockReason.None);
    }

    private static void AddFact(
        BattleState state,
        BattleUnit unit,
        GridPosition from,
        GridPosition to,
        BattleRelocationKind kind,
        BattleUnit? source) =>
        BattleEngine.AddMessage(state, new BattleFact(
            BattleFactKind.Relocated,
            unit.Id,
            detail: $"{to.X},{to.Y}",
            relocation: new BattleRelocationEvent(from, to, kind, source?.Id)));

    private static BattleRelocationBlockReason? Reject(BattleUnit unit)
    {
        if (!unit.IsAlive)
        {
            return BattleRelocationBlockReason.InvalidTarget;
        }

        return unit.HasTrait(TraitId.CannotMove) ? BattleRelocationBlockReason.Immovable : null;
    }

    private static (int X, int Y) Direction(GridPosition from, GridPosition to) =>
        (Math.Sign(to.X - from.X), Math.Sign(to.Y - from.Y));
}
