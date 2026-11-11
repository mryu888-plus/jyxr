using Game.Core.Definitions;

namespace Game.Core.Affix;

/// <summary>
/// 天赋闭包与展开后的词条。
/// PossessedTalents：已学习或经词条授予的全部天赋（候选闭包），用于"是否拥有某天赋"的判断，被替换的天赋仍在其中；
/// ActiveTalents：其中未被其它天赋 ReplaceTalentIds 替换、实际贡献词条效果的天赋。
/// </summary>
public sealed class ResolvedAffixSet(
    IReadOnlyList<AffixDefinition> affixes,
    IReadOnlySet<TalentDefinition> possessedTalents,
    IReadOnlySet<TalentDefinition> activeTalents)
{
    public IReadOnlyList<AffixDefinition> Affixes { get; } = affixes;

    public IReadOnlySet<TalentDefinition> PossessedTalents { get; } = possessedTalents;

    public IReadOnlySet<TalentDefinition> ActiveTalents { get; } = activeTalents;
}
