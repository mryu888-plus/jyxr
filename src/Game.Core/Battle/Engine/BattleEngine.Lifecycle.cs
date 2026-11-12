using Game.Core.Affix;

namespace Game.Core.Battle;

public sealed partial class BattleEngine
{
    public BattleCommandResult<BattleState> StartBattle(BattleState state)
    {
        using var command = state.BeginCommand();
        if (state.HasStarted)
            return BattleCommandResult<BattleState>.Succeeded(state, command.Messages);

        state.HasStarted = true;
        foreach (var unit in state.Units.Where(static unit => unit.IsAlive))
        {
            TriggerHooks(state, HookTiming.OnBattleStart, unit);
        }
        return BattleCommandResult<BattleState>.Succeeded(state, command.Messages);
    }
}
