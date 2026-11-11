using System.Text.Json;
using Game.Application;
using Game.Core.Definitions;
using Game.Core.Definitions.Skills;
using Game.Core.Model;
using Game.Core.Persistence;
using Game.Core.Serialization;

namespace Game.Tests;

public sealed class ShopServiceTests
{
    [Fact]
    public void Buy_SpendsSilverAddsItemAndTracksMaxClaims()
    {
        var herb = CreateItem("herb", price: 30);
        var shop = CreateShop("village_shop", new ShopProductDefinition
        {
            Id = "herb",
            Reward = new ItemRewardDefinition { ItemId = herb.Id },
            MaxClaims = 2,
            Price = 40,
        });
        var session = CreateSession([herb], [shop], silver: 100);
        var publishedEvents = CollectPublishedEvents(session);

        var result = session.ShopService.Buy(shop.Id, "herb");

        Assert.True(result.Success);
        Assert.Equal("买入【herb】", result.Message);
        Assert.Equal(60, session.State.Currency.Silver);
        Assert.True(session.State.Inventory.ContainsStack(herb));
        Assert.Equal(1, session.State.Shop.PurchasedQuantities.Single().Value);
        Assert.Contains(publishedEvents, static sessionEvent => sessionEvent is CurrencyChangedEvent);
        Assert.Contains(publishedEvents, static sessionEvent => sessionEvent is InventoryChangedEvent);
        Assert.Contains(
            publishedEvents,
            static sessionEvent => sessionEvent is ItemAcquiredEvent { ItemId: "herb", Quantity: 1 });
    }

    [Fact]
    public void Buy_RejectsInsufficientCurrencyWithoutChangingState()
    {
        var herb = CreateItem("herb", price: 30);
        var shop = CreateShop("village_shop", new ShopProductDefinition
        {
            Id = "herb",
            Reward = new ItemRewardDefinition { ItemId = herb.Id },
            Price = 40,
        });
        var session = CreateSession([herb], [shop], silver: 20);

        var result = session.ShopService.Buy(shop.Id, "herb");

        Assert.False(result.Success);
        Assert.Equal("银两不足。", result.Message);
        Assert.Equal(20, session.State.Currency.Silver);
        Assert.Empty(session.State.Inventory.Entries);
        Assert.Empty(session.State.Shop.PurchasedQuantities);
    }

    [Fact]
    public void Buy_PremiumProductSpendsYuanbaoAndDoesNotPublishCurrencyChangedEvent()
    {
        var herb = CreateItem("herb", price: 30);
        var shop = CreateShop("premium_shop", new ShopProductDefinition
        {
            Id = "herb",
            Reward = new ItemRewardDefinition { ItemId = herb.Id },
            PremiumPrice = 2,
        });
        var session = CreateSession([herb], [shop], silver: 100, yuanbao: 5);
        var publishedEvents = CollectPublishedEvents(session);

        var result = session.ShopService.Buy(shop.Id, "herb");

        Assert.True(result.Success);
        Assert.Equal(3, session.Profile.Yuanbao);
        Assert.Equal(100, session.State.Currency.Silver);
        Assert.True(session.State.Inventory.ContainsStack(herb));
        Assert.Contains(publishedEvents, static sessionEvent => sessionEvent is ProfileChangedEvent);
        Assert.DoesNotContain(publishedEvents, static sessionEvent => sessionEvent is CurrencyChangedEvent);
    }

    [Fact]
    public void Buy_RejectsSoldOutLimitedProduct()
    {
        var herb = CreateItem("herb", price: 30);
        var shop = CreateShop("village_shop", new ShopProductDefinition
        {
            Id = "herb",
            Reward = new ItemRewardDefinition { ItemId = herb.Id },
            MaxClaims = 1,
            Price = 40,
        });
        var session = CreateSession([herb], [shop], silver: 100);

        Assert.True(session.ShopService.Buy(shop.Id, "herb").Success);
        var result = session.ShopService.Buy(shop.Id, "herb");

        Assert.False(result.Success);
        Assert.Equal(60, session.State.Currency.Silver);
        Assert.Equal(1, session.State.Inventory.GetStack(herb).Quantity);
    }

    [Fact]
    public void Buy_MultipliesRewardQuantityByClaimCount()
    {
        var herb = CreateItem("herb", price: 30);
        var shop = CreateShop("village_shop", new ShopProductDefinition
        {
            Id = "herb",
            Reward = new ItemRewardDefinition { ItemId = herb.Id, Quantity = 3 },
            MaxClaims = 2,
            Price = 10,
        });
        var session = CreateSession([herb], [shop], silver: 100);

        var result = session.ShopService.Buy(shop.Id, "herb", quantity: 2);
        var rejected = session.ShopService.Buy(shop.Id, "herb");

        Assert.True(result.Success);
        Assert.Equal(6, session.State.Inventory.GetStack(herb).Quantity);
        Assert.Equal(2, session.State.Shop.PurchasedQuantities.Single().Value);
        Assert.Equal(80, session.State.Currency.Silver);
        Assert.False(rejected.Success);
    }

    [Fact]
    public void Buy_TracksProductsWithTheSameRewardIndependentlyById()
    {
        var herb = CreateItem("herb", price: 30);
        var shop = CreateShop(
            "village_shop",
            new ShopProductDefinition
            {
                Id = "daily_herb",
                Reward = new ItemRewardDefinition { ItemId = herb.Id },
                MaxClaims = 1,
                Price = 10,
            },
            new ShopProductDefinition
            {
                Id = "special_herb",
                Reward = new ItemRewardDefinition { ItemId = herb.Id },
                MaxClaims = 1,
                Price = 20,
            });
        var session = CreateSession([herb], [shop], silver: 100);

        Assert.True(session.ShopService.Buy(shop.Id, "daily_herb").Success);
        Assert.True(session.ShopService.Buy(shop.Id, "special_herb").Success);

        Assert.Equal(1, session.State.Shop.GetPurchasedQuantity(shop.Id, "daily_herb"));
        Assert.Equal(1, session.State.Shop.GetPurchasedQuantity(shop.Id, "special_herb"));
        Assert.Equal(2, session.State.Inventory.GetStack(herb).Quantity);
    }

    [Fact]
    public void Open_PresentsItemAndSpecialRewardProducts()
    {
        var herb = CreateItem("herb", price: 30);
        var externalSkill = TestContentFactory.CreateExternalSkill("dragon_palm");
        var shop = CreateShop(
            "village_shop",
            new ShopProductDefinition
            {
                Id = "yuanbao",
                Reward = new YuanbaoRewardDefinition(),
                Price = 1000,
            },
            new ShopProductDefinition
            {
                Id = "dragon_palm_fragment",
                Reward = new SkillMaxLevelRewardDefinition
                {
                    SkillKind = SkillFragmentKind.External,
                    SkillId = externalSkill.Id,
                },
                PremiumPrice = 4,
            },
            new ShopProductDefinition
            {
                Id = "herb",
                Reward = new ItemRewardDefinition { ItemId = herb.Id },
                Price = 40,
            });
        var session = CreateSession([herb], [shop], silver: 100, externalSkills: [externalSkill]);

        var view = session.ShopService.Open(shop.Id);

        Assert.Collection(
            view.Products,
            product =>
            {
                Assert.Equal("yuanbao", product.ProductId);
                Assert.Equal("元宝", product.DisplayName);
                Assert.True(product.IsSpecial);
            },
            product =>
            {
                Assert.Equal("dragon_palm_fragment", product.ProductId);
                Assert.Equal("dragon_palm残章", product.DisplayName);
                Assert.True(product.IsSpecial);
            },
            product =>
            {
                Assert.Equal("herb", product.ProductId);
                Assert.Equal("herb", product.Item!.Id);
                Assert.False(product.IsSpecial);
            });
    }

    [Fact]
    public void Sell_RemovesInventoryEntryAndAddsSilver()
    {
        var herb = CreateItem("herb", price: 30);
        var shop = CreateShop("village_shop", new ShopProductDefinition
        {
            Id = "herb",
            Reward = new ItemRewardDefinition { ItemId = herb.Id },
            Price = 40,
        });
        var session = CreateSession([herb], [shop], silver: 0);
        var entry = session.State.Inventory.AddItem(herb, 2);
        var publishedEvents = CollectPublishedEvents(session);

        var result = session.ShopService.Sell(entry);

        Assert.True(result.Success);
        Assert.Equal("卖出【herb】", result.Message);
        Assert.Equal(15, session.State.Currency.Silver);
        Assert.Equal(1, session.State.Inventory.GetStack(herb).Quantity);
        Assert.Contains(publishedEvents, static sessionEvent => sessionEvent is CurrencyChangedEvent);
        Assert.Contains(publishedEvents, static sessionEvent => sessionEvent is InventoryChangedEvent);
    }

    [Fact]
    public void TransactionMessages_IncludeQuantityOnlyWhenGreaterThanOne()
    {
        var herb = CreateItem("herb", price: 30);
        var shop = CreateShop("village_shop", new ShopProductDefinition
        {
            Id = "herb",
            Reward = new ItemRewardDefinition { ItemId = herb.Id },
            Price = 40,
        });
        var session = CreateSession([herb], [shop], silver: 100);

        var buyResult = session.ShopService.Buy(shop.Id, "herb", quantity: 2);
        var entry = session.State.Inventory.GetStack(herb);
        var sellResult = session.ShopService.Sell(entry, quantity: 2);

        Assert.Equal("买入【herb】 x2", buyResult.Message);
        Assert.Equal("卖出【herb】 x2", sellResult.Message);
    }

    [Fact]
    public void SaveGame_RoundTripsShopPurchaseState()
    {
        var herb = CreateItem("herb", price: 30);
        var shop = CreateShop("village_shop", new ShopProductDefinition
        {
            Id = "herb",
            Reward = new ItemRewardDefinition { ItemId = herb.Id },
            MaxClaims = 2,
            Price = 40,
        });
        var session = CreateSession([herb], [shop], silver: 100);
        session.ShopService.Buy(shop.Id, "herb");

        var saveGame = session.SaveGameService.CreateSave();
        var json = JsonSerializer.Serialize(saveGame, GameJson.Default);
        var roundTripped = JsonSerializer.Deserialize<SaveGame>(json, GameJson.Default);

        Assert.NotNull(roundTripped);
        var restoredShop = roundTripped!.RestoreShopState();
        Assert.Equal(1, restoredShop.GetPurchasedQuantity("village_shop", "herb"));
        var purchase = Assert.Single(roundTripped.ShopState!.Purchases!);
        Assert.Equal(new ShopPurchaseRecord("village_shop", "herb", 1), purchase);
    }

    [Fact]
    public void Open_HidesStoryLockedProductUntilConditionHolds()
    {
        var herb = CreateItem("herb", price: 30);
        var shop = CreateShop("village_shop", new ShopProductDefinition
        {
            Id = "herb",
            Reward = new ItemRewardDefinition { ItemId = herb.Id },
            Price = 40,
            When = new Game.Expressions.ExpressionParser().ParseExpression("has_flag('shop_unlocked')"),
        });
        var session = CreateSession([herb], [shop], silver: 100);

        Assert.Empty(session.ShopService.Open(shop.Id).Products);
        Assert.False(session.ShopService.Buy(shop.Id, "herb").Success);

        session.State.Story.SetVariable("shop_unlocked", Game.Expressions.ExpressionValue.FromBoolean(true));

        Assert.Single(session.ShopService.Open(shop.Id).Products);
        Assert.True(session.ShopService.Buy(shop.Id, "herb").Success);
    }

    [Fact]
    public void Restock_ResetsPurchasedQuantityAfterConfiguredDays()
    {
        var herb = CreateItem("herb", price: 30);
        var shop = CreateShop("village_shop", new ShopProductDefinition
        {
            Id = "herb",
            Reward = new ItemRewardDefinition { ItemId = herb.Id },
            Price = 1,
            MaxClaims = 1,
            RestockDays = 3,
        });
        var session = CreateSession([herb], [shop], silver: 100);

        Assert.True(session.ShopService.Buy(shop.Id, "herb").Success);
        var soldOut = Assert.Single(session.ShopService.Open(shop.Id).Products);
        Assert.Equal(0, soldOut.RemainingClaims);
        Assert.Equal(3, soldOut.RestockInDays);

        session.State.Clock.AdvanceDays(2);
        Assert.Equal(0, Assert.Single(session.ShopService.Open(shop.Id).Products).RemainingClaims);

        session.State.Clock.AdvanceDays(1);
        var restocked = Assert.Single(session.ShopService.Open(shop.Id).Products);
        Assert.Equal(1, restocked.RemainingClaims);
        Assert.Null(restocked.RestockInDays);
    }

    [Fact]
    public void AdjustStock_ChangesRemainingClaimsAndPersists()
    {
        var herb = CreateItem("herb", price: 30);
        var shop = CreateShop("village_shop",
            new ShopProductDefinition
            {
                Id = "herb",
                Reward = new ItemRewardDefinition { ItemId = herb.Id },
                Price = 1,
                MaxClaims = 1,
            },
            new ShopProductDefinition
            {
                Id = "unlimited",
                Reward = new ItemRewardDefinition { ItemId = herb.Id },
                Price = 1,
            });
        var session = CreateSession([herb], [shop], silver: 100);

        session.ShopService.AdjustStock(shop.Id, "herb", 2);

        Assert.Equal(3, session.ShopService.Open(shop.Id).Products.Single(product => product.ProductId == "herb").RemainingClaims);
        Assert.Throws<InvalidOperationException>(() => session.ShopService.AdjustStock(shop.Id, "unlimited", 1));
        var restored = Game.Core.Model.ShopState.Restore(session.State.Shop.ToRecord());
        Assert.Equal(2, restored.GetStockAdjustment(shop.Id, "herb"));
    }

    [Fact]
    public void Restore_OldRecordWithoutNewFieldsLoads()
    {
        var restored = Game.Core.Model.ShopState.Restore(
            JsonSerializer.Deserialize<ShopStateRecord>(
                """{"purchases":[{"shopId":"s","productId":"p","quantity":2}]}""",
                GameJson.Default));

        Assert.Equal(2, restored.GetPurchasedQuantity("s", "p"));
        Assert.Empty(restored.StockAdjustments);
        Assert.Empty(restored.RestockPeriodStartDays);
    }

    [Fact]
    public void Buyback_RestoresSoldItemAtSellPriceUntilShopCloses()
    {
        var herb = CreateItem("herb", price: 30);
        var shop = CreateShop("village_shop");
        var session = CreateSession([herb], [shop], silver: 0);
        session.State.Inventory.AddItem(herb, 3);
        var entry = session.State.Inventory.GetStack(herb);

        Assert.True(session.ShopService.Sell(entry, 2, shop.Id).Success);
        Assert.Equal(30, session.State.Currency.Silver);
        var buyback = Assert.Single(session.ShopService.Open(shop.Id).Buybacks);
        Assert.Equal(2, buyback.Quantity);
        Assert.Equal(15, buyback.UnitPrice);

        var result = session.ShopService.Buyback(shop.Id, buyback.Number);

        Assert.True(result.Success);
        Assert.Equal(15, session.State.Currency.Silver);
        Assert.Equal(2, session.State.Inventory.GetStack(herb).Quantity);
        Assert.Equal(1, Assert.Single(session.ShopService.Open(shop.Id).Buybacks).Quantity);

        var published = CollectPublishedEvents(session);
        session.ShopService.Close(shop.Id);

        Assert.Empty(session.ShopService.Open(shop.Id).Buybacks);
        Assert.Contains(published, static sessionEvent => sessionEvent is ShopClosedEvent { ShopId: "village_shop" });
    }

    [Fact]
    public void ShopView_GroupsPremiumOnlyProducts()
    {
        var herb = CreateItem("herb", price: 30);
        var shop = CreateShop("premium_shop",
            new ShopProductDefinition { Id = "silver", Reward = new ItemRewardDefinition { ItemId = herb.Id }, Price = 1 },
            new ShopProductDefinition { Id = "premium", Reward = new ItemRewardDefinition { ItemId = herb.Id }, PremiumPrice = 2 });
        var session = CreateSession([herb], [shop], silver: 0);

        var view = session.ShopService.Open(shop.Id);

        Assert.Equal(["premium"], view.PremiumProducts.Select(static product => product.ProductId));
        Assert.Equal(ShopProductGroup.Silver, view.Products.Single(product => product.ProductId == "silver").Group);
    }

    private static GameSession CreateSession(
        IReadOnlyList<ItemDefinition> items,
        IReadOnlyList<ShopDefinition> shops,
        int silver,
        int yuanbao = 0,
        IReadOnlyList<ExternalSkillDefinition>? externalSkills = null)
    {
        var repository = TestContentFactory.CreateRepository(
            items: items,
            externalSkills: externalSkills,
            shops: shops);
        var state = new GameState();
        state.Currency.AddSilver(silver);
        var profile = new GameProfile();
        profile.SetYuanbao(yuanbao);
        return new GameSession(state, repository, initialProfile: profile);
    }

    private static NormalItemDefinition CreateItem(string id, int price) =>
        new()
        {
            Id = id,
            Name = id,
            Type = ItemType.Consumable,
            ConsumeOnUse = true,
            Price = price,
            CanDrop = true,
        };

    private static ShopDefinition CreateShop(string id, params ShopProductDefinition[] products) =>
        new()
        {
            Id = id,
            Name = id,
            Products = products,
        };

    private static List<object> CollectPublishedEvents(GameSession session)
    {
        var publishedEvents = new List<object>();
        session.Events.SubscribeAll(publishedEvents.Add);
        return publishedEvents;
    }
}
