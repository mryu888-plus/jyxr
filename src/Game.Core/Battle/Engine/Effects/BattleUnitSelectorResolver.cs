using Game.Core.Model;

namespace Game.Core.Battle;

internal static class BattleUnitSelectorResolver
{
    public static IReadOnlyList<BattleUnit> Resolve(
        BattleState state,
        BattleUnit contextUnit,
        BattleUnit source,
        IReadOnlyList<BattleUnit> primaryTargets,
        BattleUnitSelectorDefinition selector) =>
        Resolve(state, contextUnit, contextUnit.Position, source, primaryTargets, selector);

    /// <summary>
    /// 按指定锚点位置解析选择器。执行时锚点就是上下文单位的当前位置；
    /// 技能预览（AI 评估移动后施放）用移动目的地作为锚点。
    /// </summary>
    public static IReadOnlyList<BattleUnit> Resolve(
        BattleState state,
        BattleUnit contextUnit,
        GridPosition anchorPosition,
        BattleUnit source,
        IReadOnlyList<BattleUnit> primaryTargets,
        BattleUnitSelectorDefinition selector) =>
        selector switch
        {
            SelfBattleUnitSelectorDefinition => [contextUnit],
            SourceBattleUnitSelectorDefinition => [source],
            TargetBattleUnitSelectorDefinition => primaryTargets,
            AllUnitsBattleUnitSelectorDefinition => state.GetLivingUnits(),
            AllAlliesBattleUnitSelectorDefinition allAllies => state.GetLivingUnits()
                .Where(unit => unit.Team == contextUnit.Team)
                .Where(unit => allAllies.IncludeSelf || !string.Equals(unit.Id, contextUnit.Id, StringComparison.Ordinal))
                .ToList(),
            AllEnemiesBattleUnitSelectorDefinition => state.GetLivingUnits()
                .Where(unit => unit.Team != contextUnit.Team)
                .ToList(),
            NearbyAlliesBattleUnitSelectorDefinition nearbyAllies => state.GetLivingUnits()
                .Where(unit => unit.Team == contextUnit.Team)
                .Where(unit => nearbyAllies.IncludeSelf || !string.Equals(unit.Id, contextUnit.Id, StringComparison.Ordinal))
                .Where(unit => ResolvePosition(unit, contextUnit, anchorPosition).ManhattanDistanceTo(anchorPosition) <= nearbyAllies.Radius)
                .ToList(),
            NearbyEnemiesBattleUnitSelectorDefinition nearbyEnemies => state.GetLivingUnits()
                .Where(unit => unit.Team != contextUnit.Team)
                .Where(unit => ResolvePosition(unit, contextUnit, anchorPosition).ManhattanDistanceTo(anchorPosition) <= nearbyEnemies.Radius)
                .ToList(),
            ExplicitUnitsBattleUnitSelectorDefinition => throw new InvalidOperationException(
                "Explicit unit selectors require runtime member ids."),
            _ => throw new NotSupportedException($"Unsupported battle target selector '{selector.GetType().Name}'."),
        };

    private static GridPosition ResolvePosition(BattleUnit unit, BattleUnit contextUnit, GridPosition anchorPosition) =>
        ReferenceEquals(unit, contextUnit) ? anchorPosition : unit.Position;

    public static IReadOnlyList<BattleUnit> ResolveScope(
        BattleState state,
        BattleUnit? anchor,
        int team,
        BattleUnitSelectorDefinition selector,
        IReadOnlySet<string> explicitUnitIds)
    {
        return selector switch
        {
            AllUnitsBattleUnitSelectorDefinition => state.GetLivingUnits(),
            AllAlliesBattleUnitSelectorDefinition allAllies => state.GetLivingUnits()
                .Where(unit => unit.Team == team)
                .Where(unit => allAllies.IncludeSelf || anchor is null || unit.Id != anchor.Id)
                .ToList(),
            AllEnemiesBattleUnitSelectorDefinition => state.GetLivingUnits().Where(unit => unit.Team != team).ToList(),
            NearbyAlliesBattleUnitSelectorDefinition nearby when anchor is not null => state.GetLivingUnits()
                .Where(unit => unit.Team == team)
                .Where(unit => nearby.IncludeSelf || unit.Id != anchor.Id)
                .Where(unit => unit.Position.ManhattanDistanceTo(anchor.Position) <= nearby.Radius)
                .ToList(),
            NearbyEnemiesBattleUnitSelectorDefinition nearby when anchor is not null => state.GetLivingUnits()
                .Where(unit => unit.Team != team)
                .Where(unit => unit.Position.ManhattanDistanceTo(anchor.Position) <= nearby.Radius)
                .ToList(),
            ExplicitUnitsBattleUnitSelectorDefinition => state.Units.Where(unit => explicitUnitIds.Contains(unit.Id)).ToList(),
            SelfBattleUnitSelectorDefinition or SourceBattleUnitSelectorDefinition or TargetBattleUnitSelectorDefinition =>
                throw new InvalidOperationException($"Selector '{selector.GetType().Name}' is not stable for a scoped effect."),
            _ => throw new InvalidOperationException($"Selector '{selector.GetType().Name}' requires a living anchor."),
        };
    }
}
