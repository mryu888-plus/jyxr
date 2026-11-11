using System.Text.Json;
using System.Text.Json.Serialization;
using Game.Core.Abstractions;
using Game.Core.Affix;
using Game.Core.Definitions;
using Game.Core.Model.Skills;

namespace Game.Core.Battle;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SelfBattleUnitSelectorDefinition), "self")]
[JsonDerivedType(typeof(SourceBattleUnitSelectorDefinition), "source")]
[JsonDerivedType(typeof(TargetBattleUnitSelectorDefinition), "target")]
[JsonDerivedType(typeof(AllUnitsBattleUnitSelectorDefinition), "all_units")]
[JsonDerivedType(typeof(AllAlliesBattleUnitSelectorDefinition), "all_allies")]
[JsonDerivedType(typeof(AllEnemiesBattleUnitSelectorDefinition), "all_enemies")]
[JsonDerivedType(typeof(NearbyAlliesBattleUnitSelectorDefinition), "nearby_allies")]
[JsonDerivedType(typeof(NearbyEnemiesBattleUnitSelectorDefinition), "nearby_enemies")]
[JsonDerivedType(typeof(ExplicitUnitsBattleUnitSelectorDefinition), "explicit_units")]
public abstract record BattleUnitSelectorDefinition;

public interface ITargetedBattleEffectDefinition
{
    BattleUnitSelectorDefinition Target { get; }
}

public sealed record SelfBattleUnitSelectorDefinition : BattleUnitSelectorDefinition;

public sealed record SourceBattleUnitSelectorDefinition : BattleUnitSelectorDefinition;

public sealed record TargetBattleUnitSelectorDefinition : BattleUnitSelectorDefinition;

public sealed record AllUnitsBattleUnitSelectorDefinition : BattleUnitSelectorDefinition;

public sealed record AllAlliesBattleUnitSelectorDefinition(
    bool IncludeSelf = true) : BattleUnitSelectorDefinition;

public sealed record AllEnemiesBattleUnitSelectorDefinition : BattleUnitSelectorDefinition;

public sealed record NearbyAlliesBattleUnitSelectorDefinition(
    int Radius,
    bool IncludeSelf = true) : BattleUnitSelectorDefinition;

public sealed record NearbyEnemiesBattleUnitSelectorDefinition(
    int Radius) : BattleUnitSelectorDefinition;

public sealed record ExplicitUnitsBattleUnitSelectorDefinition : BattleUnitSelectorDefinition;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ApplyBuffBattleEffectDefinition), "apply_buff")]
[JsonDerivedType(typeof(RemoveBuffBattleEffectDefinition), "remove_buff")]
[JsonDerivedType(typeof(RemoveNegativeBuffsBattleEffectDefinition), "remove_negative_buffs")]
[JsonDerivedType(typeof(RemovePositiveBuffsBattleEffectDefinition), "remove_positive_buffs")]
[JsonDerivedType(typeof(RemoveContextBuffBattleEffectDefinition), "remove_context_buff")]
[JsonDerivedType(typeof(AddRageBattleEffectDefinition), "add_rage")]
[JsonDerivedType(typeof(SetRageBattleEffectDefinition), "set_rage")]
[JsonDerivedType(typeof(AddActionGaugeBattleEffectDefinition), "add_action_gauge")]
[JsonDerivedType(typeof(SetActionGaugeBattleEffectDefinition), "set_action_gauge")]
[JsonDerivedType(typeof(AddHpBattleEffectDefinition), "add_hp")]
[JsonDerivedType(typeof(AddMpBattleEffectDefinition), "add_mp")]
[JsonDerivedType(typeof(CancelHitBattleHookEffectDefinition), "cancel_hit")]
[JsonDerivedType(typeof(SetHitSuccessBattleHookEffectDefinition), "set_hit_success")]
[JsonDerivedType(typeof(ModifyDamageBattleHookEffectDefinition), "modify_damage")]
[JsonDerivedType(typeof(ModifyDamageContextBattleHookEffectDefinition), "modify_damage_context")]
[JsonDerivedType(typeof(ModifyMpCostBattleHookEffectDefinition), "modify_mp_cost")]
[JsonDerivedType(typeof(ModifyRecoveryBattleHookEffectDefinition), "modify_recovery")]
[JsonDerivedType(typeof(ModifyLifestealBattleHookEffectDefinition), "modify_lifesteal")]
[JsonDerivedType(typeof(StrengthenContextBuffBattleHookEffectDefinition), "strengthen_context_buff")]
[JsonDerivedType(typeof(ExtraStrikeBattleHookEffectDefinition), "extra_strike")]
[JsonDerivedType(typeof(CustomBattleEffectDefinition), "custom")]
[JsonDerivedType(typeof(CustomAbilityBattleEffectDefinition), "custom_ability")]
[JsonDerivedType(typeof(GrantScopedBattleEffectDefinition), "grant_scoped_battle_effect")]
public abstract record BattleEffectDefinition;

public sealed record GrantScopedBattleEffectDefinition(
    [property: JsonPropertyName("effectId")] ContentReference<ScopedBattleEffectDefinition> EffectReference) : BattleEffectDefinition
{
    [JsonIgnore]
    public string EffectId => EffectReference.Id;

    [JsonIgnore]
    public ScopedBattleEffectDefinition Effect => EffectReference.Definition;
}

public sealed record ApplyBuffBattleEffectDefinition(
    BattleUnitSelectorDefinition Target,
    [property: JsonPropertyName("buffId")] ContentReference<BuffDefinition> BuffReference,
    int Level,
    int Duration,
    int Chance = 100) : BattleEffectDefinition, ITargetedBattleEffectDefinition
{
    [JsonIgnore]
    public string BuffId => BuffReference.Id;

    [JsonIgnore]
    public BuffDefinition Buff => BuffReference.Definition;
}

public sealed record RemoveBuffBattleEffectDefinition(
    BattleUnitSelectorDefinition Target,
    [property: JsonPropertyName("buffId")] ContentReference<BuffDefinition> BuffReference) : BattleEffectDefinition, ITargetedBattleEffectDefinition
{
    [JsonIgnore]
    public string BuffId => BuffReference.Id;

    [JsonIgnore]
    public BuffDefinition Buff => BuffReference.Definition;
}

public sealed record RemoveNegativeBuffsBattleEffectDefinition(
    BattleUnitSelectorDefinition Target) : BattleEffectDefinition, ITargetedBattleEffectDefinition;

public sealed record RemovePositiveBuffsBattleEffectDefinition(
    BattleUnitSelectorDefinition Target) : BattleEffectDefinition, ITargetedBattleEffectDefinition;

public sealed record RemoveContextBuffBattleEffectDefinition : BattleEffectDefinition;

public sealed record AddRageBattleEffectDefinition(
    BattleUnitSelectorDefinition Target,
    int Value) : BattleEffectDefinition, ITargetedBattleEffectDefinition;

public sealed record SetRageBattleEffectDefinition(
    BattleUnitSelectorDefinition Target,
    int Value) : BattleEffectDefinition, ITargetedBattleEffectDefinition;

public sealed record AddActionGaugeBattleEffectDefinition(
    BattleUnitSelectorDefinition Target,
    int Value) : BattleEffectDefinition, ITargetedBattleEffectDefinition;

public sealed record SetActionGaugeBattleEffectDefinition(
    BattleUnitSelectorDefinition Target,
    int Value) : BattleEffectDefinition, ITargetedBattleEffectDefinition;

public sealed record AddHpBattleEffectDefinition(
    BattleUnitSelectorDefinition Target,
    int Value) : BattleEffectDefinition, ITargetedBattleEffectDefinition;

public sealed record AddMpBattleEffectDefinition(
    BattleUnitSelectorDefinition Target,
    int Value) : BattleEffectDefinition, ITargetedBattleEffectDefinition;

public sealed record ModifyLifestealBattleHookEffectDefinition(
    double Factor,
    double FactorPerUnitLevel = 0d) : BattleEffectDefinition;

public sealed record CancelHitBattleHookEffectDefinition(
    bool SuppressHitEffects = true) : BattleEffectDefinition;

public sealed record ExtraStrikeBattleHookEffectDefinition(
    BattleUnitSelectorDefinition Target,
    IReadOnlyList<double> DamageFactors,
    double Chance = 0d,
    double ChancePerBuffLevel = 0d) : BattleEffectDefinition, ITargetedBattleEffectDefinition;

public sealed record CustomBattleEffectDefinition(
    string EffectId,
    JsonElement Parameters) : BattleEffectDefinition, IContentResolutionHook
{
    [JsonIgnore]
    internal CustomBattleEffectInvocation Invocation { get; private set; } = null!;

    [JsonIgnore]
    internal bool SupportsPreview => Invocation.SupportsPreview;

    /// <summary>按处理器参数类型解析后的参数对象（引用已解析）。</summary>
    [JsonIgnore]
    public object BoundParameters => Invocation.Parameters;

    public bool SupportsTiming(HookTiming timing) => Invocation.SupportedTimings.Contains(timing);

    /// <summary>按参数绑定处理器，并解析参数里声明的内容引用；缺失引用在加载阶段失败。</summary>
    public void OnResolve(IContentRepository contentRepository)
    {
        Invocation = CustomBattleEffectRegistry.Default.Bind(EffectId, Parameters);
        ContentReferenceGraph.Resolve(Invocation.Parameters, contentRepository);
    }

    internal void ExecuteHook(BattleHookContext context)
    {
        if (!SupportsTiming(context.Timing))
        {
            throw new InvalidOperationException(
                $"Custom battle effect '{EffectId}' does not support timing '{context.Timing}'.");
        }

        (Invocation.ExecuteHook ?? throw new InvalidOperationException(
            $"Custom battle effect '{EffectId}' does not support hook execution."))(context);
    }

}

public sealed record CustomAbilityBattleEffectDefinition(
    string EffectId,
    BattleUnitSelectorDefinition Target,
    JsonElement Parameters) : BattleEffectDefinition, ITargetedBattleEffectDefinition, IContentResolutionHook
{
    [JsonIgnore]
    internal CustomBattleEffectInvocation Invocation { get; private set; } = null!;

    [JsonIgnore]
    public bool SupportsAbility => Invocation.SupportsAbility;

    /// <summary>按参数绑定处理器，并解析参数里声明的内容引用；缺失引用在加载阶段失败。</summary>
    public void OnResolve(IContentRepository contentRepository)
    {
        Invocation = CustomBattleEffectRegistry.Default.Bind(EffectId, Parameters);
        ContentReferenceGraph.Resolve(Invocation.Parameters, contentRepository);
    }

    internal void ExecuteAbility(IBattleAbilityEffectContext context)
    {
        (Invocation.ExecuteAbility ?? throw new InvalidOperationException(
            $"Custom ability battle effect '{EffectId}' does not support ability execution."))(context);
    }

    internal int? EstimateAbilityDamage(BattleAbilityDamageEstimateContext context) =>
        Invocation.EstimateAbilityDamage?.Invoke(context);

    internal IEnumerable<BattleAbilityBuffPreview> PreviewAbilityBuffs(BattleAbilityDamageEstimateContext context) =>
        Invocation.PreviewAbilityBuffs?.Invoke(context) ?? [];

    internal int EstimateAbilitySourceDamage(BattleAbilityDamageEstimateContext context) =>
        Invocation.EstimateSourceDamage?.Invoke(context) ?? 0;
}
