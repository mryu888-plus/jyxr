using Game.Application.Persistence;
using Game.Application.Mods;
using Game.Godot.Persistence;

namespace Game.Godot.UI;

public static class SaveLoadWarningCoordinator
{
	public static async Task<bool> ConfirmAsync(
		LocalSaveEnvelope envelope,
		CancellationToken cancellationToken = default)
	{
		var comparison = LocalSaveStore.AssessCompatibility(envelope);
		if (!comparison.HasWarning)
		{
			return true;
		}

		var differences = FormatDifferences(comparison.Differences);
		if (comparison.WarningImpact == SaveImpact.Gameplay)
		{
			return await UIRoot.Instance.ShowConfirmAsync(
				$"该存档与当前 MOD 玩法环境不同，武功、物品或数值表现可能发生变化。\n\n{differences}\n\n仍要读取吗？",
				ConfirmDialogTone.Warning,
				cancellationToken);
		}

		var firstConfirmed = await UIRoot.Instance.ShowConfirmAsync(
			$"高风险：该存档与当前 MOD 的剧情或状态结构不同，读取后可能出现剧情错乱、状态丢失或无法继续。\n\n{differences}\n\n是否继续？",
			ConfirmDialogTone.Danger,
			cancellationToken);
		if (!firstConfirmed)
		{
			return false;
		}

		return await UIRoot.Instance.ShowConfirmAsync(
			"请先备份存档。读取后产生的自动、快速或手动存档可能固化异常状态。\n\n确认承担风险并读取该存档吗？",
			ConfirmDialogTone.Danger,
			cancellationToken);
	}

	private static string FormatDifferences(IReadOnlyList<ModLoadoutDifference> differences) =>
		string.Join(
			"\n",
			differences
				.GroupBy(static difference => difference.Id, StringComparer.Ordinal)
				.Select(static group =>
					$"• {group.Key}：{string.Join("、", group.Select(FormatDifference).Distinct(StringComparer.Ordinal))}"));

	private static string FormatDifference(ModLoadoutDifference difference) => difference.Kind switch
	{
		ModLoadoutDifferenceKind.Added => $"新增（{difference.CurrentVersion}）",
		ModLoadoutDifferenceKind.Removed => $"已移除（{difference.SavedVersion}）",
		ModLoadoutDifferenceKind.VersionChanged => $"版本 {difference.SavedVersion} → {difference.CurrentVersion}",
		ModLoadoutDifferenceKind.OrderChanged => "加载顺序变化",
		_ => throw new ArgumentOutOfRangeException(nameof(difference), difference.Kind, "Unsupported MOD difference kind."),
	};
}
