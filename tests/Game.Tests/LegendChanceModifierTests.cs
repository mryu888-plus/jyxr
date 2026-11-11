using Game.Core.Affix;
using Game.Core.Definitions;

namespace Game.Tests;

public sealed class LegendChanceModifierTests
{
    [Fact]
    public void GlobalLegendChanceModifier_CombinesWithSkillSpecificModifiers()
    {
        var talent = new TalentDefinition
        {
            Id = "scholar",
            Name = "博览群书",
            Affixes =
            [
                new LegendSkillChanceModifierAffix(null, new ModifierValue(ModifierOp.PostAdd, 0.5d)),
                new LegendSkillChanceModifierAffix("dragon", ModifierValue.Increase(1d)),
            ],
        };
        var character = TestContentFactory.CreateCharacterInstance(
            "hero",
            TestContentFactory.CreateCharacterDefinition("hero", talents: [talent]));

        // 指定奥义：(1.2) * (1 + 1) + 0.5；其它奥义只吃全局后加。
        Assert.Equal(2.9d, character.GetLegendChanceValue("dragon", 1.2d), 6);
        Assert.Equal(1.7d, character.GetLegendChanceValue("tiger", 1.2d), 6);
    }

    [Fact]
    public void LegendChanceModifierWithoutSkillId_DeserializesAsGlobal()
    {
        var talent = System.Text.Json.JsonSerializer.Deserialize<TalentDefinition>(
            """
            {"id":"scholar","name":"博览群书","affixes":[
              {"type":"legend_skill_chance_modifier","value":{"op":"post_add","delta":0.5}}]}
            """,
            Game.Core.Serialization.GameJson.Default)!;

        var affix = Assert.IsType<LegendSkillChanceModifierAffix>(Assert.Single(talent.Affixes));
        Assert.Null(affix.SkillId);
        Assert.Equal(ModifierOp.PostAdd, affix.Value.Op);
    }
}
