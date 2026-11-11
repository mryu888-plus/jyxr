using Game.Core;
using Game.Core.Affix;

namespace Game.Core.Battle;

internal sealed record PendingHitOutcome(HookAffix Hook, BattleUnit? Provider, BattleBuffInstance? Buff);

public sealed class BattleHookExecutor
{
    internal BattleEffectExecutor? EffectExecutor { get; set; }

    public void Execute(BattleHookContext context, HookAffix hook)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(hook);

        if (hook.Timing != context.Timing)
        {
            throw new InvalidOperationException(
                $"Hook timing '{hook.Timing}' does not match context timing '{context.Timing}'.");
        }

        BattleHookPreviewPolicy.EnsureSafe(context, hook);
        if (hook.Conditions.Any(condition => !BattleHookEvaluator.Evaluate(context, condition)))
        {
            return;
        }

        if (context.DeferHitOutcomes)
        {
            foreach (var effect in hook.Effects.Where(IsHitStateEffect))
            {
                ExecuteEffect(context, hook, effect);
            }

            context.PendingHitOutcomes.Add(new PendingHitOutcome(hook, context.Provider, context.Buff));
            return;
        }

        foreach (var effect in hook.Effects)
        {
            ExecuteEffect(context, hook, effect);
        }

        TryRequestFloatText(context, hook.FloatText);
        TryRequestSpeech(context, hook.Speech);
    }

    /// <summary>
    /// 命中结算第二阶段：最终命中状态确定后，提交第一阶段已触发钩子的副作用。
    /// 只有与最终结果一致的钩子才提交：取消命中的钩子要求最终未命中，强制命中的钩子要求最终命中，
    /// 声明命中状态条件的钩子要求最终状态仍满足条件。条件中的概率不重新判定。
    /// </summary>
    internal void CommitHitOutcome(BattleHookContext context, PendingHitOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(outcome);
        if (!IsConsistentWithFinalHit(outcome.Hook, context.HitState))
        {
            return;
        }

        var previousProvider = context.Provider;
        var previousBuff = context.Buff;
        context.Provider = outcome.Provider;
        context.Buff = outcome.Buff;
        foreach (var effect in outcome.Hook.Effects.Where(static effect => !IsHitStateEffect(effect)))
        {
            ExecuteEffect(context, outcome.Hook, effect);
        }

        TryRequestFloatText(context, outcome.Hook.FloatText);
        TryRequestSpeech(context, outcome.Hook.Speech);
        context.Provider = previousProvider;
        context.Buff = previousBuff;
    }

    private static bool IsHitStateEffect(BattleEffectDefinition effect) =>
        effect is CancelHitBattleHookEffectDefinition or SetHitSuccessBattleHookEffectDefinition;

    private static bool IsConsistentWithFinalHit(HookAffix hook, BattleHitState finalState)
    {
        if (hook.Effects.OfType<CancelHitBattleHookEffectDefinition>().Any() && finalState != BattleHitState.Miss)
        {
            return false;
        }

        if (hook.Effects.OfType<SetHitSuccessBattleHookEffectDefinition>().Any() && finalState != BattleHitState.Hit)
        {
            return false;
        }

        return hook.Conditions
            .OfType<ContextHitStateBattleHookConditionDefinition>()
            .All(condition => condition.State == finalState);
    }

    private void ExecuteEffect(BattleHookContext context, HookAffix hook, BattleEffectDefinition effect)
    {
        {
            if (!BattleEffectTimingPolicy.Supports(context.Timing, effect))
            {
                throw new InvalidOperationException(
                    $"Effect '{effect.GetType().Name}' cannot execute at timing '{context.Timing}'.");
            }

            using var effectScope = context.State.EnterEffect($"hook:{hook.Timing}:{effect.GetType().Name}");
            (EffectExecutor ?? throw new InvalidOperationException(
                "Battle hook executor is not attached to a battle engine."))
                .ExecuteHook(context, effect);
        }
    }

    private static void TryRequestFloatText(BattleHookContext context, BattleFloatTextDefinition? floatText)
    {
        if (floatText is null)
        {
            return;
        }

        var target = floatText.Target switch
        {
            BattleFloatTextTarget.Owner => context.Unit,
            BattleFloatTextTarget.Source => context.Source,
            BattleFloatTextTarget.Target => context.Target,
            _ => throw new ArgumentOutOfRangeException(nameof(floatText.Target), floatText.Target, null),
        };
        var text = BattleCueTextFormatter.Format(floatText.Text, context.Unit, context.Source, context.Target);
        if (target is not null && !string.IsNullOrWhiteSpace(text))
        {
            context.RequestFloatText(target, text, floatText.Style);
        }
    }

    private static void TryRequestSpeech(BattleHookContext context, BattleSpeechDefinition? speech)
    {
        if (speech is null)
        {
            return;
        }

        var speaker = ResolveSpeaker(context, speech.Speaker);
        var line = BattleSpeechRuntime.TryPickLine(speech, context.Random);
        line = BattleCueTextFormatter.Format(line, context.Unit, context.Source, context.Target);
        BattleSpeechRuntime.TryEmit(context.State, speaker, line, context.Timing);
    }

    private static BattleUnit? ResolveSpeaker(BattleHookContext context, BattleSpeechSpeaker speaker) =>
        speaker switch
        {
            BattleSpeechSpeaker.Owner => context.Unit,
            BattleSpeechSpeaker.Source => context.Source,
            BattleSpeechSpeaker.Target => context.Target,
            _ => throw new ArgumentOutOfRangeException(nameof(speaker), speaker, null),
        };
}
