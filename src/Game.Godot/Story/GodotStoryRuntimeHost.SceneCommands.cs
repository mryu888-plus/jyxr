using Game.Application;
using Game.Core.Model;
using Game.Godot.UI;

namespace Game.Godot.Story;

public sealed partial class GodotStoryRuntimeHost
{
	[StoryCommand("story")]
	private async ValueTask ExecuteStoryAsync(string storyId, CancellationToken cancellationToken)
	{
		var wasStoryPresentationActive = UIRoot.Instance.IsStoryPresentationActive;
		if (!wasStoryPresentationActive)
		{
			UIRoot.Instance.SetStoryPresentationActive(true);
		}

		try
		{
			var executionState = Game.State;
			if (!await StoryRunHelper.RunAsync(storyId, cancellationToken) &&
				ReferenceEquals(executionState, Game.State))
			{
				throw new InvalidOperationException($"Story command '{storyId}' did not complete a segment.");
			}
		}
		finally
		{
			if (!wasStoryPresentationActive && global::Godot.GodotObject.IsInstanceValid(UIRoot.Instance))
			{
				UIRoot.Instance.SetStoryPresentationActive(false);
			}
		}
	}

	[StoryCommand("map", "set_map", "tutorial")]
	private ValueTask ExecuteMapAsync(string mapId, params string[] locationIds)
	{
		if (locationIds.Length > 1)
		{
			throw new InvalidOperationException("Map command accepts at most one location id.");
		}

		if (locationIds.Length == 0)
		{
			World.Instance.EnterMap(mapId);
		}
		else
		{
			World.Instance.EnterMap(mapId, locationIds[0]);
		}

		return ValueTask.CompletedTask;
	}

	[StoryCommand("shop")]
	private ValueTask ExecuteShopAsync(string shopId, CancellationToken cancellationToken) =>
		new(UIRoot.Instance.ShowShopPanelAsync(shopId, cancellationToken));

	[StoryCommand("chest", "xiangzi")]
	private ValueTask ExecuteChestAsync(CancellationToken cancellationToken) =>
		new(UIRoot.Instance.ShowChestPanelAsync(cancellationToken));

	[StoryCommand("battle")]
	private async ValueTask ExecuteBattleAsync(
		string battleId,
		string winStoryId = "",
		string loseStoryId = "",
		CancellationToken cancellationToken = default)
	{
		var selected = await UIRoot.Instance.ShowCombatantSelectPanelAsync(battleId, cancellationToken);
		var isWin = await UIRoot.Instance.ShowBattleScreenAsync(
			new OrdinaryBattleRequest(battleId, selected.ToArray()),
			cancellationToken);
		var followUp = BattleResultProjection.FromCommandArguments(winStoryId, loseStoryId).Resolve(isWin);
		switch (followUp.Kind)
		{
			case BattleEncounterFollowUpKind.GameOver:
				GameFlow.GameOver();
				break;
			case BattleEncounterFollowUpKind.RunStory:
				await ExecuteStoryAsync(followUp.StoryId!, cancellationToken);
				break;
		}
	}

	[StoryCommand("background")]
	private ValueTask ExecuteBackgroundAsync(string backgroundId)
	{
		World.Instance.SetBackground(backgroundId);
		return ValueTask.CompletedTask;
	}
}
