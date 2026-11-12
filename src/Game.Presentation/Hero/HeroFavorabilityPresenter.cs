using Game.Core.Abstractions;
using Game.Core.Model;

namespace Game.Presentation.Hero;

public sealed record FavorabilityView(string TargetId, string DisplayName, int Value);

public static class HeroFavorabilityPresenter
{
    public static IReadOnlyList<FavorabilityView> Build(
        AdventureState adventure,
        Party party,
        IContentRepository contentRepository)
    {
        return adventure.FavorabilityByTarget.Keys
            .OrderBy(static targetId => targetId, StringComparer.Ordinal)
            .Select(targetId => CreateView(targetId, adventure, party, contentRepository))
            .ToList();
    }

    private static FavorabilityView CreateView(
        string targetId,
        AdventureState adventure,
        Party party,
        IContentRepository contentRepository) =>
        new(
            targetId,
            ResolveDisplayName(targetId, party, contentRepository),
            adventure.GetFavorability(targetId));

    private static string ResolveDisplayName(
        string targetId,
        Party party,
        IContentRepository contentRepository)
    {
        if (party.TryGetCharacter(targetId, out var character))
        {
            return character.Name;
        }

        return contentRepository.TryGetCharacter(targetId, out var definition)
            ? definition.Name
            : targetId;
    }
}
