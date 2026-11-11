using System.Text.Json.Serialization;
using Game.Core.Abstractions;

namespace Game.Core.Definitions.Skills;

[method: JsonConstructor]
public sealed record InitialExternalSkillEntryDefinition(
    [property: JsonPropertyName("id")] ContentReference<ExternalSkillDefinition> SkillReference,
    int Level = 1,
    int? MaxLevel = null)
{
    public InitialExternalSkillEntryDefinition(
        ExternalSkillDefinition skill,
        int Level = 1,
        int? MaxLevel = null)
        : this(new ContentReference<ExternalSkillDefinition>(skill.Id, skill), Level, MaxLevel)
    {
    }

    [JsonIgnore]
    public string Id => SkillReference.Id;

    [JsonIgnore]
    public ExternalSkillDefinition Skill => SkillReference.Definition;
}

[method: JsonConstructor]
public sealed record InitialInternalSkillEntryDefinition(
    [property: JsonPropertyName("id")] ContentReference<InternalSkillDefinition> SkillReference,
    int Level = 1,
    int? MaxLevel = null,
    bool Equipped = false)
{
    public InitialInternalSkillEntryDefinition(
        InternalSkillDefinition skill,
        int Level = 1,
        int? MaxLevel = null,
        bool Equipped = false)
        : this(new ContentReference<InternalSkillDefinition>(skill.Id, skill), Level, MaxLevel, Equipped)
    {
    }

    [JsonIgnore]
    public string Id => SkillReference.Id;

    [JsonIgnore]
    public InternalSkillDefinition Skill => SkillReference.Definition;
}
