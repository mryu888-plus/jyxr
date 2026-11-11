using Game.Core.Model.Skills;

namespace Game.Core.Battle;

internal sealed class TrainingBattleAiPolicy : IBattleAiPolicy
{
    private readonly Func<SkillInstance, int> _skillMaxLevelResolver;

    public TrainingBattleAiPolicy(Func<SkillInstance, int> skillMaxLevelResolver)
    {
        _skillMaxLevelResolver = skillMaxLevelResolver ?? throw new ArgumentNullException(nameof(skillMaxLevelResolver));
    }

    public IEnumerable<BattleTurnCandidate> GenerateCandidates(
        BattleAiEvaluationContext context,
        BattleTurnCandidateGenerator generator)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(generator);
        var unit = context.Unit;

        var progressionSkills = new HashSet<SkillInstance>(ReferenceEqualityComparer.Instance);
        foreach (var skill in BattleSkillCatalog.CollectSelectableSkills(unit))
        {
            if (skill is FormSkillInstance)
            {
                continue;
            }

            if (SkillExperienceProgression.NormalizeProgressionSkill(skill) is { } progressionSkill)
            {
                progressionSkills.Add(progressionSkill);
            }
        }

        if (progressionSkills.Count == 0 ||
            progressionSkills.All(skill => skill.Level >= _skillMaxLevelResolver(skill)))
        {
            return generator.Enumerate(context);
        }

        return generator.Enumerate(
            context,
            new BattleTurnCandidateGenerationOptions(SkillFilter: IsTrainingSkillAllowed));
    }

    private bool IsTrainingSkillAllowed(SkillInstance skill)
    {
        var progressionSkill = SkillExperienceProgression.NormalizeProgressionSkill(skill);
        return progressionSkill is null ||
            progressionSkill.Level < _skillMaxLevelResolver(progressionSkill);
    }
}
