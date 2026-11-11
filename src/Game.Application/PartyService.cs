using Game.Core.Model;
using Game.Core.Model.Character;

namespace Game.Application;

public sealed class PartyService
{
    private readonly GameSession _session;
    private readonly InitialCharacterFactory _initialCharacterFactory;

    public PartyService(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _initialCharacterFactory = new InitialCharacterFactory(
            session.ContentRepository,
            session.Config,
            session.SkillMaxLevelPolicy);
    }

    private GameState State => _session.State;

    public void MoveMember(string characterId, int targetIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characterId);

        if (!State.Party.MoveMember(characterId, targetIndex))
        {
            return;
        }

        _session.Events.Publish(new PartyChangedEvent());
    }

    public void Join(string characterId, string? definitionId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characterId);
        var resolvedDefinitionId = definitionId ?? characterId;
        ArgumentException.ThrowIfNullOrWhiteSpace(resolvedDefinitionId);
        _session.ContentRepository.GetCharacter(resolvedDefinitionId);

        if (State.Party.ContainsMember(characterId))
        {
            return;
        }

        if (State.Party.MoveToMembers(characterId))
        {
            _session.Events.Publish(new PartyChangedEvent());
            return;
        }

        State.Party.AddMember(CreateInitialCharacter(characterId, resolvedDefinitionId));
        _session.Events.Publish(new PartyChangedEvent());
    }

    /// <summary>
    /// 角色转化/重建: 用另一份 CharacterDefinition 重建名册里已有的角色实例 (如 程英.初级 -> 程英.高级)。
    /// 实例 id 和名册位置不变; 新模板提供基础数值、自带武学与天赋, 已有的成长保留:
    /// 等级/经验取较高者, 基础属性逐项取较高者, 未分配属性点、已学武学 (等级取较高者)、天赋、特技、
    /// 装备中的内功和身上的装备都带过去; 玩家改过的名字和头像保留。
    /// </summary>
    public void Transform(string characterId, string definitionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        var definition = _session.ContentRepository.GetCharacter(definitionId);
        if (!State.Party.TryGetCharacter(characterId, out var previous))
        {
            throw new InvalidOperationException($"Character '{characterId}' is not in the party roster and cannot be transformed.");
        }

        if (string.Equals(previous.Definition.Id, definition.Id, StringComparison.Ordinal))
        {
            return;
        }

        var rebuilt = CreateInitialCharacter(characterId, definitionId);
        CharacterTransformation.CarryOverGrowth(previous, rebuilt);
        State.Party.Replace(rebuilt);
        _session.Events.Publish(new PartyChangedEvent());
        _session.Events.Publish(new CharacterChangedEvent(characterId));
    }

    public void Follow(string characterId, string? definitionId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characterId);
        var resolvedDefinitionId = definitionId ?? characterId;
        ArgumentException.ThrowIfNullOrWhiteSpace(resolvedDefinitionId);
        _session.ContentRepository.GetCharacter(resolvedDefinitionId);

        if (State.Party.ContainsFollower(characterId))
        {
            return;
        }

        if (State.Party.MoveToFollowers(characterId))
        {
            _session.Events.Publish(new PartyChangedEvent());
            return;
        }

        State.Party.AddFollower(CreateInitialCharacter(characterId, resolvedDefinitionId));
    }

    public void Leave(string characterId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characterId);

        if (!State.Party.TryGetMember(characterId, out var character))
        {
            return;
        }

        MoveToReserves(character);
        _session.Events.Publish(new PartyChangedEvent());
    }

    public void LeaveFollow(string characterId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characterId);

        if (!State.Party.TryGetFollower(characterId, out var character))
        {
            return;
        }

        MoveToReserves(character);
        _session.Events.Publish(new PartyChangedEvent());
    }

    public void LeaveAll()
    {
        var departingMembers = State.Party.Members
            .Where(member => !string.Equals(member.Id, Party.HeroCharacterId, StringComparison.Ordinal))
            .ToArray();
        if (departingMembers.Length == 0)
        {
            return;
        }

        foreach (var member in departingMembers)
        {
            MoveToReserves(member);
        }

        _session.Events.Publish(new PartyChangedEvent());
    }

    public CharacterInstance RenameOrCreateReserve(string characterId, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var created = false;
        if (!State.Party.TryGetCharacter(characterId, out var character) || character is null)
        {
            character = CreateInitialCharacter(characterId, characterId);
            State.Party.AddReserve(character);
            created = true;
        }

        character.Name = name;
        if (created)
        {
            _session.Events.Publish(new PartyChangedEvent());
        }

        _session.Events.Publish(new CharacterChangedEvent(character.Id));
        return character;
    }

    public IEnumerable<CharacterInstance> EnumerateActiveMembers() => State.Party.GetActiveMembers();

    public IEnumerable<CharacterInstance> EnumerateAllMembers() => State.Party.GetAllCharacters();

    public bool ContainsActiveMemberId(string characterId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characterId);
        return State.Party.ContainsMember(characterId) || State.Party.ContainsFollower(characterId);
    }

    public bool TryFindAllMember(string id, out CharacterInstance character)
    {
        if (State.Party.TryGetCharacter(id, out var found))
        {
            character = found;
            return true;
        }

        character = null!;
        return false;
    }

    private CharacterInstance CreateInitialCharacter(string characterId, string definitionId)
    {
        return _initialCharacterFactory.Create(characterId, definitionId, State.EquipmentInstanceFactory);
    }

    private void MoveToReserves(CharacterInstance character)
    {
        _session.InventoryService.UnequipAllToInventory(character);
        State.Party.MoveToReserves(character.Id);
    }
}
