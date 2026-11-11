using System.Text.Json;
using Game.Core.Affix;
using Game.Core.Definitions;
using Game.Core.Model;
using Game.Core.Serialization;

namespace Game.Tests;

public sealed class InitialEquipmentEntryTests
{
    [Fact]
    public void Json_AcceptsIdShorthandAndObjectWithExtraAffixes()
    {
        var entries = JsonSerializer.Deserialize<InitialEquipmentEntryDefinition[]>(
            """
            [
              "sword",
              {"id": "ring", "affixes": [{"type": "stat_modifier", "stat": "bili", "value": {"op": "add", "delta": 5}}]}
            ]
            """,
            GameJson.Default)!;

        Assert.Equal("sword", entries[0].Id);
        Assert.Empty(entries[0].ExtraAffixes);
        Assert.Equal("ring", entries[1].Id);
        var affix = Assert.IsType<StatModifierAffix>(Assert.Single(entries[1].ExtraAffixes));
        Assert.Equal(StatType.Bili, affix.Stat);
    }

    [Fact]
    public void Json_RejectsUnknownEntryFields()
    {
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<InitialEquipmentEntryDefinition[]>(
            """[{"id": "ring", "refine": 3}]""",
            GameJson.Default));
    }

    [Fact]
    public void CreateInitial_PutsEntryAffixesOnTheEquipmentInstance()
    {
        var ring = TestContentFactory.CreateEquipment("ring", EquipmentSlotType.Accessory);
        var extra = new StatModifierAffix(StatType.Bili, ModifierValue.Add(5));
        var definition = TestContentFactory.CreateCharacterDefinition("hero", equipment: [ring]) with
        {
            Equipment = [new InitialEquipmentEntryDefinition(ring, [extra])],
        };

        var hero = TestContentFactory.CreateCharacterInstance("hero", definition);

        var instance = hero.GetEquipment(EquipmentSlotType.Accessory)!;
        Assert.Same(ring, instance.Definition);
        Assert.Equal([extra], instance.ExtraAffixes);
        Assert.Equal(5, hero.GetStat(StatType.Bili));
    }
}
