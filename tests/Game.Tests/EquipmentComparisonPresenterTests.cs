using Game.Core.Affix;
using Game.Core.Definitions;
using Game.Core.Model;
using Game.Presentation.Items;

namespace Game.Tests;

public sealed class EquipmentComparisonPresenterTests
{
    [Fact]
    public void Compare_WithEquippedItem_ReportsStatDeltasAffixChangesAndRequiresConfirmation()
    {
        var oldSword = TestContentFactory.CreateEquipment("old_sword") with
        {
            Affixes =
            [
                new StatModifierAffix(StatType.Attack, ModifierValue.Add(10)),
                new StatModifierAffix(StatType.CritChance, ModifierValue.Add(0.1)),
            ],
        };
        var newSword = TestContentFactory.CreateEquipment("new_sword") with
        {
            Affixes =
            [
                new StatModifierAffix(StatType.Attack, ModifierValue.Add(15)),
                new StatModifierAffix(StatType.CritChance, ModifierValue.Add(0.1)),
                new StatModifierAffix(StatType.Lifesteal, ModifierValue.Add(0.05)),
            ],
        };
        var definition = TestContentFactory.CreateCharacterDefinition("hero");
        var factory = new EquipmentInstanceFactory();
        var hero = TestContentFactory.CreateCharacterInstance("hero", definition, factory);
        hero.AddEquipmentInstance(factory.Create(oldSword));
        var inventory = new Inventory();
        var entry = inventory.AddItem(newSword);

        var comparison = EquipmentComparisonPresenter.Compare(hero, entry);

        Assert.True(comparison.RequiresReplacementConfirmation);
        Assert.Equal("old_sword", comparison.Current?.Definition.Id);
        Assert.Equal(
            [(StatType.Attack, 5d), (StatType.Lifesteal, 0.05d)],
            comparison.StatDeltas.Select(static delta => (delta.Stat, Math.Round(delta.Delta, 4))));
        Assert.Equal(2, comparison.GainedAffixes.Count());
        Assert.Single(comparison.LostAffixes);
        Assert.Single(comparison.AffixLines, static line =>
            line.Change == EquipmentAffixChange.Kept && line.Affix is StatModifierAffix { Stat: StatType.CritChance });
    }

    [Fact]
    public void Compare_EmptySlot_DoesNotRequireConfirmationAndAllAffixesAreGained()
    {
        var ring = TestContentFactory.CreateEquipment("ring", EquipmentSlotType.Accessory) with
        {
            Affixes = [new StatModifierAffix(StatType.Defence, ModifierValue.Add(3))],
        };
        var hero = TestContentFactory.CreateCharacterInstance("hero", TestContentFactory.CreateCharacterDefinition("hero"));
        var entry = new Inventory().AddItem(ring);

        var comparison = EquipmentComparisonPresenter.Compare(hero, entry);

        Assert.False(comparison.RequiresReplacementConfirmation);
        Assert.Equal(EquipmentAffixChange.Gained, Assert.Single(comparison.AffixLines).Change);
        Assert.Equal(3d, Assert.Single(comparison.StatDeltas).Delta);
    }
}
