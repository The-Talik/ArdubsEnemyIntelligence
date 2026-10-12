using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Kingmaker.AI;
using Kingmaker.AI.Blueprints;
using Kingmaker.Controllers;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using TurnBased.Controllers;

namespace ArdubsEnemyIntelligence;

internal static class TargetingPatches
{
    private static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            ArdubsEnemyIntelligenceMain.ModEntry.Logger.Error(exception.ToString());
        }
    }

    private static bool Guard(Func<bool> action)
    {
        try
        {
            return action();
        }
        catch (Exception exception)
        {
            ArdubsEnemyIntelligenceMain.ModEntry.Logger.Error(exception.ToString());
            return true;
        }
    }

    [HarmonyPatch(typeof(TurnController), nameof(TurnController.Prepare))]
    private static class TurnPreparePatch
    {
        [HarmonyPrefix]
        private static void Prefix(TurnController __instance)
        {
            Guard(() =>
            {
                TargetingState.BeginTurn(__instance, __instance.Rider);
                TargetingState.BeginTurn(__instance, __instance.Mount);
            });
        }
    }

    [HarmonyPatch(typeof(CombatController), nameof(CombatController.Start))]
    private static class CombatStartPatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            TargetingState.Reset();
        }
    }

    [HarmonyPatch(typeof(CombatController), nameof(CombatController.Stop))]
    private static class CombatStopPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            TargetingState.Reset();
        }
    }

    [HarmonyPatch(typeof(CombatController), nameof(CombatController.HandleCombatStart))]
    private static class CombatBeginPatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            TargetingState.Reset();
        }
    }

    [HarmonyPatch(typeof(CombatController), nameof(CombatController.Deactivate))]
    private static class CombatDeactivatePatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            TargetingState.Reset();
        }
    }

    [HarmonyPatch(typeof(UnitImpatienceController), "UpdatePriorityTarget")]
    private static class VanillaImpatiencePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(UnitEntityData unit)
        {
            // Our once-per-turn roll replaces the game's independent, chance-based
            // attack-failure switch for managed enemies/opt-in allies.
            // Player-controlled units keep vanilla behavior.
            try
            {
                return !TargetingState.IsManaged(unit);
            }
            catch (Exception exception)
            {
                ArdubsEnemyIntelligenceMain.ModEntry.Logger.Error(exception.ToString());
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(AiAction), nameof(AiAction.ScoreTarget))]
    private static class ScoreAttackPatch
    {
        private sealed class TargetScoreState
        {
            internal bool RunOriginal;
            internal decimal? PreviousBest;
        }

        [HarmonyPrefix]
        private static bool Prefix(AiAction __instance, DecisionContext context, out TargetScoreState __state)
        {
            // The game's Harmony 2.0.4 supports __state, but not __runOriginal.
            var state = new TargetScoreState();
            state.RunOriginal = Guard(() =>
            {
                if (!TargetingState.AllowTargetScore(__instance, context))
                {
                    return false;
                }
                // Vanilla prunes partial scores as if all remaining factors were
                // <= 1. Positive vanilla scores no longer determine our weights,
                // so finish the target's eligibility checks before custom scoring.
                if (TargetingState.UsesCustomScore(__instance, context))
                {
                    state.PreviousBest = context.BestScore;
                    context.BestScore = 0m;
                }
                return true;
            });
            __state = state;
            return state.RunOriginal;
        }

        [HarmonyPostfix]
        private static void Postfix(AiAction __instance, DecisionContext context, TargetScoreState __state)
        {
            RestoreBest(context, __state);
            if (__state.RunOriginal)
            {
                Guard(() =>
                {
                    if (TargetingState.UsesCustomScore(__instance, context))
                    {
                        TargetingState.ScoreAttack(__instance, context);
                    }
                });
            }
        }

        [HarmonyFinalizer]
        private static Exception? Finalizer(DecisionContext context, TargetScoreState? __state, Exception? __exception)
        {
            RestoreBest(context, __state);
            return __exception;
        }

        private static void RestoreBest(DecisionContext context, TargetScoreState? state)
        {
            if (state?.PreviousBest != null)
            {
                context.BestScore = state.PreviousBest.Value;
                state.PreviousBest = null;
            }
        }
    }

    private static bool StopComparing(decimal difference, decimal epsilon,
        DecisionContext context, AiAction action) =>
        !TargetingState.UsesCustomScore(action, context) && difference < epsilon;

    private static bool RejectBeforeTargets(decimal actorScore, decimal bestScore,
        DecisionContext context, AiAction action) =>
        actorScore <= 0m || !TargetingState.UsesCustomScore(action, context) && actorScore <= bestScore;

    private static void FinishActorScore(AiAction action, DecisionContext context, AIDebugScope scope)
    {
        var best = context.BestScore;
        if (TargetingState.UsesCustomScore(action, context))
        {
            context.BestScore = 0m;
        }
        try
        {
            action.ScoreActor(context, scope);
        }
        finally
        {
            context.BestScore = best;
        }
    }

    [HarmonyPatch(typeof(AiBrainController), "CalculateActionScore")]
    private static class FinishEligibleActionsPatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var count = 0;
            var actorCalls = 0;
            foreach (var code in instructions)
            {
                if (code.Calls(AccessTools.Method(typeof(AiAction), nameof(AiAction.ScoreActor))))
                {
                    actorCalls++;
                    code.opcode = OpCodes.Call;
                    code.operand = AccessTools.Method(typeof(TargetingPatches), nameof(FinishActorScore));
                }
                if (code.opcode == OpCodes.Call && code.operand is MethodInfo method &&
                    method.DeclaringType == typeof(decimal) && method.Name == "op_LessThanOrEqual")
                {
                    count++;
                    var context = new CodeInstruction(OpCodes.Ldarg_0);
                    context.labels.AddRange(code.labels);
                    code.labels.Clear();
                    yield return context;
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    code.operand = AccessTools.Method(typeof(TargetingPatches), nameof(RejectBeforeTargets));
                }
                yield return code;
            }
            if (count != 1 || actorCalls != 1)
            {
                throw new InvalidOperationException("Cannot identify the game's actor-score pruning check.");
            }
        }
    }

    private static bool PruneAction(decimal bestScore, decimal actionBaseScore, UnitEntityData unit) =>
        !TargetingState.IsManaged(unit) && bestScore > actionBaseScore;

    [HarmonyPatch(typeof(AiBrainController), "FindBestAction")]
    private static class RemoveUnsafeActionBoundPatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var count = 0;
            foreach (var code in instructions)
            {
                if (code.opcode == OpCodes.Call && code.operand is MethodInfo method &&
                    method.DeclaringType == typeof(decimal) && method.Name == "op_GreaterThan")
                {
                    count++;
                    var unit = new CodeInstruction(OpCodes.Ldarg_0);
                    unit.labels.AddRange(code.labels);
                    code.labels.Clear();
                    yield return unit;
                    code.operand = AccessTools.Method(typeof(TargetingPatches), nameof(PruneAction));
                }
                yield return code;
            }
            if (count != 2)
            {
                throw new InvalidOperationException("Cannot identify both game action BaseScore pruning checks.");
            }
        }
    }

    [HarmonyPatch(typeof(AiBrainController), "CalculateTargetsScore")]
    private static class CompareAllAttackCandidatesPatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList();
            var matches = codes.Where(code => code.opcode == OpCodes.Call &&
                code.operand is MethodInfo method && method.DeclaringType == typeof(decimal) &&
                method.Name == "op_LessThan").ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidOperationException("Cannot identify the game's target-score early-exit comparison.");
            }
            foreach (var code in codes)
            {
                if (ReferenceEquals(code, matches[0]))
                {
                    var context = new CodeInstruction(OpCodes.Ldarg_0);
                    context.labels.AddRange(code.labels);
                    code.labels.Clear();
                    yield return context;
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    code.operand = AccessTools.Method(typeof(TargetingPatches), nameof(StopComparing));
                }
                yield return code;
            }
        }
    }

    [HarmonyPatch(typeof(AiAction), nameof(AiAction.ScorePath))]
    private static class ScoreDistancePatch
    {
        private sealed class RouteScope
        {
            internal readonly DecisionContext? Previous = CompleteAttackRoutes.Current;
            internal TargetingState.PathScoreState? Score;
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var fixPath = AccessTools.Method(typeof(AiAction), "FixPathIfTooShort");
            var captured = 0;
            foreach (var code in instructions)
            {
                if (code.Calls(fixPath))
                {
                    captured++;
                    code.opcode = OpCodes.Call;
                    code.operand = AccessTools.Method(typeof(TargetingPatches), nameof(FixAndCaptureGameRoute));
                }
                yield return code;
            }
            if (captured != 1)
            {
                throw new InvalidOperationException("Cannot identify the game's completed attack-path callback.");
            }
        }

        [HarmonyPrefix]
        private static void Prefix(AiAction __instance, DecisionContext context,
            out RouteScope __state)
        {
            var scope = new RouteScope();
            __state = scope;
            CompleteAttackRoutes.Current = null;
            Guard(() =>
            {
                scope.Score = TargetingState.BeginPathScore(__instance, context);
                if (scope.Score != null) CompleteAttackRoutes.Current = context;
            });
        }

        [HarmonyPostfix]
        private static void Postfix(RouteScope __state)
        {
            CompleteAttackRoutes.Current = __state.Previous;
            Guard(() => TargetingState.FinishPathScore(__state.Score));
        }

        [HarmonyFinalizer]
        private static Exception? Finalizer(RouteScope? __state, Exception? __exception)
        {
            if (__state != null) CompleteAttackRoutes.Current = __state.Previous;
            return __exception;
        }
    }

    private static void FixAndCaptureGameRoute(DecisionContext context, UnityEngine.Vector3 destination)
    {
        // Native FixPathIfTooShort can replace a failed/one-point search with
        // an unchecked FakePath straight to the target. Never use that as proof
        // of reachability for any of our scored attack or ability candidates.
        if (!CompleteAttackRoutes.PreserveSearchResult(context) &&
            !TouchDeliveryPath.IsManagedOffensiveCast(context))
            AiAction.FixPathIfTooShort(context, destination);
        Guard(() => TargetingState.CapturePath(context));
    }

    [HarmonyPatch(typeof(UnitCommands), nameof(UnitCommands.Run),
        new[] { typeof(UnitCommand), typeof(bool), typeof(bool) })]
    private static class CommitBeforeApproachPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(UnitCommands __instance, UnitCommand __0, out bool __state)
        {
            __state = Guard(() => TargetingState.AllowCommand(__instance.m_Owner, __0));
            return __state;
        }

        [HarmonyPostfix]
        private static void Postfix(UnitCommands __instance, UnitCommand __0, bool __state)
        {
            // Run can refuse a command. Commit only commands actually accepted by
            // the unit, after vanilla mount-target normalization, before any tick.
            if (__state && __instance.Raw.Any(command => ReferenceEquals(command, __0)))
            {
                Guard(() => TargetingState.CommitCommand(__0));
                try { TouchDeliveryPath.Remember(__0); }
                catch (Exception exception)
                {
                    ArdubsEnemyIntelligenceMain.ModEntry.Logger.Error("AI touch path retention: " + exception);
                }
            }
        }
    }

    [HarmonyPatch(typeof(UnitCommand), "get_AiCanInterrupt")]
    private static class KeepApproachPatch
    {
        [HarmonyPostfix]
        private static void Postfix(UnitCommand __instance, ref bool __result)
        {
            if (__result)
            {
                __result = Guard(() => !TargetingState.KeepApproach(__instance));
            }
        }
    }

    // Commands may be queued before their target falls asleep. Cancel before
    // starting or ticking an attack, not after damage has already woken it.
    [HarmonyPatch(typeof(UnitCommand), nameof(UnitCommand.Start))]
    private static class SleepingCommandStartPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(UnitCommand __instance) =>
            Guard(() => !TargetingState.StopSleepingCommand(__instance));
    }

    [HarmonyPatch(typeof(UnitCommand), nameof(UnitCommand.Tick))]
    private static class SleepingCommandTickPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(UnitCommand __instance) =>
            Guard(() => !TargetingState.StopSleepingCommand(__instance));
    }

    [HarmonyPatch(typeof(UnitAttack), "set_Target", new[] { typeof(UnitEntityData) })]
    private static class PreventAttackAutoRetargetPatch
    {
        [HarmonyPrefix]
        private static void Prefix(UnitAttack __instance, ref UnitEntityData __0)
        {
            // Keep vanilla range/validity checks, but its nearby-target fallback
            // must not silently change an already committed AI attack's target.
            var target = __0;
            Guard(() => { target = TargetingState.RestrictAttackTarget(__instance, target); });
            __0 = target;
        }
    }

    [HarmonyPatch(typeof(UnitAttack), "UpdateTarget")]
    private static class RememberNativeAttackRetargetPatch
    {
        [HarmonyPostfix]
        private static void Postfix(UnitAttack __instance, ref bool __result)
        {
            try
            {
                __result = TargetingState.FinishAttackRetarget(__instance, __result);
            }
            catch (Exception exception)
            {
                ArdubsEnemyIntelligenceMain.ModEntry.Logger.Error(exception.ToString());
            }
        }
    }

    [HarmonyPatch(typeof(UnitImpatienceController), nameof(UnitImpatienceController.HandleUnitCommandDidStart))]
    private static class ObserveCommandPatch
    {
        [HarmonyPostfix]
        private static void Postfix(UnitCommand command)
        {
            if (command.Type != UnitCommand.CommandType.Standard ||
                !(command is UnitAttack) && !(command is UnitUseAbility))
            {
                return;
            }

            Guard(() => TargetingState.ObserveCommand(command));
        }
    }

    [HarmonyPatch(typeof(RuleDealStatDamage), nameof(RuleDealStatDamage.OnTrigger))]
    private static class ObserveStatDamagePatch
    {
        [HarmonyPostfix]
        private static void Postfix(RuleDealStatDamage __instance) =>
            Guard(() => TargetingState.ObserveStatDamage(__instance));
    }

    [HarmonyPatch(typeof(RuleDrainEnergy), nameof(RuleDrainEnergy.OnTrigger))]
    private static class ObserveEnergyDrainPatch
    {
        [HarmonyPostfix]
        private static void Postfix(RuleDrainEnergy __instance) =>
            Guard(() => TargetingState.ObserveEnergyDrain(__instance));
    }

    [HarmonyPatch(typeof(UnitImpatienceController), nameof(UnitImpatienceController.OnEventDidTrigger),
        new[] { typeof(RuleDealDamage) })]
    private static class ObserveDamagePatch
    {
        [HarmonyPostfix]
        private static void Postfix(RuleDealDamage evt)
        {
            Guard(() => TargetingState.ObserveDamage(evt));
        }
    }
}
