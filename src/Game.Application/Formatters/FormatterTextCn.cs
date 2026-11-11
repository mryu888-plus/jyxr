using System.Globalization;
using Game.Core.Abstractions;
using Game.Core.Definitions;
using Game.Core.Model;

namespace Game.Application.Formatters;

internal static class FormatterTextCn
{
    public static string ResolveTalentName(string talentId, IContentRepository contentRepository)
        => contentRepository.TryGetTalent(talentId, out var talent)
            ? talent.Name
            : talentId;

    public static string ResolveExternalSkillName(string skillId, IContentRepository contentRepository)
        => contentRepository.TryGetExternalSkill(skillId, out var skill)
            ? skill.Name
            : skillId;

    public static string ResolveInternalSkillName(string skillId, IContentRepository contentRepository)
        => contentRepository.TryGetInternalSkill(skillId, out var skill)
            ? skill.Name
            : skillId;

    public static string ResolveSpecialSkillName(string skillId, IContentRepository contentRepository)
        => contentRepository.TryGetSpecialSkill(skillId, out var skill)
            ? skill.Name
            : skillId;

    public static string ResolveSkillName(string skillId, IContentRepository contentRepository)
    {
        if (contentRepository.TryGetExternalSkill(skillId, out var externalSkill))
        {
            return externalSkill.Name;
        }

        if (contentRepository.TryGetInternalSkill(skillId, out var internalSkill))
        {
            return internalSkill.Name;
        }

        if (contentRepository.TryGetSpecialSkill(skillId, out var specialSkill))
        {
            return specialSkill.Name;
        }

        var legendSkill = contentRepository.GetLegendSkills()
            .FirstOrDefault(definition =>
                string.Equals(definition.Id, skillId, StringComparison.Ordinal) ||
                string.Equals(definition.Name, skillId, StringComparison.Ordinal));

        return legendSkill?.Name ?? skillId;
    }

    public static string ResolveBuffName(string buffId, IContentRepository contentRepository)
        => contentRepository.TryGetBuff(buffId, out var buff)
            ? buff.Name
            : buffId;

    public static string GetStatNameCn(StatType statType) => StatCatalog.GetDisplayNameCn(statType);

    public static string GetGenderNameCn(CharacterGender gender) =>
        gender switch
        {
            CharacterGender.Male => "男性",
            CharacterGender.Female => "女性",
            CharacterGender.Neutral => "中性",
            CharacterGender.Animal => "动物",
            CharacterGender.Eunuch => "太监",
            _ => throw new ArgumentOutOfRangeException(nameof(gender), gender, null)
        };

    // 四系有两套语义不同的中文名，只在这里成对给出，其它地方不要再写本地映射：
    // - 属性名（拳掌 / 剑法 / 刀法 / 奇门）：角色的四系属性，来自 StatCatalog；武学的武器类型也用这一套显示。
    // - 技能系名（拳掌 / 剑 / 刀 / 奇门）：复刻 legacy "拳掌系 / 剑系 / 刀系 / 奇门系技能加成" 文案。

    /// <summary>武器类型的显示名，四系与对应属性同名。</summary>
    public static string GetWeaponTypeNameCn(WeaponType weaponType) =>
        weaponType switch
        {
            WeaponType.Quanzhang => GetStatNameCn(StatType.Quanzhang),
            WeaponType.Jianfa => GetStatNameCn(StatType.Jianfa),
            WeaponType.Daofa => GetStatNameCn(StatType.Daofa),
            WeaponType.Qimen => GetStatNameCn(StatType.Qimen),
            WeaponType.InternalSkill => "内功",
            WeaponType.Unknown => "未知",
            _ => throw new ArgumentOutOfRangeException(nameof(weaponType), weaponType, null)
        };

    /// <summary>技能系名，用于"X系技能加成"。</summary>
    public static string GetSkillFamilyNameCn(WeaponType weaponType) =>
        weaponType switch
        {
            WeaponType.Quanzhang => "拳掌",
            WeaponType.Jianfa => "剑",
            WeaponType.Daofa => "刀",
            WeaponType.Qimen => "奇门",
            WeaponType.InternalSkill => "内功",
            _ => throw new ArgumentOutOfRangeException(nameof(weaponType), weaponType, null)
        };

    public static string FormatNumber(double value) =>
        Math.Round(value, 6, MidpointRounding.AwayFromZero).ToString("0.######", CultureInfo.InvariantCulture);

    public static string FormatPercent(double ratio) => $"{FormatNumber(ratio * 100d)}%";
}
