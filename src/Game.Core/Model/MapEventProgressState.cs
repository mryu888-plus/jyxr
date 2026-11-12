using Game.Core.Persistence;

namespace Game.Core.Model;

public sealed class MapEventProgressState
{
    private readonly HashSet<MapEventKey> _completedEvents = [];

    public IReadOnlyCollection<MapEventKey> CompletedEvents => _completedEvents;

    public static MapEventProgressState Restore(MapEventProgressRecord record)
    {
        var state = new MapEventProgressState();
        foreach (var completedEvent in record.CompletedEvents ?? [])
        {
            state._completedEvents.Add(CreateKey(
                completedEvent.MapId,
                completedEvent.LocationId,
                completedEvent.EventId));
        }

        return state;
    }

    public bool IsCompleted(string mapId, string locationId, string eventId)
    {
        return _completedEvents.Contains(CreateKey(mapId, locationId, eventId));
    }

    public void MarkCompleted(string mapId, string locationId, string eventId)
    {
        _completedEvents.Add(CreateKey(mapId, locationId, eventId));
    }

    public MapEventProgressRecord ToRecord() =>
        new(_completedEvents
            .OrderBy(static key => key.MapId, StringComparer.Ordinal)
            .ThenBy(static key => key.LocationId, StringComparer.Ordinal)
            .ThenBy(static key => key.EventId, StringComparer.Ordinal)
            .Select(static key => new MapEventCompletionRecord(
                key.MapId,
                key.LocationId,
                key.EventId))
            .ToArray());

    private static MapEventKey CreateKey(string mapId, string locationId, string eventId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapId);
        ArgumentException.ThrowIfNullOrWhiteSpace(locationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        return new MapEventKey(mapId, locationId, eventId);
    }
}

public readonly record struct MapEventKey(string MapId, string LocationId, string EventId);
