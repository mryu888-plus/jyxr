namespace Game.Core.Definitions;

public sealed record ShopDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string? Music { get; init; }

    public string? Background { get; init; }

    public IReadOnlyList<ShopProductDefinition> Products { get; init; } = [];
}

public sealed record ShopProductDefinition
{
    public required string Id { get; init; }

    public required RewardDefinition Reward { get; init; }

    public int? MaxClaims { get; init; }

    public int? Price { get; init; }

    public int? PremiumPrice { get; init; }

    /// <summary>剧情锁定：条件为假时商品不上架，也不能购买。</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(Game.Core.Serialization.ParsedExpressionJsonConverter))]
    public ParsedExpression? When { get; init; }

    /// <summary>定期补货：限购商品从本期第一次购买起经过这么多天后，已购数量清零。需要 <see cref="MaxClaims"/>。</summary>
    public int? RestockDays { get; init; }
}
