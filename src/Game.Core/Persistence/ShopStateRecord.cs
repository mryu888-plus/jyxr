namespace Game.Core.Persistence;

/// <summary>商店存档。后两项为可选字段，旧存档没有时视为空。</summary>
public sealed record ShopStateRecord(
    IReadOnlyList<ShopPurchaseRecord>? Purchases = null,
    IReadOnlyList<ShopStockAdjustmentRecord>? StockAdjustments = null,
    IReadOnlyList<ShopRestockPeriodRecord>? RestockPeriods = null);

public sealed record ShopPurchaseRecord(
    string ShopId,
    string ProductId,
    int Quantity);

public sealed record ShopStockAdjustmentRecord(
    string ShopId,
    string ProductId,
    int Delta);

public sealed record ShopRestockPeriodRecord(
    string ShopId,
    string ProductId,
    int StartDay);
