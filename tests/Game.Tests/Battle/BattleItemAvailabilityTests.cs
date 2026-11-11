using Game.Core.Affix;
using Game.Core.Battle;
using Game.Core.Definitions;
using Game.Core.Model;
using Game.Presentation.Battle;

namespace Game.Tests.Battle;

public sealed class BattleItemAvailabilityTests
{
    private static readonly TalentDefinition AllyItemTalent = new()
    {
        Id = "ally_item",
        Name = "ally_item",
        Affixes = [new TraitAffix(TraitId.CanUseItemOnAlly)],
    };

    internal static BattleUnit Unit(string id, int team, GridPosition position, params TalentDefinition[] talents)
    {
        var definition = TestContentFactory.CreateCharacterDefinition(
            id,
            new Dictionary<StatType, int> { [StatType.MaxHp] = 100, [StatType.MaxMp] = 30 },
            talents: talents);
        return new BattleUnit(id, TestContentFactory.CreateCharacterInstance(id, definition), team, position);
    }

    [Fact]
    public void Evaluate_SelfOnCooldownWithoutAllyTrait_BlocksWithCooldownFailure()
    {
        var hero = Unit("hero", 1, new GridPosition(0, 0));
        hero.AddItemCooldown(2);
        var state = new BattleState(new BattleGrid(4, 4), [hero]);

        var availability = BattleItemAvailability.Evaluate(state, hero);

        Assert.False(availability.CanOpen);
        Assert.Equal(BattleCommandFailureReason.ItemOnCooldown, availability.BlockingFailure?.Reason);
        Assert.Equal(2, availability.BlockingFailure?.RemainingTurns);
    }

    [Fact]
    public void Evaluate_SelfOnCooldownWithAllyInRange_OpensAndExcludesSelf()
    {
        var hero = Unit("hero", 1, new GridPosition(0, 0), AllyItemTalent);
        var ally = Unit("ally", 1, new GridPosition(2, 0));
        var farAlly = Unit("far", 1, new GridPosition(3, 3));
        var enemy = Unit("enemy", 2, new GridPosition(1, 0));
        hero.AddItemCooldown(1);
        var state = new BattleState(new BattleGrid(5, 5), [hero, ally, farAlly, enemy]);

        var availability = BattleItemAvailability.Evaluate(state, hero);

        Assert.True(availability.CanOpen);
        Assert.Null(availability.BlockingFailure);
        Assert.Equal(["ally"], availability.AvailableTargets.Select(static unit => unit.Id));
    }

    [Fact]
    public void Evaluate_NormalDifficultyCooldownRuleIgnoresCooldown()
    {
        var hero = Unit("hero", 1, new GridPosition(0, 0));
        hero.AddItemCooldown(2);
        var state = new BattleState(
            new BattleGrid(4, 4),
            [hero],
            new BattleRuleSettings { EnableDifficultyItemCooldownRules = true, Difficulty = GameDifficulty.Normal });

        Assert.True(BattleItemAvailability.Evaluate(state, hero).CanOpen);
    }

    [Fact]
    public async Task OpenItems_WhenNoTargetIsUsable_ShowsFailureAndKeepsState()
    {
        var hero = Unit("hero", 1, new GridPosition(0, 0));
        hero.AddItemCooldown(3);
        var state = new BattleState(new BattleGrid(4, 4), [hero]);
        var context = new ItemFlowContext(state, hero);
        var machine = new BattleFlowStateMachine(context);
        await machine.StartAsync(new UnitActingState());

        await machine.DispatchAsync(new BattleUiIntent.OpenItems());

        Assert.Equal(BattleFlowStateKind.UnitActing, machine.Current.Kind);
        Assert.Equal(0, context.ItemPanelOpenCount);
        var failure = Assert.Single(context.Failures);
        Assert.Equal(BattleCommandFailureReason.ItemOnCooldown, failure.Reason);
        Assert.Equal(3, failure.RemainingTurns);
    }

    [Fact]
    public async Task OpenItems_WhenAllyIsUsable_OpensItemPanel()
    {
        var hero = Unit("hero", 1, new GridPosition(0, 0), AllyItemTalent);
        var ally = Unit("ally", 1, new GridPosition(1, 0));
        hero.AddItemCooldown(3);
        var state = new BattleState(new BattleGrid(4, 4), [hero, ally]);
        var context = new ItemFlowContext(state, hero);
        var machine = new BattleFlowStateMachine(context);
        await machine.StartAsync(new UnitActingState());

        await machine.DispatchAsync(new BattleUiIntent.OpenItems());

        Assert.Equal(1, context.ItemPanelOpenCount);
        Assert.Empty(context.Failures);
    }

    private sealed class ItemFlowContext(BattleState state, BattleUnit acting) : IBattleFlowContext
    {
        public List<BattleCommandFailure> Failures { get; } = [];
        public int ItemPanelOpenCount { get; private set; }
        public BattleState State => state;
        public BattleEngine Engine => throw new NotSupportedException();
        public bool IsAutoBattleEnabled => false;
        public bool IsSurrenderRequested => false;
        public int PlayerTeam => acting.Team;
        public BattleUnit? ActingUnit => acting;
        public void RenderInteraction(BattleInteractionState interaction) { }
        public void CommitBattleStateToView(BattleInteractionState interaction) { }
        public void BeginActionPresentation(BattleInteractionState interaction) { }
        public void ToggleAutoBattle() => throw new NotSupportedException();
        public void RequestSurrender() => throw new NotSupportedException();
        public BattleUnit AdvanceTimelineToNextAction() => throw new NotSupportedException();
        public BattleTurnPlan DecideAutomatedTurn(BattleUnit actingUnit) => throw new NotSupportedException();
        public void AppendTurnLog(BattleUnit unit, bool isAutomated) { }
        public Task<bool> ExecuteMoveAsync(BattleUnit actingUnit, GridPosition destination) => throw new NotSupportedException();
        public Task<bool> ExecuteSkillAsync(BattleUnit actingUnit, Game.Core.Model.Skills.SkillInstance skill, GridPosition target) => throw new NotSupportedException();
        public bool ExecuteItem(BattleUnit actingUnit, InventoryEntry item, string targetUnitId) => throw new NotSupportedException();
        public Task<bool> ExecuteRestAsync(BattleUnit actingUnit) => throw new NotSupportedException();
        public bool ExecuteEndAction(BattleUnit actingUnit) => throw new NotSupportedException();
        public Task<bool> RollbackMoveAsync(BattleUnit actingUnit) => throw new NotSupportedException();
        public Game.Core.Model.Skills.SkillInstance? ResolveDefaultSkill(BattleUnit actingUnit) => null;
        public Game.Core.Model.Skills.SkillInstance? ResolveSkill(BattleUnit actingUnit, string? skillId) => null;
        public Task<InventoryEntry?> ShowItemPanelAsync()
        {
            ItemPanelOpenCount++;
            return Task.FromResult<InventoryEntry?>(null);
        }
        public void ShowCommandFailure(BattleCommandFailure failure) => Failures.Add(failure);
        public void ShowStatusPanel() { }
        public bool TryGetBattleResult(out bool isWin) { isWin = false; return false; }
        public Task CompleteBattleAsync(bool isWin) => Task.CompletedTask;
    }
}
