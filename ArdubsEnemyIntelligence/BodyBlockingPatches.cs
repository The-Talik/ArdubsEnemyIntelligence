using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Kingmaker.TurnBasedMode;
using Kingmaker.TurnBasedMode.Pathfinding;
using Kingmaker.View;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using Pathfinding;
using UnityEngine;

namespace ArdubsEnemyIntelligence;

internal static class BodyBlockingPatches
{
    private static void Guard(Action action)
    {
        try { action(); }
        catch (Exception exception) { ArdubsEnemyIntelligenceMain.ModEntry.Logger.Error("Body blocking: " + exception); }
    }

    private static ABPathWithClearance Attach(ABPathWithClearance path, SeekerWithClearance seeker)
    {
        var start = PerformanceDiagnostics.Start();
        try { Guard(() => BodyBlocking.Attach(path, seeker)); }
        finally { PerformanceDiagnostics.End(PerformanceDiagnostics.Stage.PathSetup, start); }
        return path;
    }

    [HarmonyPatch(typeof(SeekerWithClearance), nameof(SeekerWithClearance.StartPathWithClerance))]
    private static class AttachBeforeSearch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var construct = AccessTools.Method(typeof(ABPathWithClearance), nameof(ABPathWithClearance.Construct));
            var matches = 0;
            foreach (var code in instructions)
            {
                yield return code;
                if (!code.Calls(construct)) continue;
                matches++;
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BodyBlockingPatches), nameof(Attach)));
            }
            if (matches != 1) throw new InvalidOperationException("Cannot attach body clearance before the native path search.");
        }
    }

    [HarmonyPatch(typeof(ABPathWithClearance), nameof(ABPathWithClearance.CanTraverse),
        new[] { typeof(Path), typeof(GraphNode) })]
    private static class Traversal
    {
        [HarmonyPostfix]
        private static void Postfix(Path __0, GraphNode __1, ref bool __result)
        {
            if (!__result) return; // Never relax native terrain/tag restrictions.
            var snapshot = BodyBlocking.For(__0);
            if (snapshot != null)
                __result = BodyBlockingPolicy.CanTraverse(BodyBlocking.ToPoint((Vector3)__1.position),
                    snapshot.Origin, snapshot.Bodies);
        }
    }

    [HarmonyPatch]
    private static class GridConnectionClearance
    {
        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(GridNode), nameof(GridNode.Open));
            yield return AccessTools.Method(typeof(LevelGridNode), nameof(LevelGridNode.Open));
        }

        private static bool CanTraverseEdge(bool native, Path path, GraphNode next, GraphNode from)
        {
            if (!native) return false;
            var snapshot = BodyBlocking.For(path);
            return snapshot == null || BodyBlockingPolicy.CanTraverseEdge(
                BodyBlocking.ToPoint((Vector3)from.position),
                BodyBlocking.ToPoint((Vector3)next.position), snapshot.Bodies);
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var native = AccessTools.Method(typeof(Path), "CanTraverse", new[] { typeof(GraphNode) });
            var precise = AccessTools.Method(typeof(GridConnectionClearance), nameof(CanTraverseEdge));
            var codes = instructions.ToList();
            var matches = 0;
            for (var i = 0; i < codes.Count; i++)
            {
                var instruction = codes[i];
                yield return instruction;
                if (!instruction.Calls(native)) continue;
                if (i == 0 || !codes[i - 1].opcode.Name.StartsWith("ldloc", StringComparison.Ordinal))
                    throw new InvalidOperationException("Cannot identify the native grid-connection destination.");
                matches++;
                yield return new CodeInstruction(OpCodes.Ldarg_1);
                yield return new CodeInstruction(codes[i - 1].opcode, codes[i - 1].operand);
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, precise);
            }
            if (matches != 1)
                throw new InvalidOperationException("Cannot replace the native grid-connection clearance check.");
        }
    }

    [HarmonyPatch(typeof(RaycastModifier), nameof(RaycastModifier.Apply))]
    private static class SmoothingScope
    {
        private readonly struct BeforeSmoothing
        {
            internal readonly BodyBlocking.Snapshot? Previous;
            internal readonly Vector3[]? Route;
            internal BeforeSmoothing(BodyBlocking.Snapshot? previous, Vector3[]? route)
            {
                Previous = previous;
                Route = route;
            }
        }

        [HarmonyPrefix]
        private static void Prefix(Path __0, out BeforeSmoothing __state)
        {
            var snapshot = BodyBlocking.For(__0);
            __state = new BeforeSmoothing(BodyBlocking.Smoothing,
                snapshot != null && __0.vectorPath != null ? __0.vectorPath.ToArray() : null);
            BodyBlocking.Smoothing = snapshot;
        }
        [HarmonyPostfix]
        private static void Postfix(Path __0, BeforeSmoothing __state)
        {
            try
            {
                Guard(() =>
                {
                    var restored = BodyBlocking.RestoreUnsafeRaycast(__0, __state.Route);
                    BodyBlocking.RecordAfterRaycast(__0, restored);
                });
            }
            finally { BodyBlocking.Smoothing = __state.Previous; }
        }
        [HarmonyFinalizer]
        private static Exception? Finalizer(Exception? __exception, BeforeSmoothing __state)
        {
            if (__exception != null) BodyBlocking.Smoothing = __state.Previous;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(ABPathWithClearance.LosPathConditionForGrid), nameof(ABPathWithClearance.LosPathConditionForGrid.TargetFound))]
    private static class EndpointClearance
    {
        [HarmonyPostfix]
        private static void Postfix(ABPath ___abPath, PathNode __0, ref bool __result)
        {
            var snapshot = BodyBlocking.For(___abPath);
            if (__result && snapshot?.PreventOverlap == true)
                __result = BodyBlockingPolicy.CanStopAction(snapshot.Origin, BodyBlocking.ToPoint(__0.node == ___abPath.startNode
                    ? ___abPath.originalStartPoint : (Vector3)__0.node.position), snapshot.Bodies);
        }
    }

    [HarmonyPatch(typeof(StartEndModifier), "Snap")]
    private static class PreserveApproachEndpoint
    {
        [HarmonyPostfix]
        private static void Postfix(ABPath __0, bool __2, ref bool __3, ref int __4, ref Vector3 __result)
        {
            try
            {
                var endpoint = BodyBlocking.RestoreApproachEndpoint(__0, __2, __result,
                    out var restored, out var snappedToNode);
                if (!restored) return;
                __result = endpoint;
                if (snappedToNode) { __3 = false; __4 = 0; } // Native SnapToNode bookkeeping.
            }
            catch (Exception exception) { ArdubsEnemyIntelligenceMain.ModEntry.Logger.Error("Body blocking endpoint: " + exception); }
        }
    }

    [HarmonyPatch(typeof(UnitCommand), nameof(UnitCommand.Tick))]
    private static class PendingSpellApproach
    {
        [HarmonyPrefix]
        private static bool Prefix(UnitCommand __instance)
        {
            try { return SpellApproach.ContinueApproaching(__instance); }
            catch (Exception exception)
            {
                ArdubsEnemyIntelligenceMain.ModEntry.Logger.Error("Body blocking spell approach: " + exception);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(UnitCommand), nameof(UnitCommand.IsUnitCloseEnough), new Type[] { })]
    private static class AttackStopClearance
    {
        [HarmonyPrefix]
        private static void Prefix(UnitCommand __instance) =>
            // Covers restored commands too, before the first approach tick.
            Guard(() => BodyBlocking.RememberAttackOrigin(__instance));

        [HarmonyPostfix]
        private static void Postfix(UnitCommand __instance, ref bool __result)
        {
            if (!__result || !(__instance is UnitAttack)) return;
            try { __result = BodyBlocking.CanStopAttackApproach(__instance); }
            catch (Exception exception) { ArdubsEnemyIntelligenceMain.ModEntry.Logger.Error("Body blocking attack stop: " + exception); }
        }
    }

    [HarmonyPatch(typeof(PathVisualizer), "UpdatePath")]
    private static class PreviewPathDiagnostics
    {
        private readonly struct Before
        {
            internal readonly bool Requested;
            internal readonly long Started;
            internal Before(bool requested, long started) { Requested = requested; Started = started; }
        }

        [HarmonyPrefix]
        private static void Prefix(PathVisualizer __instance, Path __0, out Before __state) =>
            __state = new Before(ReferenceEquals(__instance.m_RequestedPath, __0), PerformanceDiagnostics.Start());

        [HarmonyPostfix]
        private static void Postfix(PathVisualizer __instance, Path __0, Vector3 __1, Before __state)
        {
            if (__state.Requested) Guard(() => BodyBlocking.LogPreviewPath(__instance, __0, __1));
            PerformanceDiagnostics.End(PerformanceDiagnostics.Stage.PreviewUpdate, __state.Started);
        }
    }

    [HarmonyPatch(typeof(PathVisualizer), nameof(PathVisualizer.CommitRealPath))]
    private static class PreserveClearApproachRoute
    {
        [HarmonyPrefix]
        private static bool Prefix(PathVisualizer __instance)
        {
            try { return BodyBlocking.CommitPreviewReplacement(__instance); }
            catch (Exception exception)
            {
                ArdubsEnemyIntelligenceMain.ModEntry.Logger.Error("Body blocking preview commit: " + exception);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(UnitMovementAgent), "OnPathComplete", new[] { typeof(Path), typeof(bool) })]
    private static class CommittedPathDiagnostics
    {
        [HarmonyPrefix]
        private static void Prefix(UnitMovementAgent __instance, Path __0, out bool __state) =>
            __state = ReferenceEquals(__instance.RequestedPath, __0);

        [HarmonyPostfix]
        private static void Postfix(UnitMovementAgent __instance, Path __0, bool __state)
        {
            if (!__state) return;
            Guard(() =>
            {
                BodyBlocking.LogCommittedPath(__instance, __0);
                BodyBlocking.LogLivePathFailure(__instance, __0);
            });
        }
    }

    [HarmonyPatch(typeof(UnitMovementAgent), nameof(UnitMovementAgent.FollowPrecomputedPath))]
    private static class PrecomputedCommandRoute
    {
        [HarmonyPrefix]
        private static void Prefix(UnitMovementAgent __instance, Path __0) =>
            Guard(() => BodyBlocking.PreparePrecomputedPath(__instance, __0));
    }

    [HarmonyPatch(typeof(UnitMovementAgent), nameof(UnitMovementAgent.ForcePath))]
    private static class ForcedCommandRoute
    {
        [HarmonyPrefix]
        private static void Prefix(UnitMovementAgent __instance, Path __0) =>
            Guard(() => BodyBlocking.PreparePrecomputedPath(__instance, __0));
    }

    [HarmonyPatch(typeof(RaycastModifier), "ValidateLine")]
    private static class SmoothingClearance
    {
        [HarmonyPostfix]
        private static void Postfix(Vector3 __2, Vector3 __3, ref bool __result)
        {
            var snapshot = BodyBlocking.Smoothing;
            if (__result && snapshot != null && ArdubsEnemyIntelligenceMain.Settings.BodyBlockingEnabled)
                __result = BodyBlockingPolicy.PassageFraction(BodyBlocking.ToPoint(__2),
                    BodyBlocking.ToPoint(__3), snapshot.Bodies) >= 1;
        }
    }

    [HarmonyPatch(typeof(Seeker), nameof(Seeker.RunModifiers))]
    private static class FinalRoute
    {
        [HarmonyPrefix]
        private static void Prefix(Seeker.ModifierPass __0, Path __1)
        {
            if (__0 == Seeker.ModifierPass.PostProcess) Guard(() => BodyBlocking.RecordBeforeModifiers(__1));
        }

        [HarmonyPostfix]
        private static void Postfix(Seeker.ModifierPass __0, Path __1)
        {
            if (__0 != Seeker.ModifierPass.PostProcess) return;
            var start = PerformanceDiagnostics.Start();
            try { Guard(() => BodyBlocking.FinishRoute(__1)); }
            finally { PerformanceDiagnostics.End(PerformanceDiagnostics.Stage.RouteFinalization, start); }
        }
    }

    [HarmonyPatch(typeof(UnitMovementAgent), nameof(UnitMovementAgent.SimulateTickMovementForTurnBased))]
    private static class SimulationClearance
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var codes = instructions.ToList();
            var position = AccessTools.PropertySetter(typeof(MovementSimulator), nameof(MovementSimulator.Position));
            var matches = 0;
            for (var i = 0; i + 6 < codes.Count; i++)
            {
                // The native range/LOS success branch copies the candidate
                // stopping point into the movement result, sets its stop flag,
                // then commits Position. Do not change range, LOS or budgets.
                if (codes[i].opcode != OpCodes.Ldloc_S || codes[i + 1].opcode != OpCodes.Stloc_S ||
                    codes[i + 2].opcode != OpCodes.Ldc_I4_1 || codes[i + 3].opcode != OpCodes.Stloc_S ||
                    codes[i + 4].opcode != OpCodes.Ldarg_1 || codes[i + 5].opcode != OpCodes.Ldloc_S ||
                    !Equals(codes[i + 1].operand, codes[i + 5].operand) || !codes[i + 6].Calls(position)) continue;
                matches++;
                var continueTick = generator.DefineLabel();
                codes[i + 4].labels.Add(continueTick);
                var loadSimulator = new CodeInstruction(OpCodes.Ldarg_1);
                loadSimulator.labels.AddRange(codes[i].labels);
                codes[i].labels.Clear();
                codes.InsertRange(i, new[]
                {
                    loadSimulator,
                    new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(MovementSimulator), nameof(MovementSimulator.Path))),
                    new CodeInstruction(codes[i].opcode, codes[i].operand),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BodyBlocking), nameof(BodyBlocking.CanStopSimulationApproach))),
                    new CodeInstruction(OpCodes.Brfalse, continueTick)
                });
                i += 5;
            }
            if (matches != 1)
                throw new InvalidOperationException("Cannot guard the native simulation's in-range stopping point.");
            return codes;
        }

        private readonly struct BeforeTick
        {
            internal readonly Vector3 Position;
            internal readonly float Budget;
            internal BeforeTick(Vector3 position, float budget) { Position = position; Budget = budget; }
        }
        [HarmonyPrefix]
        private static void Prefix(ref MovementSimulator __0, float __2, out BeforeTick __state) =>
            __state = new BeforeTick(__0.Position, __2);
        [HarmonyPostfix]
        private static void Postfix(UnitMovementAgent __instance, ref MovementSimulator __0,
            BeforeTick __state, ref bool __result)
        {
            // The native method subtracts a waypoint length from its argument
            // with starg. Harmony's postfix sees that mutated (possibly negative)
            // argument, not the original movement budget. Preserve it in prefix.
            try { BodyBlocking.GuardSimulation(__instance, ref __0, __state.Position, __state.Budget, ref __result); }
            catch (Exception exception) { ArdubsEnemyIntelligenceMain.ModEntry.Logger.Error("Body blocking simulation: " + exception); }
        }
    }

    [HarmonyPatch(typeof(UnitMovementAgentBase), nameof(UnitMovementAgentBase.MoveUnit))]
    private static class LiveClearance
    {
        [HarmonyPostfix]
        private static void Postfix(UnitMovementAgentBase __instance, Vector3 __0, ref Vector3 __result)
        {
            if (!(__instance is UnitMovementAgent agent)) return;
            try
            {
                __result = BodyBlocking.GuardMove(agent, __0, __result);
                FlyingPassageVisual.ObserveMovement(agent, __result, BodyBlocking.CaptureVisual(agent));
            }
            catch (Exception exception) { ArdubsEnemyIntelligenceMain.ModEntry.Logger.Error("Body blocking movement: " + exception); }
        }
    }

    [HarmonyPatch(typeof(UnitMovementAgent), nameof(UnitMovementAgent.CanMoveInTurnBased))]
    private static class StopBeforeNextTick
    {
        [HarmonyPrefix]
        private static void Prefix(UnitMovementAgent __instance) => Guard(() => BodyBlocking.FinishMovementTick(__instance));
    }
}
