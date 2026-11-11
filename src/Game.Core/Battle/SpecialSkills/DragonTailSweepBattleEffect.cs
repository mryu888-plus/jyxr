using Game.Core.Model;

namespace Game.Core.Battle.SpecialSkills;

public sealed record DragonTailSweepBattleEffectParameters;

public sealed class DragonTailSweepBattleEffectHandler
    : CustomAbilityBattleEffectHandler<DragonTailSweepBattleEffectParameters>
{
    public override void Execute(
        IBattleAbilityEffectContext context,
        DragonTailSweepBattleEffectParameters parameters)
    {
        foreach (var target in context.Targets)
        {
            // 原版：沿出手方向一路推到撞上障碍，推得越远伤害越高。
            var movedCells = context.Push(target, int.MaxValue).Cells;
            var distanceFactor = movedCells + 1;
            var damage = context.Random.Next(distanceFactor * 100, distanceFactor * 500 + 1);
            context.DealDamage(target, damage);
        }
    }

    public override int? EstimateDamage(
        BattleAbilityDamageEstimateContext context,
        DragonTailSweepBattleEffectParameters parameters)
    {
        var directionX = context.Target.Position.X - context.SourcePosition.X;
        var directionY = context.Target.Position.Y - context.SourcePosition.Y;
        var destination = context.Target.Position;
        var movedCells = 0;

        while (true)
        {
            var next = new GridPosition(destination.X + directionX, destination.Y + directionY);
            if (!context.IsCellAvailable(next, context.Target))
            {
                break;
            }

            destination = next;
            movedCells++;
        }

        return (movedCells + 1) * 300;
    }
}
