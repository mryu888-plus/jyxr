using Game.Core;
using Game.Core.Abstractions;

namespace Game.Tests;

public sealed class SequentialTrialSelectorTests
{
    [Fact]
    public void Select_TriesInConfiguredOrderAndRetriesWholePassOnMiss()
    {
        var values = new[] { ("A", 1), ("B", 3) };

        // A 的概率 1/4, B 的概率 3/4.
        Assert.Equal("B", SequentialTrialSelector.Select(values, static value => value.Item2, new ScriptedRandom(0.5, 0.5)).Item1);
        Assert.Equal("A", SequentialTrialSelector.Select(values, static value => value.Item2, new ScriptedRandom(0.9, 0.9, 0.1)).Item1);
    }

    [Fact]
    public void Select_FavorsEarlierEntriesLikeLegacyDrops()
    {
        var values = new[] { ("A", 1), ("B", 3) };
        var random = new SeededRandom(20261112);
        var hits = 0;
        const int samples = 40000;
        for (var index = 0; index < samples; index++)
        {
            if (SequentialTrialSelector.Select(values, static value => value.Item2, random).Item1 == "A")
            {
                hits++;
            }
        }

        // 标准轮盘下 A = 0.25; 逐项试投 A = 0.25 / (1 - 0.75 * 0.25) ≈ 0.3077.
        Assert.InRange(hits / (double)samples, 0.295, 0.32);
    }

    private sealed class ScriptedRandom(params double[] values) : IRandomService
    {
        private int _index;
        public double NextDouble() => values[_index++];
        public int Next(int minInclusive, int maxExclusive) => minInclusive;
    }

    private sealed class SeededRandom(int seed) : IRandomService
    {
        private readonly Random _random = new(seed);
        public double NextDouble() => _random.NextDouble();
        public int Next(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);
    }
}
