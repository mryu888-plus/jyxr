using Game.Application;
using Game.Core.Model;
using Game.Core.Story;
using Game.Godot.UI;

namespace Game.Godot.Story;

public sealed partial class GodotStoryRuntimeHost : IRuntimeHost, ISpecialBattleRuntimeHost, IApplicationRuntimeHost, IMiniGameRuntimeHost, IItemUseRuntimeHost
{
	public ValueTask DialogueAsync(DialogueContext dialogue, CancellationToken cancellationToken) =>
		new(UIRoot.Instance.ShowDialogueAsync(dialogue.Speaker, dialogue.Text, cancellationToken));

	public async ValueTask<int> ChooseOptionAsync(ChoiceContext choice, CancellationToken cancellationToken)
	{
		var visibleIndex = await UIRoot.Instance.ShowChoicesAsync(
			choice.PromptSpeaker,
			choice.PromptText,
			choice.Options.Select(static option => option.Text).ToArray(),
			choice.Style,
			cancellationToken);
		return choice.Options[visibleIndex].Index;
	}

	public async ValueTask<BattleOutcome> ResolveBattleAsync(BattleContext battle, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(battle);
		var selectedCharacterIds = await UIRoot.Instance.ShowCombatantSelectPanelAsync(battle.BattleId, cancellationToken);
		var isWin = await UIRoot.Instance.ShowBattleScreenAsync(
			new OrdinaryBattleRequest(battle.BattleId, selectedCharacterIds.ToArray()),
			cancellationToken);
		return isWin ? BattleOutcome.Win : BattleOutcome.Lose;
	}

	public async ValueTask<InventoryEntry?> SelectRefinementEquipmentAsync(
		IReadOnlyList<InventoryEntry> entries,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(entries);
		return await UIRoot.Instance.ShowRefinementEquipmentSelectionPanelAsync(entries, cancellationToken);
	}

	public async ValueTask<global::Game.Core.Model.Skills.SkillInstance?> SelectSkillToForgetAsync(
		global::Game.Core.Model.Character.CharacterInstance character,
		IReadOnlyList<global::Game.Core.Model.Skills.SkillInstance> candidates,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(character);
		ArgumentNullException.ThrowIfNull(candidates);
		var options = candidates
			.Select(static skill => $"{skill.Name}（{FormatSkillKind(skill)} {skill.Level}级）")
			.Append("取消")
			.ToArray();
		var index = await UIRoot.Instance.ShowChoicesAsync(
			character.Name,
			$"要让{character.Name}遗忘哪门武学？",
			options,
			cancellationToken: cancellationToken);
		return index >= 0 && index < candidates.Count ? candidates[index] : null;
	}

	private static string FormatSkillKind(global::Game.Core.Model.Skills.SkillInstance skill) =>
		skill switch
		{
			global::Game.Core.Model.Skills.SpecialSkillInstance => "特技",
			global::Game.Core.Model.Skills.InternalSkillInstance => "内功",
			_ => "外功",
		};

	public async ValueTask<IReadOnlyList<string>> SelectCombatantsAsync(
		CombatantSelectionRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return await UIRoot.Instance.ShowCombatantSelectPanelAsync(
			request.BattleId,
			request.ForbiddenCharacterIds,
			cancellationToken);
	}

	public async ValueTask<bool> RunBattleAsync(
		SpecialBattleRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return await UIRoot.Instance.ShowBattleScreenAsync(request, cancellationToken);
	}

	public ValueTask<int> RunLightnessTrainingAsync(CancellationToken cancellationToken) =>
		new(UIRoot.Instance.ShowLightnessTrainingScreenAsync(cancellationToken));

	public ValueTask<(int Score, IReadOnlyDictionary<string, int> ItemCounts)> RunStrengthTrainingAsync(
		IReadOnlyList<string> itemIds,
		CancellationToken cancellationToken) =>
		new(UIRoot.Instance.ShowStrengthTrainingScreenAsync(itemIds, cancellationToken));

	public ValueTask PlayEffectAsync(string effectId, CancellationToken cancellationToken) => ExecuteEffectAsync(effectId);

	public ValueTask GameOverAsync(CancellationToken cancellationToken) => ExecuteGameOverAsync();
}
