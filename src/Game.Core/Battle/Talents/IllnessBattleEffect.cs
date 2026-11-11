using System.Text.Json.Serialization;
using Game.Core.Affix;
using Game.Core.Definitions;

namespace Game.Core.Battle.Talents;

public sealed record IllnessBattleEffectParameters(
    [property: JsonPropertyName("protectorCharacterId")] ContentReference<CharacterDefinition>? ProtectorCharacterReference = null,
    string? SkipReason = null)
{
    [JsonIgnore]
    public string? ProtectorCharacterId => ProtectorCharacterReference?.Id;
}

public sealed class IllnessBattleEffectHandler : CustomBattleEffectHandler<IllnessBattleEffectParameters, IActionStartEffectContext>
{
    public override IReadOnlySet<HookTiming> SupportedTimings { get; } =
        new HashSet<HookTiming> { HookTiming.BeforeActionStart };

    public override void Execute(
        IActionStartEffectContext context,
        IllnessBattleEffectParameters parameters)
    {
        var protectorIsPresent = context.State.GetLivingUnits().Any(unit =>
            unit.Team == context.Unit.Team &&
            string.Equals(
                unit.Character.Definition.Id,
                parameters.ProtectorCharacterId,
                StringComparison.Ordinal));
        if (!protectorIsPresent)
        {
            context.SkipCurrentAction(parameters.SkipReason);
        }
    }
}
