using System.Text.Json.Serialization;
using Game.Core.Abstractions;
using Game.Core.Definitions.Skills;
using Game.Core.Model;

namespace Game.Core.Definitions;

[method: JsonConstructor]
public sealed record CharacterDefinition(
    string Id,
    string Name,
    IReadOnlyDictionary<StatType, int> Stats,
    IReadOnlyList<InitialExternalSkillEntryDefinition> ExternalSkills,
    IReadOnlyList<InitialInternalSkillEntryDefinition> InternalSkills,
    [property: JsonPropertyName("talentIds")] IReadOnlyList<ContentReference<TalentDefinition>> TalentReferences,
    IReadOnlyList<InitialEquipmentEntryDefinition> Equipment,
    int Level = 1,
    string? Portrait = null,
    string? Model = null,
    CharacterGender Gender = CharacterGender.Neutral,
    string? GrowTemplate = null,
    bool ArenaEnabled = false,
    [property: JsonPropertyName("specialSkillIds")] IReadOnlyList<ContentReference<SpecialSkillDefinition>>? SpecialSkillReferences = null)
{
    [JsonIgnore]
    public IReadOnlyList<string> TalentIds =>
        TalentReferences.Select(static reference => reference.Id).ToList();

    [JsonIgnore]
    public IReadOnlyList<TalentDefinition> Talents =>
        TalentReferences.Select(static reference => reference.Definition).ToList();

    [JsonIgnore]
    public IReadOnlyList<EquipmentDefinition> Equipments =>
        Equipment.Select(static entry => entry.Equipment).ToList();

    [JsonIgnore]
    public IReadOnlyList<SpecialSkillDefinition> SpecialSkills =>
        (SpecialSkillReferences ?? []).Select(static reference => reference.Definition).ToList();
}
