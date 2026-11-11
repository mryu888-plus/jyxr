using System.Text.Json.Serialization;
using Game.Core.Affix;
using Game.Core.Model.Skills;

namespace Game.Core.Battle;

public abstract record BattleMessage(string UnitId, HookTiming? Timing = null);

public sealed record BattleFact : BattleMessage
{
    public BattleFact(
        BattleFactKind kind,
        string unitId,
        HookTiming? timing = null,
        string? detail = null,
        BattleSkillCastInfo? skillCast = null,
        BattleDamageEvent? damage = null,
        BattleRestRecovery? rest = null,
        BattleSkillExperienceEvent? skillExperience = null,
        BattleCharacterExperienceEvent? characterExperience = null,
        BattleLifestealEvent? lifesteal = null,
        BattleBuffReductionEvent? buffReduction = null,
        BattleRelocationEvent? relocation = null)
        : base(unitId, timing)
    {
        Kind = kind;
        Detail = detail;
        SkillCast = skillCast;
        Damage = damage;
        Rest = rest;
        SkillExperience = skillExperience;
        CharacterExperience = characterExperience;
        Lifesteal = lifesteal;
        BuffReduction = buffReduction;
        Relocation = relocation;
    }

    /// <summary>强制位置变化（<see cref="BattleFactKind.Relocated"/>）的起止格与种类。</summary>
    public BattleRelocationEvent? Relocation { get; }

    public BattleFactKind Kind { get; }
    public string? Detail { get; }
    public BattleSkillCastInfo? SkillCast { get; }
    public BattleDamageEvent? Damage { get; }
    public BattleRestRecovery? Rest { get; }
    public BattleSkillExperienceEvent? SkillExperience { get; }
    public BattleCharacterExperienceEvent? CharacterExperience { get; }
    public BattleLifestealEvent? Lifesteal { get; }
    public BattleBuffReductionEvent? BuffReduction { get; }
}

public sealed record BattleCue : BattleMessage
{
    public BattleCue(
        BattleCueKind kind,
        string unitId,
        HookTiming? timing = null,
        BattleSpeechCue? speech = null,
        BattleFloatTextCue? floatText = null)
        : base(unitId, timing)
    {
        Kind = kind;
        Speech = speech;
        FloatText = floatText;
    }

    public BattleCueKind Kind { get; }
    public BattleSpeechCue? Speech { get; }
    public BattleFloatTextCue? FloatText { get; }
}

public sealed record BattleTrace : BattleMessage
{
    public BattleTrace(
        BattleTraceKind kind,
        string unitId,
        HookTiming? timing = null,
        IReadOnlyList<string>? hookLabels = null,
        BattleExecutionScope? executionScope = null)
        : base(unitId, timing)
    {
        Kind = kind;
        HookLabels = hookLabels;
        ExecutionScope = executionScope;
    }

    public BattleTraceKind Kind { get; }
    public IReadOnlyList<string>? HookLabels { get; }
    public BattleExecutionScope? ExecutionScope { get; }
}

public enum BattleFactKind
{
    ActionStarted,
    ActionSkipped,
    Moved,
    MovementRolledBack,
    SkillCast,
    SkillCooldownsReset,
    Damaged,
    ItemUsed,
    Rested,
    ActionEnded,
    BuffApplied,
    BuffRemoved,
    BuffReduced,
    Healed,
    Lifesteal,
    MpDamaged,
    MpRecovered,
    RageChanged,
    ActionGaugeChanged,
    DefeatPrevented,
    UnitDefeated,
    ScopedEffectGranted,
    ScopedEffectRemoved,
    SkillLeveledUp,
    CharacterLeveledUp,
    Relocated,
}

public enum BattleCueKind
{
    SpeechRequested,
    FloatTextRequested,
}

public enum BattleTraceKind
{
    HooksTriggered,
}

public sealed record BattleSpeechCue(string Text);
public sealed record BattleBuffReductionEvent(
    string BuffId,
    int LevelReduction,
    int DurationReduction,
    int RemainingLevel,
    int RemainingDuration);
public enum BattleFloatTextTarget
{
    [JsonStringEnumMemberName("owner")]
    Owner,
    [JsonStringEnumMemberName("source")]
    Source,
    [JsonStringEnumMemberName("target")]
    Target,
}
public sealed record BattleFloatTextDefinition
{
    public BattleFloatTextTarget Target { get; init; } = BattleFloatTextTarget.Owner;
    public required string Text { get; init; }
    public BattleFloatTextStyle Style { get; init; } = BattleFloatTextStyle.Normal;
}
public sealed record BattleFloatTextCue(string Text, BattleFloatTextStyle Style = BattleFloatTextStyle.Normal);
public enum BattleFloatTextStyle
{
    Normal,
    Critical,
    Recovery,
    Mana,
    Energy,
    Beneficial,
    Harmful,
    Special,
}
public sealed record BattleDamageEvent(int Amount, bool IsCritical = false, string? SourceUnitId = null);
public sealed record BattleLifestealEvent(int Amount);
public sealed record BattleSkillExperienceEvent(string SkillId, string SkillName, SkillKind SkillKind, int AddedExperience, int OldLevel, int NewLevel);
public sealed record BattleCharacterExperienceEvent(string CharacterId, string CharacterName, int AddedExperience, int OldLevel, int NewLevel);
