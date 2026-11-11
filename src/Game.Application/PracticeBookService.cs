using Game.Core.Definitions;
using Game.Core.Model;
using Game.Core.Model.Character;
using Game.Core.Model.Skills;

namespace Game.Application;

/// <summary>
/// 武学经书修炼 (legacy 第四个装备位 + Role.AddExp)。经书装备在角色身上, 角色此后获得的每一笔人物经验
/// (战斗中出招、战斗结算、剧情给的经验) 都同时加到经书对应的武学上: 没学过的先以 1 级学会 (受外功/内功数量上限限制),
/// 只要武学当前等级不超过经书等级就继续涨, 额外加 悟性/30, 仍受武学精通上限约束。
/// <see cref="SkillBookMode.Instant"/> 下经书仍是"使用即学会到书本等级"的消耗方式, 本服务不生效。
/// </summary>
public sealed class PracticeBookService
{
    private readonly GameSession _session;

    public PracticeBookService(GameSession session) => _session = session;

    private GameConfig Config => _session.Config;

    public bool IsPracticeBook(ItemDefinition item) =>
        Config.SkillBookMode == SkillBookMode.Practice && TryGetBookSkill(item, out _, out _, out _);

    public static bool TryGetBookSkill(ItemDefinition item, out string skillId, out bool isInternal, out int? bookLevel)
    {
        ArgumentNullException.ThrowIfNull(item);
        skillId = string.Empty;
        isInternal = false;
        bookLevel = null;
        if (item.UseEffects is not [var effect])
        {
            return false;
        }

        switch (effect)
        {
            case GrantExternalSkillItemUseEffectDefinition external:
                (skillId, isInternal, bookLevel) = (external.SkillId, false, external.Level);
                return true;
            case GrantInternalSkillItemUseEffectDefinition internalSkill:
                (skillId, isInternal, bookLevel) = (internalSkill.SkillId, true, internalSkill.Level);
                return true;
            default:
                return false;
        }
    }

    /// <summary>把背包里的一本经书装备给角色修炼; 原来修炼的经书放回背包。</summary>
    public ItemUseResult Equip(CharacterInstance character, ItemDefinition book)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(book);
        if (!IsPracticeBook(book))
        {
            return ItemUseResult.Failed($"【{book.Name}】不是可修炼的武学经书。");
        }

        _session.InventoryService.RemoveItem(book);
        if (character.SetPracticeBook(book) is { } previous)
        {
            _session.InventoryService.AddItem(previous);
        }

        _session.Events.Publish(new CharacterChangedEvent(character.Id));
        return ItemUseResult.Succeeded($"【{character.Name}】开始修炼【{book.Name}】");
    }

    public ItemDefinition? Unequip(string characterId)
    {
        var character = _session.State.Party.TryGetCharacter(characterId, out var found)
            ? found
            : throw new InvalidOperationException($"Character '{characterId}' is not in the party roster.");
        var previous = character.SetPracticeBook(null);
        if (previous is not null)
        {
            _session.InventoryService.AddItem(previous);
            _session.Events.Publish(new CharacterChangedEvent(character.Id));
        }

        return previous;
    }

    /// <summary>角色获得人物经验时调用。返回经书武学的升级结果 (没有修炼或没加上经验时为 null)。</summary>
    public SkillExperienceChange? ApplyExperience(CharacterInstance character, int experience, bool notify)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (experience <= 0 ||
            Config.SkillBookMode != SkillBookMode.Practice ||
            character.PracticeBook is not { } book ||
            !TryGetBookSkill(book, out var skillId, out var isInternal, out var bookLevel))
        {
            return null;
        }

        var skill = isInternal
            ? (SkillInstance?)character.GetInternalSkills().FirstOrDefault(candidate => candidate.Id == skillId)
            : character.GetExternalSkills().FirstOrDefault(candidate => candidate.Id == skillId);
        var learned = false;
        if (skill is null)
        {
            skill = TryLearn(character, skillId, isInternal);
            if (skill is null)
            {
                return null;
            }

            learned = true;
        }

        var cap = Config.IgnoreSkillBookLevelLimit || bookLevel is null ? int.MaxValue : bookLevel.Value;
        if (skill.Level > cap)
        {
            return null;
        }

        var wuxingBonus = (int)Math.Floor(character.GetStat(StatType.Wuxing) / 30d);
        var change = SkillExperienceProgression.TryAddExperience(
            skill,
            checked(experience + wuxingBonus),
            _session.SkillMaxLevelPolicy.GetMaxLevel(skill));
        if (notify && (learned || change is { LeveledUp: true }))
        {
            var text = learned && change is not { LeveledUp: true }
                ? $"{character.Name} 修炼【{book.Name}】领悟了【{skill.Name}】"
                : $"{character.Name} 修炼【{skill.Name}】提升到 {skill.Level} 级";
            _session.Events.Publish(new ToastRequestedEvent(text, ToastTone.Important, "音效.升级"));
            _session.Events.Publish(new CharacterChangedEvent(character.Id));
        }

        return change;
    }

    private SkillInstance? TryLearn(CharacterInstance character, string skillId, bool isInternal)
    {
        if (isInternal)
        {
            if (character.GetInternalSkills().Count >= Config.MaxInternalSkillCount)
            {
                return null;
            }

            character.SetInternalSkillState(_session.ContentRepository.GetInternalSkill(skillId), 1, 0);
            return character.GetInternalSkills().First(candidate => candidate.Id == skillId);
        }

        if (character.GetExternalSkills().Count >= Config.MaxExternalSkillCount)
        {
            return null;
        }

        character.SetExternalSkillState(_session.ContentRepository.GetExternalSkill(skillId), 1, 0, true);
        return character.GetExternalSkills().First(candidate => candidate.Id == skillId);
    }
}
