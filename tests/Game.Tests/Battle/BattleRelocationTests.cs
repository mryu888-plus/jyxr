using Game.Core.Affix;
using Game.Core.Battle;
using Game.Core.Definitions;
using Game.Core.Definitions.Skills;
using Game.Core.Model;

namespace Game.Tests.Battle;

public sealed class BattleRelocationTests
{
    private static readonly TalentDefinition RootedTalent = new()
    {
        Id = "rooted",
        Name = "rooted",
        Affixes = [new TraitAffix(TraitId.CannotMove)],
    };

    [Fact]
    public void Push_SlidesTargetAwayAndStopsBeforeBlockedCell()
    {
        var (state, engine, source, target) = Arrange(BattleRelocationKind.Push, 3, targetAt: new GridPosition(1, 1));
        var blocker = Unit("blocker", 2, new GridPosition(3, 1));
        state = new BattleState(new BattleGrid(6, 3), [source, target, blocker]);
        engine.BeginAction(state, source.Id);

        var result = Cast(engine, state, source, target);

        Assert.True(result.Success);
        Assert.Equal(new GridPosition(2, 1), target.Position);
        var fact = Assert.Single(result.Messages.OfType<BattleFact>(), static fact => fact.Kind == BattleFactKind.Relocated);
        Assert.Equal(new BattleRelocationEvent(new GridPosition(1, 1), new GridPosition(2, 1), BattleRelocationKind.Push, "source"), fact.Relocation);
    }

    [Fact]
    public void Pull_StopsAdjacentToSource()
    {
        var (state, engine, source, target) = Arrange(BattleRelocationKind.Pull, 5, targetAt: new GridPosition(4, 1));
        engine.BeginAction(state, source.Id);

        Assert.True(Cast(engine, state, source, target).Success);

        Assert.Equal(new GridPosition(1, 1), target.Position);
    }

    [Fact]
    public void Swap_ExchangesSourceAndTargetPositions()
    {
        var (state, engine, source, target) = Arrange(BattleRelocationKind.Swap, 1, targetAt: new GridPosition(2, 1));
        engine.BeginAction(state, source.Id);

        var result = Cast(engine, state, source, target);

        Assert.True(result.Success);
        Assert.Equal(new GridPosition(2, 1), source.Position);
        Assert.Equal(new GridPosition(0, 1), target.Position);
        Assert.Equal(2, result.Messages.OfType<BattleFact>().Count(static fact => fact.Kind == BattleFactKind.Relocated));
    }

    [Fact]
    public void ImmovableUnit_IsNotRelocated()
    {
        var (state, engine, source, target) = Arrange(BattleRelocationKind.Push, 2, targetAt: new GridPosition(1, 1), targetTalents: [RootedTalent]);
        engine.BeginAction(state, source.Id);

        var result = Cast(engine, state, source, target);

        Assert.True(result.Success);
        Assert.Equal(new GridPosition(1, 1), target.Position);
        Assert.DoesNotContain(result.Messages.OfType<BattleFact>(), static fact => fact.Kind == BattleFactKind.Relocated);
    }

    [Fact]
    public void Push_StopsAtBoardEdge()
    {
        var (state, engine, source, target) = Arrange(BattleRelocationKind.Push, 10, targetAt: new GridPosition(1, 1));
        engine.BeginAction(state, source.Id);

        Cast(engine, state, source, target);

        Assert.Equal(new GridPosition(5, 1), target.Position);
    }

    private static (BattleState State, BattleEngine Engine, BattleUnit Source, BattleUnit Target) Arrange(
        BattleRelocationKind kind,
        int distance,
        GridPosition targetAt,
        IReadOnlyList<TalentDefinition>? targetTalents = null)
    {
        var effect = new RelocateBattleEffectDefinition(new TargetBattleUnitSelectorDefinition(), kind, distance);
        var skill = new SpecialSkillDefinition(
            "relocate_test",
            "relocate_test",
            string.Empty,
            SpecialSkillIntent.Offensive,
            string.Empty,
            Cooldown: 0,
            SkillCostDefinition.None,
            new SkillTargetingDefinition(CastSize: 5, ImpactType: SkillImpactType.Single, ImpactSize: 0),
            string.Empty,
            string.Empty,
            Speech: null,
            Buffs: [],
            Effects: [effect]);
        var source = Unit("source", 1, new GridPosition(0, 1), specialSkills: [skill]);
        var target = Unit("target", 2, targetAt, talents: targetTalents);
        source.ActionGauge = 100;
        var state = new BattleState(new BattleGrid(6, 3), [source, target]);
        return (state, new BattleEngine(), source, target);
    }

    private static BattleCommandResult<BattleActionResult> Cast(
        BattleEngine engine,
        BattleState state,
        BattleUnit source,
        BattleUnit target) =>
        engine.CastSkill(state, source.Id, source.Character.GetSpecialSkills().Single(), target.Position);

    private static BattleUnit Unit(
        string id,
        int team,
        GridPosition position,
        IReadOnlyList<SpecialSkillDefinition>? specialSkills = null,
        IReadOnlyList<TalentDefinition>? talents = null)
    {
        var definition = TestContentFactory.CreateCharacterDefinition(
            id,
            new Dictionary<StatType, int> { [StatType.MaxHp] = 100, [StatType.MaxMp] = 30, [StatType.Speed] = 10 },
            talents: talents,
            specialSkills: specialSkills);
        return new BattleUnit(id, TestContentFactory.CreateCharacterInstance(id, definition), team, position);
    }
}
