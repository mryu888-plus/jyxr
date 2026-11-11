using Game.Core.Abstractions;

namespace Game.Core;

/// <summary>
/// legacy <c>ItemInstance.GenerateRandomTrigger()</c> 的抽样方式: 按配置顺序逐项以 <c>weight / totalWeight</c>
/// 的概率试投, 第一项命中即选中; 整轮都没命中就重来一轮。这不是标准轮盘: 越靠前的项实际概率越高,
/// 装备随机词条按原版分布掉落依赖这一点。
/// </summary>
public static class SequentialTrialSelector
{
    // 正常随机源下单轮命中概率 = 1 - ∏(1 - p_i) > 0, 几轮内就会结束; 上限只为防止固定随机源 (测试替身) 死循环.
    private const int MaxPasses = 4096;

    public static T Select<T>(
        IReadOnlyList<T> values,
        Func<T, int> weightSelector,
        IRandomService random)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(weightSelector);
        ArgumentNullException.ThrowIfNull(random);
        if (values.Count == 0)
        {
            throw new InvalidOperationException("Sequential trial selection requires at least one value.");
        }

        var totalWeight = 0;
        foreach (var value in values)
        {
            var weight = weightSelector(value);
            ArgumentOutOfRangeException.ThrowIfLessThan(weight, 1);
            totalWeight = checked(totalWeight + weight);
        }

        for (var pass = 0; pass < MaxPasses; pass++)
        {
            foreach (var value in values)
            {
                if (random.NextDouble() < (double)weightSelector(value) / totalWeight)
                {
                    return value;
                }
            }
        }

        return WeightedRandomSelector.Select(values, weightSelector, random);
    }
}
