using Game.Application.Formatters;
using Game.Core.Affix;
using Game.Core.Model;

namespace Game.Tests;

/// <summary>四系的属性名与技能系名语义不同，统一由 FormatterTextCn 给出。</summary>
public sealed class FourFamilyNameTests
{
    [Theory]
    [InlineData(WeaponType.Quanzhang, "拳掌系技能加成")]
    [InlineData(WeaponType.Jianfa, "剑系技能加成")]
    [InlineData(WeaponType.Daofa, "刀系技能加成")]
    [InlineData(WeaponType.Qimen, "奇门系技能加成")]
    public void WeaponBonusAffix_UsesSkillFamilyName(WeaponType weaponType, string expectedPrefix)
    {
        var text = AffixFormatter.FormatCn(
            new WeaponBonusModifierAffix(weaponType, ModifierValue.Add(0.1d)),
            TestContentFactory.CreateRepository());

        Assert.StartsWith(expectedPrefix, text);
    }

    [Theory]
    [InlineData(StatType.Quanzhang, "拳掌")]
    [InlineData(StatType.Jianfa, "剑法")]
    [InlineData(StatType.Daofa, "刀法")]
    [InlineData(StatType.Qimen, "奇门")]
    public void StatModifierAffix_UsesAttributeName(StatType stat, string expectedPrefix)
    {
        var text = AffixFormatter.FormatCn(new StatModifierAffix(stat, ModifierValue.Add(5)), TestContentFactory.CreateRepository());

        Assert.StartsWith(expectedPrefix, text);
        Assert.DoesNotContain("系", text);
    }

    [Fact]
    public void GlobalLegendChanceAffix_FormatsAsAllLegendSkills()
    {
        var text = AffixFormatter.FormatCn(
            new LegendSkillChanceModifierAffix(null, new ModifierValue(ModifierOp.PostAdd, 0.5d)),
            TestContentFactory.CreateRepository());

        Assert.StartsWith("全部奥义触发率", text);
    }
}
