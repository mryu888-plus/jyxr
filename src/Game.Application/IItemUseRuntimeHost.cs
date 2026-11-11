using Game.Core.Model.Character;
using Game.Core.Model.Skills;

namespace Game.Application;

/// <summary>物品使用中需要玩家额外选择的宿主交互。</summary>
public interface IItemUseRuntimeHost
{
    /// <summary>让玩家从候选中选择要遗忘的武学；取消时返回 null。</summary>
    ValueTask<SkillInstance?> SelectSkillToForgetAsync(
        CharacterInstance character,
        IReadOnlyList<SkillInstance> candidates,
        CancellationToken cancellationToken);
}
