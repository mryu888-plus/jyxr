using Game.Core.Abstractions;

namespace Game.Core.Definitions.Skills;

public sealed record FormSkillDefinition(
    string Id,
    string Name,
    string Description,
    string? Icon,
    int UnlockLevel,
    int Cooldown,
    SkillCostDefinition Cost,
    SkillTargetingDefinition? Targeting,
    double PowerExtra,
    string Animation,
    string Audio,
    IReadOnlyList<SkillBuffDefinition> Buffs,
    double Hard = 1d,
    double? LifestealRate = null);
