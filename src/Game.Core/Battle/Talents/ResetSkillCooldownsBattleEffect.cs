using Game.Core.Definitions;
using System.Text.Json.Serialization;
using Game.Core.Affix;

namespace Game.Core.Battle.Talents;

public sealed record ResetSkillCooldownsBattleEffectParameters(
    [property: JsonPropertyName("talentId")] ContentReference<TalentDefinition> TalentReference)
{
    [JsonIgnore]
    public string TalentId => TalentReference.Id;
}

internal sealed class ResetSkillCooldownsBattleEffectHandler
    : CustomBattleEffectHandler<ResetSkillCooldownsBattleEffectParameters, IHitResultEffectContext>
{
    public override IReadOnlySet<HookTiming> SupportedTimings { get; } =
        new HashSet<HookTiming> { HookTiming.BeforeHitResolved };

    public override void Execute(
        IHitResultEffectContext context,
        ResetSkillCooldownsBattleEffectParameters parameters) =>
        context.ResetUnitSkillCooldowns(parameters.TalentId);
}
