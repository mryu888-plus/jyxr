using Game.Core.Definitions;
using Game.Core.Persistence;

namespace Game.Core.Model;

public sealed class ShopState
{
    private readonly Dictionary<ShopProductKey, int> _purchasedQuantities = [];
    private readonly Dictionary<ShopProductKey, int> _stockAdjustments = [];
    private readonly Dictionary<ShopProductKey, int> _restockPeriodStartDays = [];
    private readonly Dictionary<string, List<ShopBuybackEntry>> _buybacks = new(StringComparer.Ordinal);
    private long _nextBuybackNumber = 1;

    public IReadOnlyDictionary<ShopProductKey, int> PurchasedQuantities => _purchasedQuantities;

    /// <summary>剧情调整过的库存（正数加货、负数减货），叠加在商品定义的 maxClaims 上，可存档。</summary>
    public IReadOnlyDictionary<ShopProductKey, int> StockAdjustments => _stockAdjustments;

    /// <summary>定期补货商品的本期起始日（ClockState.TotalDays），可存档。</summary>
    public IReadOnlyDictionary<ShopProductKey, int> RestockPeriodStartDays => _restockPeriodStartDays;

    public static ShopState Restore(ShopStateRecord? record)
    {
        var state = new ShopState();
        if (record is null)
        {
            return state;
        }

        foreach (var purchase in record.Purchases ?? [])
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(purchase.ShopId);
            ArgumentException.ThrowIfNullOrWhiteSpace(purchase.ProductId);
            ArgumentOutOfRangeException.ThrowIfNegative(purchase.Quantity);
            if (purchase.Quantity > 0)
            {
                state._purchasedQuantities.Add(
                    new ShopProductKey(purchase.ShopId, purchase.ProductId),
                    purchase.Quantity);
            }
        }

        foreach (var adjustment in record.StockAdjustments ?? [])
        {
            if (adjustment.Delta != 0)
            {
                state._stockAdjustments.Add(CreateKey(adjustment.ShopId, adjustment.ProductId), adjustment.Delta);
            }
        }

        foreach (var period in record.RestockPeriods ?? [])
        {
            ArgumentOutOfRangeException.ThrowIfNegative(period.StartDay);
            state._restockPeriodStartDays.Add(CreateKey(period.ShopId, period.ProductId), period.StartDay);
        }

        return state;
    }

    public int GetPurchasedQuantity(string shopId, string productId)
    {
        var key = CreateKey(shopId, productId);
        return _purchasedQuantities.GetValueOrDefault(key);
    }

    public void AddPurchasedQuantity(string shopId, string productId, int quantity)
    {
        var key = CreateKey(shopId, productId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        _purchasedQuantities[key] = checked(GetPurchasedQuantity(shopId, productId) + quantity);
    }

    public int GetStockAdjustment(string shopId, string productId) =>
        _stockAdjustments.GetValueOrDefault(CreateKey(shopId, productId));

    public void AdjustStock(string shopId, string productId, int delta)
    {
        var key = CreateKey(shopId, productId);
        var value = checked(_stockAdjustments.GetValueOrDefault(key) + delta);
        if (value == 0)
        {
            _stockAdjustments.Remove(key);
        }
        else
        {
            _stockAdjustments[key] = value;
        }
    }

    public int? GetRestockPeriodStartDay(string shopId, string productId) =>
        _restockPeriodStartDays.TryGetValue(CreateKey(shopId, productId), out var day) ? day : null;

    public void BeginRestockPeriod(string shopId, string productId, int startDay)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startDay);
        _restockPeriodStartDays[CreateKey(shopId, productId)] = startDay;
    }

    /// <summary>补货：清空本期已购数量并结束本期，下次购买重新开始计时。</summary>
    public void Restock(string shopId, string productId)
    {
        var key = CreateKey(shopId, productId);
        _purchasedQuantities.Remove(key);
        _restockPeriodStartDays.Remove(key);
    }

    /// <summary>本次光顾期间卖出的物品，可按卖出价买回；关店或读档后失效，不存档。</summary>
    public IReadOnlyList<ShopBuybackEntry> GetBuybacks(string shopId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shopId);
        return _buybacks.TryGetValue(shopId, out var entries) ? entries : [];
    }

    public ShopBuybackEntry AddBuyback(string shopId, ShopBuybackEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shopId);
        ArgumentNullException.ThrowIfNull(entry);
        if (!_buybacks.TryGetValue(shopId, out var entries))
        {
            entries = [];
            _buybacks.Add(shopId, entries);
        }

        var numbered = entry with { Number = _nextBuybackNumber++ };
        entries.Add(numbered);
        return numbered;
    }

    public void RemoveBuyback(string shopId, long number, int quantity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shopId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        var entries = _buybacks.TryGetValue(shopId, out var list) ? list : [];
        var index = entries.FindIndex(entry => entry.Number == number);
        if (index < 0)
        {
            throw new InvalidOperationException($"Shop '{shopId}' has no buyback entry {number}.");
        }

        var entry = entries[index];
        if (quantity > entry.Quantity)
        {
            throw new InvalidOperationException($"Buyback entry {number} only has {entry.Quantity} item(s).");
        }

        if (quantity == entry.Quantity)
        {
            entries.RemoveAt(index);
        }
        else
        {
            entries[index] = entry with { Quantity = entry.Quantity - quantity };
        }
    }

    public void ClearBuybacks(string shopId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shopId);
        _buybacks.Remove(shopId);
    }

    public ShopStateRecord ToRecord() =>
        new(
            _purchasedQuantities
                .OrderBy(static entry => entry.Key.ShopId, StringComparer.Ordinal)
                .ThenBy(static entry => entry.Key.ProductId, StringComparer.Ordinal)
                .Select(static entry => new ShopPurchaseRecord(
                    entry.Key.ShopId,
                    entry.Key.ProductId,
                    entry.Value))
                .ToArray(),
            _stockAdjustments.Count == 0
                ? null
                : _stockAdjustments
                    .OrderBy(static entry => entry.Key.ShopId, StringComparer.Ordinal)
                    .ThenBy(static entry => entry.Key.ProductId, StringComparer.Ordinal)
                    .Select(static entry => new ShopStockAdjustmentRecord(entry.Key.ShopId, entry.Key.ProductId, entry.Value))
                    .ToArray(),
            _restockPeriodStartDays.Count == 0
                ? null
                : _restockPeriodStartDays
                    .OrderBy(static entry => entry.Key.ShopId, StringComparer.Ordinal)
                    .ThenBy(static entry => entry.Key.ProductId, StringComparer.Ordinal)
                    .Select(static entry => new ShopRestockPeriodRecord(entry.Key.ShopId, entry.Key.ProductId, entry.Value))
                    .ToArray());

    private static ShopProductKey CreateKey(string shopId, string productId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shopId);
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        return new ShopProductKey(shopId, productId);
    }
}

public readonly record struct ShopProductKey(string ShopId, string ProductId);

/// <summary>
/// 可回购的卖出记录。堆叠物品保存定义和数量；独立装备保存原实例（锻造、随机词条原样回到背包）。
/// </summary>
public sealed record ShopBuybackEntry(
    ItemDefinition Item,
    int Quantity,
    int UnitPrice,
    EquipmentInstance? Equipment = null,
    long Number = 0);
