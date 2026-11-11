using Game.Core.Model.Skills;

namespace Game.Core.Battle;

/// <summary>
/// 默认技能评分器：用引擎的技能执行预览解析实际作用单位（含 all_enemies、all_units 等选择器派生的目标），
/// 汇总伤害、击杀与非伤害收益。外功、内功招式、奥义与攻击型特殊技能都走同一条路径，不按技能名特判。
/// </summary>
public sealed class BattleSkillPreviewAiScorer : IBattleSkillAiScorer
{
    public bool CanScore(SkillInstance skill)
    {
        ArgumentNullException.ThrowIfNull(skill);
        return skill is SpecialSkillInstance || skill.Power > 0d;
    }

    public BattleSkillAiEvaluation Score(BattleSkillAiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var evaluation = context.Evaluation
            ?? throw new InvalidOperationException("Skill preview scoring requires a battle AI evaluation context.");

        var preview = evaluation.PreviewBuffer;
        preview.Clear();
        evaluation.Engine.PreviewSkillExecution(
            context.State,
            context.Source,
            context.MoveDestination,
            context.Skill,
            context.Targets,
            evaluation.SkillDamageEstimator,
            preview);
        return BattleAiOutcomeValuation.Evaluate(context.State, context.Source, preview);
    }
}
