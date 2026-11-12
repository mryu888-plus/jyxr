using Game.Core.Battle;
using Game.Core.Model;

namespace Game.Application;

internal sealed class BattleSettlementService(
    GameSession session,
    ZhenlongqijuBattleFactory zhenlongqijuFactory)
{
    private GameState State => session.State;
    private int PlayerTeam => session.Config.BattlePlayerTeam;

    public OrdinaryBattleVictorySettlement PreviewVictorySettlement(
        BattleState state,
        SpecialBattleRequest request)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(request);
        var battle = session.ContentRepository.GetBattle(request.BattleId);
        return request is ZhenlongqijuBattleRequest zhenlongqiju
            ? PreviewZhenlongqijuSettlement(state, zhenlongqiju.Level)
            : PreviewOrdinarySettlement(state, battle.ExperienceMultiplier);
    }

    public void ApplyVictorySettlement(BattleState state, OrdinaryBattleVictorySettlement settlement)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(settlement);

        foreach (var playerUnit in GetRewardUnits(state))
            session.CharacterService.GainExperience(playerUnit.Character.Id, settlement.ExperiencePerMember);

        if (settlement.Silver > 0)
        {
            State.Currency.AddSilver(settlement.Silver);
            session.Events.Publish(new CurrencyChangedEvent());
        }
        foreach (var reward in settlement.Rewards)
        {
            session.RewardGrantService.Apply(reward);
        }
    }

    private OrdinaryBattleVictorySettlement PreviewOrdinarySettlement(
        BattleState state,
        double experienceMultiplier)
    {
        var rewardUnitCount = GetRewardUnits(state).Count();
        var settlement = OrdinaryBattleVictorySettlementCalculator.Calculate(
            state,
            session.Config.BattleGoldDropChance,
            PlayerTeam,
            rewardUnitCount,
            experienceMultiplier * session.Config.BattleCharacterExperienceMultiplier);
        var drops = OrdinaryBattleLootGenerator.Generate(
            state,
            session.ContentRepository,
            session.Config,
            session.SkillMaxLevelPolicy,
            State.Adventure.Difficulty,
            State.Adventure.Round,
            PlayerTeam,
            session.Config.OrdinaryBattleDropChance,
            session.RandomService);
        return settlement with { Rewards = settlement.Rewards.Concat(drops).ToArray() };
    }

    private OrdinaryBattleVictorySettlement PreviewZhenlongqijuSettlement(BattleState state, int level)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        var settlement = OrdinaryBattleVictorySettlementCalculator.Calculate(
            state, 0d, PlayerTeam, GetRewardUnits(state).Count(),
            session.Config.BattleCharacterExperienceMultiplier);
        return settlement with
        {
            Rewards = settlement.Rewards
                .Append<RewardGrant>(new YuanbaoRewardGrant(level / 2 + 1))
                .Concat(zhenlongqijuFactory.GenerateDrops(level))
                .ToArray(),
        };
    }

    private IEnumerable<BattleUnit> GetRewardUnits(BattleState state) =>
        state.Units.Where(unit =>
            unit.Team == PlayerTeam && State.Party.ContainsMember(unit.Character.Id));
}
