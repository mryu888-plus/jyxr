using Game.Core.Abstractions;

namespace Game.Core;

public static class WeightedRandomSelector
{
    public static T Select<T>(
        IReadOnlyList<T> values,
        Func<T, int> weightSelector,
        IRandomService random)
    {
        if (values.Count == 0)
        {
            throw new InvalidOperationException("Weighted random selection requires at least one value.");
        }

        var totalWeight = 0;
        foreach (var value in values)
        {
            var weight = weightSelector(value);
            ArgumentOutOfRangeException.ThrowIfLessThan(weight, 1);
            totalWeight = checked(totalWeight + weight);
        }

        var ticket = random.Next(0, totalWeight);
        foreach (var value in values)
        {
            var weight = weightSelector(value);
            if (ticket < weight)
            {
                return value;
            }

            ticket -= weight;
        }

        throw new InvalidOperationException("Weighted random selection did not resolve a value.");
    }
}
