using Game.Core.Affix;

namespace Game.Core.Model.Skills;

public static class SkillTargetingResolver
{
    public static int ResolveCastSize(SkillInstance skill)
    {
        return skill.Owner.GetSkillTargetingValue(
            skill,
            SkillTargetingField.CastSize,
            skill.CastSize);
    }

    public static int ResolveImpactSize(SkillInstance skill)
    {
        return skill.Owner.GetSkillTargetingValue(
            skill,
            SkillTargetingField.ImpactSize,
            skill.ImpactSize);
    }
}
