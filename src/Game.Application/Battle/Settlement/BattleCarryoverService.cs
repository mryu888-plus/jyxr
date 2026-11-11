using Game.Core.Battle;

namespace Game.Application;

internal sealed class BattleCarryoverService(GameSession session)
{
    public void ApplyPlayerBattleCarryover(BattleState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        foreach (var playerUnit in GetPlayerPartyUnits(state))
        {
            var character = playerUnit.Character;
            // 战斗内成长 (BattleGrowthResolver) 直接改了 CharacterInstance; 这里补上应用层升级路径的资源上限规则,
            // 并桥接成应用层事件, 让 HUD、档案、成就等 session 订阅者看到战斗中的升级.
            var leveledUp = playerUnit.ReceivesPersistentGrowth && character.Level > playerUnit.InitialCharacterLevel;
            if (leveledUp)
            {
                session.CharacterResourceLimitPolicy.ClampBaseResourceStats(character);
            }

            character.ApplyBattleCarryover(playerUnit.Hp, playerUnit.Mp, playerUnit.Rage);
            if (leveledUp)
            {
                session.Events.Publish(new CharacterLeveledUpEvent(
                    character.Id,
                    playerUnit.InitialCharacterLevel,
                    character.Level));
            }

            session.Events.Publish(new CharacterChangedEvent(character.Id));
        }
    }

    public void RestorePartyBattleResources()
    {
        foreach (var member in session.State.Party.Members)
        {
            member.RestoreBattleResources();
        }
    }

    private IEnumerable<BattleUnit> GetPlayerPartyUnits(BattleState state) =>
        state.Units.Where(unit =>
            unit.Team == session.Config.BattlePlayerTeam &&
            session.State.Party.ContainsMember(unit.Character.Id));
}
