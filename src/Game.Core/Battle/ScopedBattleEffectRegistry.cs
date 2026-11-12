using Game.Core.Affix;
using Game.Core.Definitions;

namespace Game.Core.Battle;

public sealed class ScopedBattleEffectInstance
{
    internal ScopedBattleEffectInstance(
        ScopedBattleEffectDefinition definition,
        BattleUnit? provider,
        int team,
        long sequence)
    {
        Definition = definition;
        Provider = provider;
        Team = team;
        Sequence = sequence;
    }

    public ScopedBattleEffectDefinition Definition { get; }
    public BattleUnit? Provider { get; }
    public int Team { get; }
    public long Sequence { get; }
    public IReadOnlySet<string> Members => _members;
    public bool IsEstablished { get; internal set; }
    internal HashSet<string> MutableMembers => _members;
    private readonly HashSet<string> _members = new(StringComparer.Ordinal);
}

public sealed class ScopedBattleEffectRegistry
{
    private readonly List<ScopedBattleEffectInstance> _instances = [];
    private readonly HashSet<string> _closedGroupKeys = new(StringComparer.Ordinal);
    private long _nextSequence;

    public IReadOnlyList<ScopedBattleEffectInstance> Instances => _instances;

    public bool Grant(BattleState state, BattleUnit provider, ScopedBattleEffectDefinition definition, HookTiming? timing)
    {
        return definition.GrantMode switch
        {
            ScopedBattleEffectGrantMode.PerProvider => GrantPerProvider(state, provider, definition, timing),
            ScopedBattleEffectGrantMode.PerTeamGroup => GrantGroup(state, provider, definition, timing),
            _ => throw new ArgumentOutOfRangeException(nameof(definition.GrantMode)),
        };
    }

    public void NotifyUnitDefeated(BattleState state, BattleUnit defeated)
    {
        var removed = _instances.Where(instance =>
            instance.IsEstablished &&
            instance.Definition.Lifetime == BattleEffectLifetime.RemoveWhenMemberDefeated &&
            instance.Members.Contains(defeated.Id)).ToList();
        foreach (var instance in removed)
        {
            _instances.Remove(instance);
            if (instance.Definition.GrantMode == ScopedBattleEffectGrantMode.PerTeamGroup)
                _closedGroupKeys.Add(GroupKey(instance.Definition.Id, instance.Team));
            state.AddMessage(new BattleFact(
                BattleFactKind.ScopedEffectRemoved,
                defeated.Id,
                HookTiming.OnDefeated,
                detail: instance.Definition.Id));
        }
    }

    internal AffixProjection ResolveProjection(BattleState state, BattleUnit unit)
    {
        var entries = new List<ActiveAffixEntry>();
        foreach (var instance in _instances.Where(static value => value.IsEstablished))
        {
            if (instance.Definition.Activation == BattleEffectActivation.SourceAlive &&
                instance.Provider?.IsAlive != true) continue;
            var covered = BattleUnitSelectorResolver.ResolveScope(
                state,
                instance.Provider,
                instance.Team,
                instance.Definition.Scope,
                instance.Members);
            if (!covered.Contains(unit)) continue;
            entries.AddRange(instance.Definition.Affixes.Select((affix, order) => new ActiveAffixEntry(
                affix,
                new ScopedEffectAffixOrigin(instance.Definition.Id, instance.Sequence),
                instance.Provider,
                SourceLevel: 1,
                AffixOrder: order,
                SourceSequence: instance.Sequence)));
        }
        return AffixProjectionBuilder.Build(entries);
    }

    private bool GrantPerProvider(
        BattleState state,
        BattleUnit provider,
        ScopedBattleEffectDefinition definition,
        HookTiming? timing)
    {
        if (_instances.Any(instance => instance.Definition.Id == definition.Id && ReferenceEquals(instance.Provider, provider)))
            return false;
        var created = new ScopedBattleEffectInstance(definition, provider, provider.Team, ++_nextSequence)
        {
            IsEstablished = true,
        };
        _instances.Add(created);
        state.AddMessage(new BattleFact(BattleFactKind.ScopedEffectGranted, provider.Id, timing, definition.Id));
        return true;
    }

    private bool GrantGroup(
        BattleState state,
        BattleUnit provider,
        ScopedBattleEffectDefinition definition,
        HookTiming? timing)
    {
        var key = GroupKey(definition.Id, provider.Team);
        if (_closedGroupKeys.Contains(key)) return false;
        var instance = _instances.FirstOrDefault(value =>
            value.Definition.Id == definition.Id && value.Team == provider.Team);
        if (instance is null)
        {
            instance = new ScopedBattleEffectInstance(definition, null, provider.Team, ++_nextSequence);
            _instances.Add(instance);
        }
        if (instance.IsEstablished || !instance.MutableMembers.Add(provider.Id)) return false;
        if (instance.MutableMembers.Count < definition.RequiredMembers) return false;
        instance.IsEstablished = true;
        state.AddMessage(new BattleFact(BattleFactKind.ScopedEffectGranted, provider.Id, timing, definition.Id));
        return true;
    }

    private static string GroupKey(string effectId, int team) => $"{effectId}:{team}";
}
