using System.Text.Json.Serialization;
using Game.Core.Abstractions;
using Game.Core.Model;

namespace Game.Core.Definitions;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "category")]
[JsonDerivedType(typeof(NormalItemDefinition), "normal")]
[JsonDerivedType(typeof(EquipmentDefinition), "equipment")]
public abstract record ItemDefinition
{
    public required string Id { get; init; }
    public required string Name  { get; init; }
    public ItemType Type  { get; init; }
    public required bool ConsumeOnUse { get; init; }
    public int Level { get; init; }
    public int Price { get; init; }
    public int Cooldown { get; init; }
    public bool CanDrop { get; init; }
    public string Description { get; init; } = "";
    public string Picture { get; init; } = "";
    [JsonPropertyName("tagIds")]
    public IReadOnlyList<ContentReference<ItemTagDefinition>> TagReferences { get; init; } = [];
    [JsonIgnore]
    public IReadOnlyList<string> TagIds =>
        TagReferences.Select(static reference => reference.Id).ToList();
    [JsonIgnore]
    public IReadOnlyList<ItemTagDefinition> Tags =>
        TagReferences.Select(static reference => reference.Definition).ToList();
    public IReadOnlyList<ItemRequirementDefinition> Requirements { get; init; } = [];
    public IReadOnlyList<ItemUseEffectDefinition> UseEffects { get; init; } = [];
}

public sealed record NormalItemDefinition : ItemDefinition;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(StatItemRequirementDefinition), "stat")]
[JsonDerivedType(typeof(TalentItemRequirementDefinition), "talent")]
[JsonDerivedType(typeof(GenderItemRequirementDefinition), "gender")]
public abstract record ItemRequirementDefinition;

public sealed record StatItemRequirementDefinition(
    StatType StatId,
    int Value) : ItemRequirementDefinition;

public sealed record TalentItemRequirementDefinition(
    string TalentId) : ItemRequirementDefinition;

public sealed record GenderItemRequirementDefinition(
    IReadOnlyList<CharacterGender> Genders) : ItemRequirementDefinition;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(AddBuffItemUseEffectDefinition), "add_buff")]
[JsonDerivedType(typeof(AddRageItemUseEffectDefinition), "add_rage")]
[JsonDerivedType(typeof(DetoxifyItemUseEffectDefinition), "detoxify")]
[JsonDerivedType(typeof(AddStatsItemUseEffectDefinition), "add_stats")]
[JsonDerivedType(typeof(AddHpItemUseEffectDefinition), "add_hp")]
[JsonDerivedType(typeof(AddMpItemUseEffectDefinition), "add_mp")]
[JsonDerivedType(typeof(AddHpPercentItemUseEffectDefinition), "add_hp_percent")]
[JsonDerivedType(typeof(AddMpPercentItemUseEffectDefinition), "add_mp_percent")]
[JsonDerivedType(typeof(GrantExternalSkillItemUseEffectDefinition), "external_skill")]
[JsonDerivedType(typeof(GrantInternalSkillItemUseEffectDefinition), "internal_skill")]
[JsonDerivedType(typeof(GrantSpecialSkillItemUseEffectDefinition), "special_skill")]
[JsonDerivedType(typeof(GrantTalentItemUseEffectDefinition), "grant_talent")]
[JsonDerivedType(typeof(SetGenderItemUseEffectDefinition), "set_gender")]
[JsonDerivedType(typeof(ReduceMaxResourceRatioItemUseEffectDefinition), "reduce_max_resource_ratio")]
[JsonDerivedType(typeof(RunStoryItemUseEffectDefinition), "run_story")]
public abstract record ItemUseEffectDefinition;

public sealed record AddBuffItemUseEffectDefinition(
    string BuffId,
    int Level = 1,
    int Duration = 3,
    int? Property = null) : ItemUseEffectDefinition;

public sealed record AddRageItemUseEffectDefinition(
    int Value) : ItemUseEffectDefinition;

public sealed record DetoxifyItemUseEffectDefinition(
    IReadOnlyList<int>? Values = null) : ItemUseEffectDefinition;

public sealed record AddStatsItemUseEffectDefinition(
    IReadOnlyDictionary<StatType, int> Values) : ItemUseEffectDefinition;

public sealed record AddHpItemUseEffectDefinition(
    int Value) : ItemUseEffectDefinition;

public sealed record AddMpItemUseEffectDefinition(
    int Value) : ItemUseEffectDefinition;

public sealed record AddHpPercentItemUseEffectDefinition(
    int Value) : ItemUseEffectDefinition;

public sealed record AddMpPercentItemUseEffectDefinition(
    int Value) : ItemUseEffectDefinition;

public sealed record GrantExternalSkillItemUseEffectDefinition(
    string SkillId,
    int? Level = null) : ItemUseEffectDefinition;

public sealed record GrantInternalSkillItemUseEffectDefinition(
    string SkillId,
    int? Level = null) : ItemUseEffectDefinition;

public sealed record GrantSpecialSkillItemUseEffectDefinition(
    string SkillId) : ItemUseEffectDefinition;

public sealed record GrantTalentItemUseEffectDefinition(
    string TalentId) : ItemUseEffectDefinition;

public sealed record SetGenderItemUseEffectDefinition(
    CharacterGender Gender) : ItemUseEffectDefinition;

public sealed record ReduceMaxResourceRatioItemUseEffectDefinition(
    StatType StatId,
    double Ratio) : ItemUseEffectDefinition;

public sealed record RunStoryItemUseEffectDefinition(
    string StoryId) : ItemUseEffectDefinition;
