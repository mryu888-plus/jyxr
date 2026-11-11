using Game.Core.Definitions;
using Game.Core.Model;

namespace Game.Application;

public sealed class ShopService
{
    private const decimal SellPriceRatio = 0.5m;
    private readonly GameSession _session;
    private readonly GameConditionExpressionService _conditions;

    public ShopService(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _conditions = new GameConditionExpressionService(session);
    }

    private GameState State => _session.State;

    public ShopView Open(string shopId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shopId);

        var shop = _session.ContentRepository.GetShop(shopId);
        foreach (var product in shop.Products)
        {
            ApplyRestock(shop.Id, product);
        }

        var products = shop.Products
            .Where(product => IsListed(product) && IsAvailable(product.Reward))
            .Select(product => CreateProductView(shop.Id, product))
            .ToList();

        return new ShopView(shop, products, State.Shop.GetBuybacks(shop.Id).ToArray());
    }

    /// <summary>离开商店：本次光顾的回购清单失效，并发布关闭事件供剧情/宿主回调。</summary>
    public void Close(string shopId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shopId);
        State.Shop.ClearBuybacks(shopId);
        _session.Events.Publish(new ShopClosedEvent(shopId));
    }

    /// <summary>剧情调整限购商品的库存（正数加货、负数减货），剩余数量不会低于 0。</summary>
    public void AdjustStock(string shopId, string productId, int delta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shopId);
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        var product = GetProduct(_session.ContentRepository.GetShop(shopId), productId);
        if (product.MaxClaims is null)
        {
            throw new InvalidOperationException(
                $"Shop '{shopId}' product '{productId}' has unlimited stock and cannot be adjusted.");
        }

        if (delta != 0)
        {
            State.Shop.AdjustStock(shopId, productId, delta);
        }
    }

    /// <summary>按卖出价买回本次光顾卖出的物品。</summary>
    public ShopTransactionResult Buyback(string shopId, long buybackNumber, int quantity = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shopId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        var entry = State.Shop.GetBuybacks(shopId).SingleOrDefault(candidate => candidate.Number == buybackNumber);
        if (entry is null)
        {
            return ShopTransactionResult.Failed("该物品已不可回购。");
        }

        if (quantity > entry.Quantity)
        {
            return ShopTransactionResult.Failed($"【{entry.Item.Name}】可回购数量不足。");
        }

        var totalPrice = checked(entry.UnitPrice * quantity);
        if (!State.Currency.CanSpendSilver(totalPrice))
        {
            return ShopTransactionResult.Failed("银两不足。");
        }

        State.Currency.SpendSilver(totalPrice);
        if (entry.Equipment is not null)
        {
            State.Inventory.AddEquipmentInstance(entry.Equipment);
        }
        else
        {
            State.Inventory.AddItem(entry.Item, quantity);
        }

        State.Shop.RemoveBuyback(shopId, buybackNumber, quantity);
        _session.Events.Publish(new InventoryChangedEvent());
        _session.Events.Publish(new CurrencyChangedEvent());
        return ShopTransactionResult.Succeeded(FormatTransactionMessage("买回", entry.Item.Name, quantity));
    }

    public ShopTransactionResult Buy(
        string shopId,
        string productId,
        int quantity = 1,
        ShopCurrencyKind? currencyKind = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shopId);
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);

        var shop = _session.ContentRepository.GetShop(shopId);
        var productDefinition = GetProduct(shop, productId);
        ApplyRestock(shop.Id, productDefinition);
        if (!IsListed(productDefinition))
        {
            return ShopTransactionResult.Failed($"【{_session.RewardGrantService.GetDisplayName(productDefinition.Reward)}】暂不出售。");
        }

        if (!IsAvailable(productDefinition.Reward))
        {
            return ShopTransactionResult.Failed($"【{_session.RewardGrantService.GetDisplayName(productDefinition.Reward)}】已达等级上限。");
        }

        var product = CreateProductView(shop.Id, productDefinition);
        var selectedCurrency = currencyKind ?? product.DefaultCurrencyKind;
        var unitPrice = product.GetUnitPrice(selectedCurrency);
        if (unitPrice is null)
        {
            throw new InvalidOperationException($"Shop product '{product.DisplayName}' cannot be bought with {selectedCurrency}.");
        }

        if (product.RemainingClaims is not null && quantity > product.RemainingClaims.Value)
        {
            return ShopTransactionResult.Failed($"【{product.DisplayName}】已达购买上限。");
        }

        var totalPrice = checked(unitPrice.Value * quantity);
        if (!CanSpend(selectedCurrency, totalPrice))
        {
            return ShopTransactionResult.Failed(selectedCurrency == ShopCurrencyKind.Silver ? "银两不足。" : "元宝不足。");
        }

        if (productDefinition.Reward is SkillMaxLevelRewardDefinition fragment &&
            checked(fragment.Quantity * quantity) >
            _session.RewardGrantService.GetRemainingSkillMaxLevelBonus(fragment.SkillKind, fragment.SkillId))
        {
            return ShopTransactionResult.Failed($"【{product.DisplayName}】购买数量超过剩余可提升等级。");
        }

        Spend(selectedCurrency, totalPrice);
        if (productDefinition.RestockDays is not null &&
            State.Shop.GetRestockPeriodStartDay(shop.Id, productDefinition.Id) is null)
        {
            State.Shop.BeginRestockPeriod(shop.Id, productDefinition.Id, State.Clock.TotalDays);
        }

        State.Shop.AddPurchasedQuantity(shop.Id, productDefinition.Id, quantity);
        if (selectedCurrency == ShopCurrencyKind.Silver)
        {
            _session.Events.Publish(new CurrencyChangedEvent());
        }

        _session.RewardGrantService.Apply(_session.RewardGrantService.Resolve(productDefinition.Reward, quantity));
        return ShopTransactionResult.Succeeded(FormatTransactionMessage("买入", product.DisplayName, quantity));
    }

    /// <summary>卖出背包物品。传入 <paramref name="shopId"/> 时记入该商店本次光顾的回购清单。</summary>
    public ShopTransactionResult Sell(InventoryEntry entry, int quantity = 1, string? shopId = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);

        if (!CanSell(entry.Definition))
        {
            return ShopTransactionResult.Failed($"【{entry.Definition.Name}】不能出售。");
        }

        var unitPrice = GetSellPrice(entry.Definition);

        switch (entry)
        {
            case StackInventoryEntry stack:
                if (quantity > stack.Quantity)
                {
                    return ShopTransactionResult.Failed($"【{entry.Definition.Name}】数量不足。");
                }

                State.Inventory.RemoveItem(stack.Definition, quantity);
                if (shopId is not null)
                {
                    State.Shop.AddBuyback(shopId, new ShopBuybackEntry(stack.Definition, quantity, unitPrice));
                }

                break;

            case EquipmentInstanceInventoryEntry equipment:
                if (quantity != 1)
                {
                    return ShopTransactionResult.Failed("独立装备一次只能出售 1 件。");
                }

                var removed = State.Inventory.RemoveEquipmentInstance(equipment.Equipment.Id);
                if (shopId is not null)
                {
                    State.Shop.AddBuyback(shopId, new ShopBuybackEntry(entry.Definition, 1, unitPrice, removed));
                }

                break;

            default:
                throw new InvalidOperationException($"Unsupported inventory entry type '{entry.GetType().Name}'.");
        }

        var totalPrice = checked(unitPrice * quantity);
        State.Currency.AddSilver(totalPrice);
        _session.Events.Publish(new InventoryChangedEvent());
        _session.Events.Publish(new CurrencyChangedEvent());
        return ShopTransactionResult.Succeeded(FormatTransactionMessage("卖出", entry.Definition.Name, quantity));
    }

    public int GetSellPrice(ItemDefinition item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Price <= 0 ? 0 : Math.Max(1, (int)Math.Floor(item.Price * SellPriceRatio));
    }

    public bool CanSell(ItemDefinition item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Price > 0;
    }

    private ShopProductView CreateProductView(string shopId, ShopProductDefinition product)
    {
        var purchasedQuantity = State.Shop.GetPurchasedQuantity(shopId, product.Id);
        int? remainingClaims = product.MaxClaims is null
            ? null
            : Math.Max(0, product.MaxClaims.Value + State.Shop.GetStockAdjustment(shopId, product.Id) - purchasedQuantity);
        int? restockInDays = product.RestockDays is { } restockDays &&
            State.Shop.GetRestockPeriodStartDay(shopId, product.Id) is { } periodStart
                ? Math.Max(1, periodStart + restockDays - State.Clock.TotalDays)
                : null;
        var item = product.Reward is ItemRewardDefinition itemReward
            ? _session.ContentRepository.GetItem(itemReward.ItemId)
            : null;
        int? price = product.PremiumPrice is not null && product.Price is null
            ? null
            : product.Price ?? item?.Price;
        var displayName = _session.RewardGrantService.GetDisplayName(product.Reward);
        var (picture, description) = ResolvePresentation(product.Reward, item);

        return new ShopProductView(
            product.Id,
            product,
            item,
            displayName,
            picture,
            description,
            product.Reward is not ItemRewardDefinition,
            price,
            product.PremiumPrice,
            purchasedQuantity,
            remainingClaims,
            restockInDays);
    }

    private static ShopProductDefinition GetProduct(ShopDefinition shop, string productId) =>
        shop.Products.SingleOrDefault(product => string.Equals(product.Id, productId, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"Shop '{shop.Id}' has no product '{productId}'.");

    private bool IsListed(ShopProductDefinition product) => _conditions.Evaluate(product.When);

    private void ApplyRestock(string shopId, ShopProductDefinition product)
    {
        if (product.RestockDays is not { } restockDays ||
            State.Shop.GetRestockPeriodStartDay(shopId, product.Id) is not { } periodStart)
        {
            return;
        }

        if (State.Clock.TotalDays - periodStart >= restockDays)
        {
            State.Shop.Restock(shopId, product.Id);
        }
    }

    private bool IsAvailable(RewardDefinition reward) =>
        reward is not SkillMaxLevelRewardDefinition fragment ||
        _session.RewardGrantService.GetRemainingSkillMaxLevelBonus(fragment.SkillKind, fragment.SkillId) > 0;

    private (string Picture, string Description) ResolvePresentation(
        RewardDefinition reward,
        ItemDefinition? item) =>
        reward switch
        {
            ItemRewardDefinition => (item!.Picture, item.Description),
            YuanbaoRewardDefinition yuanbao => (
                "物品.元宝",
                $"兑换 {yuanbao.Quantity} 枚跨存档、跨周目共享的元宝。"),
            SkillMaxLevelRewardDefinition fragment => (
                ResolveSkillIcon(fragment),
                $"立即永久提高【{ResolveSkillName(fragment)}】等级上限 {fragment.Quantity} 级。"),
            _ => throw new NotSupportedException($"Unsupported shop reward '{reward.GetType().Name}'."),
        };

    private string ResolveSkillName(SkillMaxLevelRewardDefinition fragment) =>
        fragment.SkillKind switch
        {
            SkillFragmentKind.External => _session.ContentRepository.GetExternalSkill(fragment.SkillId).Name,
            SkillFragmentKind.Internal => _session.ContentRepository.GetInternalSkill(fragment.SkillId).Name,
            _ => throw new ArgumentOutOfRangeException(nameof(fragment.SkillKind), fragment.SkillKind, null),
        };

    private string ResolveSkillIcon(SkillMaxLevelRewardDefinition fragment)
    {
        var icon = fragment.SkillKind switch
        {
            SkillFragmentKind.External => _session.ContentRepository.GetExternalSkill(fragment.SkillId).Icon,
            SkillFragmentKind.Internal => _session.ContentRepository.GetInternalSkill(fragment.SkillId).Icon,
            _ => throw new ArgumentOutOfRangeException(nameof(fragment.SkillKind), fragment.SkillKind, null),
        };
        return string.IsNullOrWhiteSpace(icon) ? "物品.剑谱" : icon;
    }

    private bool CanSpend(ShopCurrencyKind currencyKind, int amount) =>
        currencyKind switch
        {
            ShopCurrencyKind.Silver => State.Currency.CanSpendSilver(amount),
            ShopCurrencyKind.Gold => _session.ProfileService.CanSpendYuanbao(amount),
            _ => throw new ArgumentOutOfRangeException(nameof(currencyKind), currencyKind, null)
        };

    private void Spend(ShopCurrencyKind currencyKind, int amount)
    {
        switch (currencyKind)
        {
            case ShopCurrencyKind.Silver:
                State.Currency.SpendSilver(amount);
                return;
            case ShopCurrencyKind.Gold:
                _session.ProfileService.SpendYuanbao(amount);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(currencyKind), currencyKind, null);
        }
    }

    private static string FormatTransactionMessage(string verb, string itemName, int quantity) =>
        quantity == 1
            ? $"{verb}【{itemName}】"
            : $"{verb}【{itemName}】 x{quantity}";
}

public enum ShopCurrencyKind
{
    Silver,
    Gold,
}

public sealed record ShopView(
    ShopDefinition Definition,
    IReadOnlyList<ShopProductView> Products,
    IReadOnlyList<ShopBuybackEntry> Buybacks)
{
    /// <summary>只能用元宝购买的商品，界面单独分组展示。</summary>
    public IReadOnlyList<ShopProductView> PremiumProducts =>
        Products.Where(static product => product.Group == ShopProductGroup.Premium).ToArray();
}

public enum ShopProductGroup
{
    /// <summary>可用银两购买（可能同时支持元宝）。</summary>
    Silver,
    /// <summary>只能用元宝购买。</summary>
    Premium,
}

public sealed record ShopProductView(
    string ProductId,
    ShopProductDefinition Definition,
    ItemDefinition? Item,
    string DisplayName,
    string Picture,
    string Description,
    bool IsSpecial,
    int? Price,
    int? PremiumPrice,
    int PurchasedQuantity,
    int? RemainingClaims,
    int? RestockInDays = null)
{
    public ShopProductGroup Group => Price is null && PremiumPrice is not null
        ? ShopProductGroup.Premium
        : ShopProductGroup.Silver;

    public ShopCurrencyKind DefaultCurrencyKind =>
        Price is not null ? ShopCurrencyKind.Silver : ShopCurrencyKind.Gold;

    public int? GetUnitPrice(ShopCurrencyKind currencyKind) =>
        currencyKind switch
        {
            ShopCurrencyKind.Silver => Price,
            ShopCurrencyKind.Gold => PremiumPrice,
            _ => throw new ArgumentOutOfRangeException(nameof(currencyKind), currencyKind, null)
        };
}

public sealed record ShopTransactionResult(
    bool Success,
    string Message)
{
    public static ShopTransactionResult Succeeded(string message) => new(true, message);

    public static ShopTransactionResult Failed(string message) => new(false, message);
}
