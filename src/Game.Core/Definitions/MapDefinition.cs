using System.Text.Json.Serialization;
using Game.Core.Model;

namespace Game.Core.Definitions;

public sealed record MapDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public MapKind Kind { get; init; } = MapKind.Small;

    public double TravelSpeed { get; init; }

    public string? DefaultLocation { get; init; }

    public string? Description { get; init; }

    public string? Picture { get; init; }

    public IReadOnlyList<string> Musics { get; init; } = [];

    public IReadOnlyList<MapLocationDefinition> Locations { get; init; } = [];
}

public sealed record MapLocationDefinition
{
    public required string Id { get; init; }

    public string? Name { get; init; }

    public MapPosition? Position { get; init; }

    public string? Description { get; init; }

    public string? Picture { get; init; }

    public bool HideWhenNoEvent { get; init; }

    public string? NoEventImage { get; init; }

    /// <summary>地点级通行条件：为假时地点不出现、不可前往（大地图也不能移动过去），事件不参与判定。</summary>
    [JsonConverter(typeof(Game.Core.Serialization.ParsedExpressionJsonConverter))]
    public ParsedExpression? When { get; init; }

    /// <summary>交互对象种类，只影响宿主表现与提示；行为仍由事件表达。</summary>
    public MapInteractableKind? Interactable { get; init; }

    public IReadOnlyList<MapEventDefinition> Events { get; init; } = [];
}

/// <summary>地图交互对象：宝箱、门、机关、调查点、采集点、触发器等，都是“带事件的地点”。</summary>
public enum MapInteractableKind
{
    [JsonStringEnumMemberName("npc")]
    Npc,
    [JsonStringEnumMemberName("chest")]
    Chest,
    [JsonStringEnumMemberName("door")]
    Door,
    [JsonStringEnumMemberName("mechanism")]
    Mechanism,
    [JsonStringEnumMemberName("investigate")]
    Investigate,
    [JsonStringEnumMemberName("gather")]
    Gather,
    [JsonStringEnumMemberName("trigger")]
    Trigger,
}

/// <summary>交互对象当前状态，由地点条件、事件条件和一次性事件进度推导，不单独存档。</summary>
public enum MapInteractableState
{
    /// <summary>有可触发的事件。</summary>
    Available,
    /// <summary>所有事件都是一次性且已完成（如已打开的宝箱）。</summary>
    Exhausted,
    /// <summary>地点不可通行，或暂时没有满足条件的事件。</summary>
    Inactive,
}

public sealed record MapEventDefinition
{
    public required string Id { get; init; }

    [JsonConverter(typeof(Game.Core.Serialization.ParsedCallJsonConverter))]
    public required ParsedCall Action { get; init; }

    [JsonConverter(typeof(Game.Core.Serialization.ParsedExpressionJsonConverter))]
    public ParsedExpression? When { get; init; }

    public RepeatMode RepeatMode { get; init; } = RepeatMode.Infinite;

    public string? Image { get; init; }

    public string? Description { get; init; }

}

public enum RepeatMode
{
    [JsonStringEnumMemberName("once")]
    Once,
    [JsonStringEnumMemberName("infinite")]
    Infinite
}

public enum MapKind
{
    [JsonStringEnumMemberName("small")]
    Small,
    [JsonStringEnumMemberName("large")]
    Large,
}
