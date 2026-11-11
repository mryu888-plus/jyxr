using Game.Core.Definitions.Skills;
using Game.Core.Model;
using Game.Core.Model.Skills;

namespace Game.Core.Battle;

public sealed class BattleTurnCandidateGenerator
{
    private static readonly GridPosition Origin = new(0, 0);

    private readonly BattleEngine _engine;
    private readonly IReadOnlyList<IBattleSkillAiScorer> _skillScorers;

    public BattleTurnCandidateGenerator(
        BattleEngine engine,
        IReadOnlyList<IBattleSkillAiScorer>? skillScorers = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _skillScorers = skillScorers ?? [new BattleSkillPreviewAiScorer()];
    }

    public BattleEngine Engine => _engine;

    /// <summary>创建本次决策的评估上下文（同一决策内的多次枚举应共用同一个上下文）。</summary>
    public BattleAiEvaluationContext CreateEvaluationContext(BattleState state, string unitId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(unitId);
        return new BattleAiEvaluationContext(_engine, state, state.GetUnit(unitId));
    }

    /// <summary>物化全部候选。主要供测试和调试使用；AI 决策走 <see cref="Enumerate"/> 流式择优。</summary>
    public IReadOnlyList<BattleTurnCandidate> Generate(
        BattleState state,
        string unitId,
        BattleTurnCandidateGenerationOptions? options = null) =>
        Enumerate(CreateEvaluationContext(state, unitId), options).ToList();

    /// <summary>
    /// 流式枚举候选：落点 × 技能 × 施放格。施放格对应的覆盖格按技能预先算好的相对模板平移，
    /// 主要目标经位置索引查找，并用技能执行预览评估实际作用单位与收益。
    /// </summary>
    public IEnumerable<BattleTurnCandidate> Enumerate(
        BattleAiEvaluationContext context,
        BattleTurnCandidateGenerationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!ReferenceEquals(context.Engine, _engine))
        {
            throw new ArgumentException("Evaluation context belongs to a different battle engine.", nameof(context));
        }

        return EnumerateCore(context, options ?? BattleTurnCandidateGenerationOptions.Default);
    }

    private IEnumerable<BattleTurnCandidate> EnumerateCore(
        BattleAiEvaluationContext context,
        BattleTurnCandidateGenerationOptions options)
    {
        var state = context.State;
        var unit = context.Unit;
        IReadOnlyDictionary<GridPosition, int> reachablePositions = options.AllowMovement
            ? context.ReachablePositions
            : new Dictionary<GridPosition, int> { [unit.Position] = 0 };
        var preparedSkills = options.AllowSkillCandidates
            ? PrepareSkills(state, unit, options.SkillFilter)
            : [];

        foreach (var (destination, moveCost) in reachablePositions)
        {
            var distanceToNearestEnemy = context.GetDistanceToNearestEnemy(destination);
            foreach (var preparedSkill in preparedSkills)
            {
                foreach (var cast in preparedSkill.CastTemplates)
                {
                    var target = Translate(destination, cast.Offset);
                    if (!state.Grid.Contains(target))
                    {
                        continue;
                    }

                    var hasEnemyTarget = CollectPrimaryTargets(context, preparedSkill.Skill, destination, cast);
                    if (preparedSkill.HasDamageStep && !hasEnemyTarget)
                    {
                        // 伤害型技能沿用原版规则: 覆盖范围内必须有敌人
                        continue;
                    }

                    var evaluation = preparedSkill.Scorer.Score(new BattleSkillAiContext(
                        state,
                        unit,
                        preparedSkill.Skill,
                        destination,
                        target,
                        context.PrimaryTargetBuffer,
                        context));
                    if (!preparedSkill.HasDamageStep &&
                        evaluation.EnemyDamage <= 0 &&
                        evaluation.UtilityScore <= 0d)
                    {
                        // 非伤害步骤的攻击型技能: 必须有实际收益（伤害或非伤害收益）
                        continue;
                    }

                    yield return new BattleTurnCandidate(
                        new BattleTurnPlan(unit.Id, destination, BattleMainActionPlan.CastSkill(preparedSkill.Skill.Id, target)),
                        Score: 0d,
                        evaluation.EnemyDamage,
                        evaluation.AllyDamage,
                        evaluation.EnemyKills,
                        evaluation.AllyKills,
                        evaluation.EnemyHitCount,
                        DistanceToNearestEnemy: distanceToNearestEnemy,
                        MoveCost: moveCost,
                        UtilityScore: evaluation.UtilityScore);
                }
            }

            if (options.AllowRestCandidates)
            {
                yield return new BattleTurnCandidate(
                    new BattleTurnPlan(unit.Id, destination, BattleMainActionPlan.Rest()),
                    Score: 0d,
                    EnemyDamage: 0,
                    AllyDamage: 0,
                    EnemyKills: 0,
                    AllyKills: 0,
                    EnemyHitCount: 0,
                    DistanceToNearestEnemy: distanceToNearestEnemy,
                    MoveCost: moveCost);
            }
        }
    }

    public BattleTurnPlan? CreateRandomSupportSpecialSkillPlan(
        BattleState state,
        string unitId,
        GridPosition destination) =>
        CreateRandomSupportSpecialSkillPlan(CreateEvaluationContext(state, unitId), destination);

    /// <summary>
    /// 原版行为：没有攻击候选时，有一半机会对自己施放辅助特殊技能。
    /// 在此基础上先用技能执行预览排除有害的辅助技能，并优先在预计有收益的技能中随机选择；
    /// 都没有可预览的收益时退回原版的“任选一个”。
    /// </summary>
    public BattleTurnPlan? CreateRandomSupportSpecialSkillPlan(
        BattleAiEvaluationContext context,
        GridPosition destination)
    {
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var unit = context.Unit;
        var beneficial = new List<SpecialSkillInstance>();
        var neutral = new List<SpecialSkillInstance>();
        foreach (var skill in unit.Character.GetSpecialSkills())
        {
            if (!skill.IsActive ||
                skill.Definition.Intent != SpecialSkillIntent.Support ||
                !skill.CanCastAtSelf ||
                !_engine.EvaluateSkillAvailability(state, unit.Id, skill).IsAvailable)
            {
                continue;
            }

            var value = EvaluateSelfCast(context, skill, destination);
            if (value > 0d)
            {
                beneficial.Add(skill);
            }
            else if (value == 0d)
            {
                neutral.Add(skill);
            }
        }

        var skills = beneficial.Count > 0 ? beneficial : neutral;
        if (skills.Count == 0)
        {
            return null;
        }

        var selected = skills[_engine.RandomService.Next(0, skills.Count)];
        return new BattleTurnPlan(
            unit.Id,
            destination,
            BattleMainActionPlan.CastSkill(selected.Id, destination));
    }

    internal bool ShouldUseSupportSpecialSkill() =>
        _engine.RandomService.Next(0, 2) == 0;

    private double EvaluateSelfCast(BattleAiEvaluationContext context, SkillInstance skill, GridPosition destination)
    {
        var targets = context.PrimaryTargetBuffer;
        targets.Clear();
        var impactSize = BattleSkillTargeting.ResolveEffectiveImpactSize(context.Unit, skill);
        foreach (var position in BattleEngine.GetImpactPositions(destination, destination, skill.ImpactType, impactSize))
        {
            if (context.State.Grid.Contains(position) &&
                context.GetUnitAt(position, destination) is { IsAlive: true } occupant &&
                BattleSkillTargeting.CanAffect(context.State, context.Unit, occupant, skill))
            {
                targets.Add(occupant);
            }
        }

        var preview = context.PreviewBuffer;
        preview.Clear();
        _engine.PreviewSkillExecution(
            context.State,
            context.Unit,
            destination,
            skill,
            targets,
            context.SkillDamageEstimator,
            preview);
        var evaluation = BattleAiOutcomeValuation.Evaluate(context.State, context.Unit, preview);
        return evaluation.EnemyDamage - evaluation.AllyDamage + evaluation.UtilityScore;
    }

    /// <summary>把施放格模板平移到落点，经位置索引收集主要目标；返回其中是否有敌人。</summary>
    private static bool CollectPrimaryTargets(
        BattleAiEvaluationContext context,
        SkillInstance skill,
        GridPosition destination,
        CastTemplate cast)
    {
        var state = context.State;
        var unit = context.Unit;
        var targets = context.PrimaryTargetBuffer;
        targets.Clear();
        var hasEnemy = false;
        foreach (var offset in cast.ImpactOffsets)
        {
            var position = Translate(destination, offset);
            if (!state.Grid.Contains(position) ||
                context.GetUnitAt(position, destination) is not { IsAlive: true } occupant ||
                !BattleSkillTargeting.CanAffect(state, unit, occupant, skill))
            {
                continue;
            }

            targets.Add(occupant);
            hasEnemy |= state.AreEnemies(unit, occupant);
        }

        return hasEnemy;
    }

    private IReadOnlyList<PreparedSkill> PrepareSkills(
        BattleState state,
        BattleUnit unit,
        Func<SkillInstance, bool>? skillFilter)
    {
        var preparedSkills = new List<PreparedSkill>();
        foreach (var skill in BattleSkillCatalog.CollectSelectableSkills(unit))
        {
            if (skillFilter is not null && !skillFilter(skill))
            {
                continue;
            }

            if (skill is SpecialSkillInstance specialSkill &&
                specialSkill.Definition.Intent != SpecialSkillIntent.Offensive)
            {
                continue;
            }

            if (!_engine.EvaluateSkillAvailability(state, unit.Id, skill).IsAvailable)
            {
                continue;
            }

            var scorer = _skillScorers.FirstOrDefault(candidateScorer => candidateScorer.CanScore(skill));
            if (scorer is null)
            {
                continue;
            }

            preparedSkills.Add(new PreparedSkill(
                skill,
                scorer,
                BattleSkillExecutionPlanFactory.Create(skill).Steps.Any(static step => step is ResolveSkillDamageStep),
                CreateCastTemplates(
                    skill,
                    BattleSkillTargeting.ResolveEffectiveCastSize(unit, skill),
                    BattleSkillTargeting.ResolveEffectiveImpactSize(unit, skill))));
        }

        return preparedSkills;
    }

    /// <summary>
    /// 覆盖形状只取决于施放格相对施法者的偏移（与 <see cref="BattleEngine.GetImpactPositions"/> 平移不变），
    /// 因此每个技能只在原点算一次模板，枚举时按落点平移。
    /// </summary>
    private static IReadOnlyList<CastTemplate> CreateCastTemplates(SkillInstance skill, int castSize, int impactSize)
    {
        var templates = new List<CastTemplate>();
        var size = Math.Max(0, castSize);
        for (var y = -size; y <= size; y++)
        {
            for (var x = -size; x <= size; x++)
            {
                var offset = new GridPosition(x, y);
                if (Origin.ManhattanDistanceTo(offset) > size || offset == Origin && !skill.CanCastAtSelf)
                {
                    continue;
                }

                templates.Add(new CastTemplate(
                    offset,
                    BattleEngine.GetImpactPositions(Origin, offset, skill.ImpactType, impactSize).ToArray()));
            }
        }

        return templates;
    }

    private static GridPosition Translate(GridPosition origin, GridPosition offset) =>
        new(origin.X + offset.X, origin.Y + offset.Y);

    private sealed record CastTemplate(GridPosition Offset, GridPosition[] ImpactOffsets);

    private sealed record PreparedSkill(
        SkillInstance Skill,
        IBattleSkillAiScorer Scorer,
        bool HasDamageStep,
        IReadOnlyList<CastTemplate> CastTemplates);
}
