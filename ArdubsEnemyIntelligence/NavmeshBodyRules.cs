using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Pathfinding;
using TurnBased.Controllers;

namespace ArdubsEnemyIntelligence;

// Constrain only the native per-unit occupancy updates, never terrain flags.
// Native RevertNavmeshForUnit reverses these same updates, keeping node counters
// balanced across turns, condition changes and the mod's setting changes.
internal static class NavmeshBodyRules
{
    private sealed class Applied
    {
        internal readonly GraphUpdateObject Update;
        internal readonly bool Skipped;
        internal Applied(GraphUpdateObject update, bool skipped) { Update = update; Skipped = skipped; }
    }

    private static readonly ConditionalWeakTable<CombatController.TBUnitInfo, Applied> Updates =
        new ConditionalWeakTable<CombatController.TBUnitInfo, Applied>();

    private static bool ShouldSkip(CombatController controller, CombatController.TBUnitInfo info)
    {
        if (!ArdubsEnemyIntelligenceMain.Settings.BodyBlockingEnabled) return false;
        if (!BodyBlocking.OccupiesSpace(info.Unit)) return true;
        var turn = controller.CurrentTurn;
        var mover = turn?.Mount ?? turn?.Rider;
        if (mover == null) return false;
        if (BodyBlocking.IsFriendlyForMovement(mover, info.Unit) && !BodyBlockingPolicy.BlocksStopping(true,
            turn!.GetEnabledFiveFootStep(mover), ArdubsEnemyIntelligenceMain.Settings.IgnoreFriendlyBlockingOnFiveFootStep,
            ArdubsEnemyIntelligenceMain.Settings.AllowFriendlyStoppingOverlap)) return true;
        return ArdubsEnemyIntelligenceMain.Settings.AllowFlyingEnemyPassage && mover.State.Features.Flying &&
            info.Unit.IsEnemy(turn!.Rider);
    }

    internal static void SuppressBodyUpdate(GraphUpdateObject update)
    {
        update.modifyDynamicWalkability = false;
        update.modifyAllyBlocked = false;
        update.modifyTag = false;
    }

    internal static void RefreshBeforePath()
    {
        if (!CombatController.IsInTurnBasedCombat()) return;
        var controller = Kingmaker.Game.Instance.TurnBasedCombatController;
        var astar = AstarPath.active;
        if (controller.CurrentTurn == null || astar == null) return;
        var changed = false;
        foreach (var info in controller.m_Units)
        {
            // Do not rebuild the current mover's excluded/native untracked entry.
            if (info.LastNavmeshUpdateObject == null || !Updates.TryGetValue(info, out var applied) ||
                !ReferenceEquals(applied.Update, info.LastNavmeshUpdateObject) ||
                applied.Skipped == ShouldSkip(controller, info)) continue;
            controller.RevertNavmeshForUnit(info);
            controller.PrepareUnitNavmeshUpdateObject(info);
            astar.UpdateGraphs(info.LastNavmeshUpdateObject);
            changed = true;
        }
        // Only on an occupancy-rule change, not on each path/candidate. Queued
        // updates must finish before worker threads search the changed grid.
        if (changed) astar.FlushGraphUpdates();
    }

    [HarmonyPatch(typeof(CombatController), "PrepareUnitNavmeshUpdateObject")]
    private static class Prepare
    {
        [HarmonyPostfix]
        private static void Postfix(CombatController __instance, CombatController.TBUnitInfo __0)
        {
            try
            {
                var update = __0.LastNavmeshUpdateObject;
                if (update == null) return;
                var skipped = ShouldSkip(__instance, __0);
                if (skipped) SuppressBodyUpdate(update);
                Updates.Remove(__0);
                Updates.Add(__0, new Applied(update, skipped));
            }
            catch (Exception exception) { ArdubsEnemyIntelligenceMain.ModEntry.Logger.Error("Native body occupancy: " + exception); }
        }
    }
}
