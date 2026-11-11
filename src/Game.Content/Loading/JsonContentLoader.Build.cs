using Game.Core.Definitions;
using Game.Core.Definitions.Skills;
using Game.Core.Affix;
using Game.Core.Model;
using Game.Core.Story;

namespace Game.Content.Loading;

public sealed partial class JsonContentLoader
{
    /// <summary>
    /// 两阶段装配：先把全部定义注册进仓库（此时引用尚未解析），再由 <see cref="ContentReferenceResolver"/>
    /// 统一解析所有定义里的引用。解析只按 ID 查找已注册的定义，与注册和解析顺序无关，因此允许定义间循环引用。
    /// </summary>
    private static InMemoryContentRepository BuildRepository(ContentPackage package)
    {
        var repository = new InMemoryContentRepository
        {
            Battles = IndexById(package.Battles, "Battle"),
            ScopedBattleEffects = IndexById(package.ScopedBattleEffects, "ScopedBattleEffect"),
            Characters = IndexById(package.Characters, "Character"),
            ExternalSkills = IndexById(package.ExternalSkills, "ExternalSkill"),
            GameTips = IndexById(package.GameTips, "GameTip"),
            GrowTemplates = IndexById(package.GrowTemplates, "GrowTemplate"),
            InternalSkills = IndexById(package.InternalSkills, "InternalSkill"),
            LegendSkills = package.LegendSkills.ToList(),
            Maps = IndexById(package.Maps, "Map"),
            WorldTriggers = package.WorldTriggers.ToList(),
            Resources = IndexById(package.Resources, "Resource"),
            Sects = IndexOrderedById(package.Sects, "Sect"),
            Shops = IndexById(package.Shops, "Shop"),
            SpecialSkills = IndexById(package.SpecialSkills, "SpecialSkill"),
            StoryScripts = new Dictionary<string, StoryScript>(package.StoryScripts, StringComparer.Ordinal),
            StorySegments = BuildStorySegments(package.StoryScripts),
            Items = BuildItems(package),
            ItemTags = IndexById(package.ItemTags, "ItemTag"),
            EquipmentRandomAffixTables = package.RandomAffixTables.ToList(),
            Buffs = IndexById(package.Buffs, "Buff"),
            Talents = IndexById(package.Talents, "Talent"),
            Equipments = IndexById(package.Items.OfType<EquipmentDefinition>(), "Equipment"),
            Towers = IndexById(package.Towers, "Tower"),
        };

        ValidateItemTags(repository);
        ContentReferenceResolver.ResolveAll(repository);
        return repository;
    }

    private static Dictionary<string, ItemDefinition> BuildItems(ContentPackage package) =>
        package.Items.ToDictionary(
            definition => definition.Id,
            StringComparer.Ordinal);

    private static Dictionary<string, StorySegmentEntry> BuildStorySegments(
        IReadOnlyDictionary<string, StoryScript> storyScripts)
    {
        var segments = new Dictionary<string, StorySegmentEntry>(StringComparer.Ordinal);
        var allSegments = storyScripts
            .SelectMany(static entry => entry.Value.Segments)
            .ToList();
        var combinedScript = new StoryScript(
            storyScripts.Values.FirstOrDefault()?.Version ?? StoryScript.CurrentVersion,
            allSegments);

        foreach (var (scriptId, script) in storyScripts)
        {
            foreach (var segment in script.Segments)
            {
                Ensure(segments.TryAdd(segment.Name, new StorySegmentEntry(segment.Name, scriptId, combinedScript, segment)),
                    $"Story segment '{segment.Name}' is duplicated.");
            }
        }

        return segments;
    }

    private static Dictionary<string, TDefinition> IndexById<TDefinition>(IEnumerable<TDefinition> definitions, string typeName)
        where TDefinition : class
    {
        var indexed = new Dictionary<string, TDefinition>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            var id = (string?)typeof(TDefinition).GetProperty("Id")?.GetValue(definition);
            Ensure(!string.IsNullOrWhiteSpace(id), $"{typeName} definition has empty id.");
            Ensure(indexed.TryAdd(id!, definition), $"{typeName} '{id}' is duplicated.");
        }

        return indexed;
    }

    private static OrderedDictionary<string, TDefinition> IndexOrderedById<TDefinition>(
        IEnumerable<TDefinition> definitions,
        string typeName)
        where TDefinition : class =>
        new(
            definitions.Select(definition => KeyValuePair.Create(GetDefinitionId(definition, typeName), definition)),
            StringComparer.Ordinal);

    private static string GetDefinitionId<TDefinition>(TDefinition definition, string typeName)
        where TDefinition : class
    {
        var id = (string?)typeof(TDefinition).GetProperty("Id")?.GetValue(definition);
        Ensure(!string.IsNullOrWhiteSpace(id), $"{typeName} definition has empty id.");
        return id!;
    }
}
