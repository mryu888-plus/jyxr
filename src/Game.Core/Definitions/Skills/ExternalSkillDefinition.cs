using System.Text.Json.Serialization;
using Game.Core.Abstractions;
using Game.Core.Affix;
using Game.Core.Model;

namespace Game.Core.Definitions.Skills;

public sealed record ExternalSkillDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public string Icon { get; init; } = "";
    public WeaponType Type { get; init; } = WeaponType.Quanzhang;
    public bool IsHarmony { get; init; }
    public double Affinity { get; init; }
    public double Hard { get; init; } = 1d;
    public int Cooldown { get; init; }
    public SkillCostDefinition Cost { get; init; } = SkillCostDefinition.None;
    public SkillTargetingDefinition Targeting { get; init; } =  SkillTargetingDefinition.None;
    public double PowerBase { get; init; }
    public double PowerStep { get; init; }
    public double LifestealRate { get; init; }
    public string Audio { get; init; } = "";
    public string Animation { get; init; } = "";
    public IReadOnlyList<SkillBuffDefinition> Buffs { get; init; } = [];
    /// <summary>按等级覆盖的数值，JSON 中是列表；运行期通过 <see cref="LevelOverrides"/> 按等级查找。</summary>
    [JsonPropertyName("levelOverrides")]
    public IReadOnlyList<ExternalSkillLevelDefinition> LevelOverrideEntries
    {
        get;
        init
        {
            field = value;
            LevelOverrides = value.ToDictionary(static definition => definition.Level, static definition => definition);
        }
    } = [];

    public IReadOnlyList<FormSkillDefinition> FormSkills { get; init; } = [];
    public IReadOnlyList<SkillAffixDefinition> Affixes { get; init; } = [];

    [JsonIgnore]
    public IReadOnlyDictionary<int, ExternalSkillLevelDefinition> LevelOverrides { get; private init; } =
        new Dictionary<int, ExternalSkillLevelDefinition>();
}
