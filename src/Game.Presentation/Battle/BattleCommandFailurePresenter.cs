using Game.Core.Battle;

namespace Game.Presentation.Battle;

public static class BattleCommandFailurePresenter
{
    public static string Format(BattleCommandFailure failure)
    {
        return failure.Reason switch
        {
            BattleCommandFailureReason.UnitAlreadyActing => "已有角色正在行动。",
            BattleCommandFailureReason.UnitDefeated => "该角色已被击败。",
            BattleCommandFailureReason.UnitNotReady => "该角色尚未准备好行动。",
            BattleCommandFailureReason.NoUnitActing => "当前没有正在行动的角色。",
            BattleCommandFailureReason.WrongActingUnit => "当前不是该角色的行动回合。",
            BattleCommandFailureReason.MainActionCommitted => "本回合的主要行动已经完成。",
            BattleCommandFailureReason.SkillOwnerMismatch => "该角色未掌握此技能。",
            BattleCommandFailureReason.SkillInactive => "该技能当前未启用。",
            BattleCommandFailureReason.SkillOnCooldown => "该技能尚在冷却。",
            BattleCommandFailureReason.SkillDisabled => "该技能当前无法使用。",
            BattleCommandFailureReason.SkillUnavailable => "该技能当前不可用。",
            BattleCommandFailureReason.NotEnoughMp => "内力不足。",
            BattleCommandFailureReason.NotEnoughRage => "怒气不足。",
            BattleCommandFailureReason.SkillCannotTargetSelf => "该技能不能对自己施展。",
            BattleCommandFailureReason.TargetOutOfCastRange => "目标超出施展范围。",
            BattleCommandFailureReason.InvalidItemTarget => "无法对该目标使用物品。",
            BattleCommandFailureReason.ItemCannotTargetEnemy => "物品不能对敌人使用。",
            BattleCommandFailureReason.ItemCannotTargetAlly => "该角色不能对队友使用物品。",
            BattleCommandFailureReason.AllyItemTargetOutOfRange => "目标距离过远，无法使用物品。",
            BattleCommandFailureReason.ItemOnCooldown =>
                $"还需等待 {failure.RemainingTurns ?? 0} 回合才能再次使用物品。",
            BattleCommandFailureReason.DestinationUnreachable => "无法移动到该位置。",
            BattleCommandFailureReason.MovementRollbackAfterMainAction => "主要行动完成后无法取消移动。",
            BattleCommandFailureReason.TimelineAdvanceLimitReached => "暂时没有角色可以行动。",
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };
    }
}
