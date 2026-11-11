using Game.Application;
using Game.Core.Battle;
using Game.Core.Definitions;
using Game.Core.Definitions.Skills;
using Game.Core.Model;
using Game.Core.Model.Character;

namespace Game.Tests;

public sealed class PracticeBookServiceTests
{
    [Fact]
    public async Task UsingBook_EquipsItForPracticeAndReturnsPreviousBook()
    {
        var (session, hero, palmBook, swordBook) = CreateSession();
        session.InventoryService.AddItem(palmBook);
        session.InventoryService.AddItem(swordBook);

        var palmEntry = session.State.Inventory.GetStack(palmBook);
        Assert.True((await session.ItemUseService.UseAsync(palmEntry, "hero")).Success);
        Assert.Same(palmBook, hero.PracticeBook);
        Assert.False(session.State.Inventory.ContainsStack(palmBook));
        Assert.Null(hero.GetExternalSkillLevel("palm"));

        var swordEntry = session.State.Inventory.GetStack(swordBook);
        Assert.True((await session.ItemUseService.UseAsync(swordEntry, "hero")).Success);
        Assert.Same(swordBook, hero.PracticeBook);
        Assert.True(session.State.Inventory.ContainsStack(palmBook));

        Assert.Same(swordBook, session.PracticeBookService.Unequip("hero"));
        Assert.Null(hero.PracticeBook);
        Assert.True(session.State.Inventory.ContainsStack(swordBook));
    }

    [Fact]
    public void CharacterExperience_LearnsBookSkillThenTrainsItUntilBookLevel()
    {
        var (session, hero, palmBook, _) = CreateSession();
        hero.SetPracticeBook(palmBook);

        session.CharacterService.GainExperience("hero", 5);

        // 没学过: 先以 1 级学会, 再加 5 + 悟性(60)/30 = 7 点经验.
        var palm = hero.GetExternalSkills().Single(skill => skill.Id == "palm");
        Assert.Equal(1, palm.Level);
        Assert.Equal(7, palm.Exp);

        palm.Level = 3;
        palm.Exp = 0;
        session.CharacterService.GainExperience("hero", 5);
        Assert.Equal(0, palm.Exp); // 已超过经书等级 2, 不再加经验
    }

    [Fact]
    public void BattleSkillCasts_FeedThePracticeBookThroughTheEngineCallback()
    {
        var (session, hero, palmBook, _) = CreateSession();
        hero.SetPracticeBook(palmBook);
        var strike = TestContentFactory.CreateExternalSkill("strike", impactType: SkillImpactType.Single, impactSize: 0, castSize: 3);
        hero.SetExternalSkillState(strike, 1, 0, true);
        var unit = new BattleUnit("hero_unit", hero, 1, new GridPosition(0, 0));
        unit.ActionGauge = 100;
        var state = new BattleState(new BattleGrid(4, 4), [unit]);
        var gained = new List<int>();
        var engine = new BattleEngine(
            skillMaxLevelResolver: _ => 20,
            characterExperienceGained: (battleUnit, experience) =>
            {
                gained.Add(experience);
                session.PracticeBookService.ApplyExperience(battleUnit.Character, experience, notify: false);
            });
        engine.BeginAction(state, unit.Id);

        engine.CastSkill(state, unit.Id, hero.GetExternalSkills().Single(skill => skill.Id == "strike"), new GridPosition(1, 0));

        Assert.Equal([3], gained);
        Assert.Equal(3 + 2, hero.GetExternalSkills().Single(skill => skill.Id == "palm").Exp);
    }

    [Fact]
    public void SaveRecord_RoundTripsPracticeBookAndIgnoresRemovedItems()
    {
        var (session, hero, palmBook, _) = CreateSession();
        hero.SetPracticeBook(palmBook);

        var record = CharacterMapper.ToRecord(hero);
        var restored = CharacterMapper.FromRecord(record, session.ContentRepository);
        var orphan = CharacterMapper.FromRecord(record with { PracticeBookItemId = "removed_book" }, session.ContentRepository);

        Assert.Equal("palm_book", record.PracticeBookItemId);
        Assert.Same(palmBook, restored.PracticeBook);
        Assert.Null(orphan.PracticeBook);
    }

    [Fact]
    public void InstantMode_DisablesPractice()
    {
        var (session, hero, palmBook, _) = CreateSession(SkillBookMode.Instant);
        hero.SetPracticeBook(palmBook);

        Assert.False(session.PracticeBookService.IsPracticeBook(palmBook));
        Assert.Null(session.PracticeBookService.ApplyExperience(hero, 10, notify: false));
    }

    private static (GameSession Session, CharacterInstance Hero, ItemDefinition PalmBook, ItemDefinition SwordBook) CreateSession(
        SkillBookMode mode = SkillBookMode.Practice)
    {
        var palm = TestContentFactory.CreateExternalSkill("palm");
        var sword = TestContentFactory.CreateExternalSkill("sword");
        var palmBook = CreateBook("palm_book", "palm", 2);
        var swordBook = CreateBook("sword_book", "sword", 10);
        var heroDefinition = TestContentFactory.CreateCharacterDefinition(
            "hero",
            stats: new Dictionary<StatType, int> { [StatType.Wuxing] = 60, [StatType.MaxHp] = 100 });
        var state = new GameState();
        var hero = TestContentFactory.CreateCharacterInstance("hero", heroDefinition);
        state.Party.AddMember(hero);
        var session = new GameSession(
            state,
            TestContentFactory.CreateRepository(
                characters: [heroDefinition],
                externalSkills: [palm, sword],
                items: [palmBook, swordBook]),
            config: new GameConfig { SkillBookMode = mode });
        return (session, hero, palmBook, swordBook);
    }

    private static ItemDefinition CreateBook(string id, string skillId, int level) =>
        new NormalItemDefinition
        {
            Id = id,
            Name = id,
            Type = ItemType.SkillBook,
            ConsumeOnUse = false,
            UseEffects = [new GrantExternalSkillItemUseEffectDefinition(skillId, level)],
        };
}
