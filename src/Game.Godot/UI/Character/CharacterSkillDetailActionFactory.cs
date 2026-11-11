using Game.Application;
using Game.Core.Model.Skills;

namespace Game.Godot.UI;

public static class CharacterSkillDetailActionFactory
{
	public static DetailPanelAction? CreateForgetAction(SkillInstance skill)
	{
		ArgumentNullException.ThrowIfNull(skill);

		return CharacterService.GetForgetAvailability(skill) switch
		{
			SkillForgetAvailability.Available => CreateForgetAction(() => Game.CharacterService.ForgetSkill(skill)),
			SkillForgetAvailability.Unsupported => null,
			_ => CreateDisabledForgetAction(),
		};
	}

	private static DetailPanelAction CreateDisabledForgetAction() =>
		new("遗忘", false, () => Task.CompletedTask);

	private static DetailPanelAction CreateForgetAction(Func<bool> action)
	{
		ArgumentNullException.ThrowIfNull(action);
		return new DetailPanelAction(
			"遗忘",
			true,
			() =>
			{
				if (action())
				{
					Game.Audio.PlaySfx("音效.恢复2");
				}

				return Task.CompletedTask;
			});
	}
}
