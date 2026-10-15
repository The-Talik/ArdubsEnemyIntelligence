using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Controllers.Combat;
using Kingmaker.Controllers.Units;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.TurnBasedMode.Pathfinding;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Groups;
using Kingmaker.UnitLogic.Parts;
using Owlcat.Runtime.Core.Utils;
using Owlcat.Runtime.Visual.RenderPipeline.RendererFeatures.FogOfWar;
using Pathfinding;
using TurnBased.Controllers;
using UnityEngine;

namespace ArdubsEnemyIntelligence;

// Additional detection, not a replacement for native sight or combat joining.
// Queue awareness only after a safe observer sees and can reach an existing
// combatant. Vanilla retains its normal turn-transition and group-join rules.
internal static class ReinforcementDetection
{
    private static float nextCheck;
    private static int cursor;
    private static readonly Dictionary<UnitEntityData, int> TargetOffsets = new Dictionary<UnitEntityData, int>();
    private static readonly object PathOwner = new object();
    private const int PathsPerCheck = 2;

    internal static void Reset()
    {
        nextCheck = 0f;
        cursor = 0;
        TargetOffsets.Clear();
    }

    internal static bool Enabled(bool turnBased, bool passing) =>
        turnBased && passing && ArdubsEnemyIntelligenceMain.Settings.ApplyToEnemies &&
        ArdubsEnemyIntelligenceMain.Settings.ExpandReinforcementDetection &&
        ArdubsEnemyIntelligenceMain.Settings.ActiveReinforcementRangeMultiplier > 1f;

    internal static bool InAdditionalRange(float distanceSquared, float normalRange, float multiplier) =>
        distanceSquared > normalRange * normalRange &&
        distanceSquared <= (double)normalRange * multiplier * normalRange * multiplier;

    private static bool Protected(UnitEntityData unit) => unit.CutsceneControlledUnit != null ||
        unit.State.Features.IsIgnoredByCombat || unit.Passive || unit.Faction.NeverJoinCombat ||
        unit.Faction.Peaceful || unit.Stealth.InAmbush || unit.VisionRangeMetersOverride.HasValue ||
        unit.CombatVisionRangeMetersOverride.HasValue || unit.ExtendedVisionArea != null ||
        unit.Blueprint.GetComponent<UnitAggroFilter>() != null;

    private static bool EligibleObserver(UnitEntityData unit, UnitEntityData player) =>
        unit.IsInGame && !unit.IsDisposed && unit.IsAwake && unit.IsAIEnabled && unit.View != null &&
        !unit.IsInCombat && !unit.IsDirectlyControllable && !unit.Blueprint.IsFake &&
        unit.State.IsConscious && unit.State.CanAct && !unit.State.HasCondition(UnitCondition.Blindness) &&
        unit.IsEnemy(player) && !Protected(unit) &&
        !unit.Group.Any(member => member != null && !member.IsDisposed && member.IsInGame &&
            member.State.IsConscious && Protected(member));

    private static bool CanSee(UnitEntityData observer, UnitEntityData target)
    {
        if (target.InStealthFor(observer.Group)) return false;
        // Preserve native invisibility/blindsense handling, independently of
        // geometry. A successful route must never reveal a hidden character.
        if (target.State.HasCondition(UnitCondition.Invisible) && !observer.Descriptor.IsSeeInvisibility &&
            observer.Get<UnitPartBlindsense>()?.Reach(target) != true) return false;
        observer.CheckLOS(target.Position + LineOfSightGeometry.EyeShift, observer.Position,
            out _, out var geometryClear);
        return geometryClear;
    }

    internal static bool CompleteRoute(Path path, Vector3 target, float approachRange) =>
        path != null && !path.error && path.CompleteState == PathCompleteState.Complete &&
        path.vectorPath != null && path.vectorPath.Count > 0 &&
        (path.vectorPath[path.vectorPath.Count - 1] - target).sqrMagnitude <=
            (double)approachRange * approachRange;

    private static bool CanReach(UnitEntityData observer, UnitEntityData target)
    {
        var agent = observer.View.AgentASP;
        var threat = observer.GetThreatHand();
        var weapon = threat?.MaybeWeapon ?? observer.Body.PrimaryHand.MaybeWeapon;
        var reach = threat != null ? observer.GetThreatRange(threat) : null;
        var weaponRange = reach ?? weapon?.AttackRange.Meters ?? 1.524f;
        var approachRange = weaponRange + observer.View.Corpulence + target.View.Corpulence;
        var diameter = agent.AvoidanceDisabled ? 0f : observer.View.Corpulence * 2f;
        // Use the game's native clearance, tags, LOS ending condition and grid.
        // An independent path leaves the agent's seeker, requested paths and
        // commands untouched. No smoothing or straight-line fallback is used.
        var path = ABPathWithClearance.Construct(observer.Position, target.Position,
            new Vector3(diameter, 0f, diameter), null, approachRange, true, 0f, float.MaxValue);
        path.Claim(PathOwner);
        try
        {
            path.enabledTags = agent.ConstructTags();
            path.tagPenalties = agent.m_Seeker.tagPenalties;
            path.nnConstraint.graphMask = (GraphMask)2;
            AstarPath.StartPath(path);
            AstarPath.BlockUntilCalculated(path);
            return CompleteRoute(path, target.Position, approachRange);
        }
        finally { path.Release(PathOwner); }
    }

    private static void Check()
    {
        if (!Enabled(CombatController.IsInTurnBasedCombat(), CombatController.IsPassing()) ||
            Time.unscaledTime < nextCheck || Game.Instance?.State == null) return;
        nextCheck = Time.unscaledTime + 1f;
        var player = Game.Instance.Player?.GetMainPartyUnit();
        if (player == null) return;
        var units = Game.Instance.State.AwakeUnits.ToArray();
        if (units.Length == 0) return;
        var combatants = units.Where(unit => unit != null && !unit.IsDisposed && unit.IsInGame &&
            unit.View != null && unit.IsInCombat &&
            unit.State.IsConscious && !unit.Passive && !unit.Faction.Peaceful &&
            !unit.State.Features.IsIgnoredByCombat).ToArray();
        if (combatants.Length == 0) return;
        var alerted = new HashSet<UnitGroup>();
        var searches = 0;
        for (var visited = 0; visited < units.Length; visited++)
        {
            cursor %= units.Length;
            var observer = units[cursor++];
            if (observer == null || alerted.Contains(observer.Group) || !EligibleObserver(observer, player)) continue;
            var multiplier = ArdubsEnemyIntelligenceMain.Settings.ActiveReinforcementRangeMultiplier;
            var normalRange = observer.VisionRangeMeters;
            var targets = combatants.OrderBy(observer.SqrDistanceTo).ToArray();
            TargetOffsets.TryGetValue(observer, out var targetOffset);
            for (var checkedTargets = 0; checkedTargets < targets.Length; checkedTargets++)
            {
                var targetIndex = (targetOffset + checkedTargets) % targets.Length;
                var target = targets[targetIndex];
                if (!observer.IsEnemy(target) || observer.Memory.Contains(target) ||
                    !InAdditionalRange(GeometryUtils.SqrDistance2D(observer.Position, target.Position), normalRange, multiplier) ||
                    !UnitCombatJoinController.ShouldEngageEnemy(observer, target) || !CanSee(observer, target)) continue;
                searches++;
                TargetOffsets[observer] = (targetIndex + 1) % targets.Length;
                if (CanReach(observer, target))
                {
                    Game.Instance.UnitMemoryController.AddToMemory(observer, target);
                    alerted.Add(observer.Group);
                    TargetingLog.Write($"Reinforcements: {observer.CharacterName} spotted {target.CharacterName}; " +
                        $"range {LogNumber.Format((decimal)(normalRange * multiplier))}m " +
                        $"({LogNumber.Format((decimal)multiplier)} * normal {LogNumber.Format((decimal)normalRange)}m); " +
                        "visibility and complete native route verified; group awareness queued for vanilla combat joining");
                    break;
                }
                if (searches >= PathsPerCheck) return;
            }
            if (searches >= PathsPerCheck) return;
        }
    }

    [HarmonyPatch(typeof(UnitCombatJoinController), nameof(UnitCombatJoinController.Tick))]
    private static class JoinTransition
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            var start = PerformanceDiagnostics.Start();
            try { Check(); }
            catch (Exception exception)
            {
                ArdubsEnemyIntelligenceMain.ModEntry.Logger.Error("Reinforcement detection: " + exception);
            }
            finally { PerformanceDiagnostics.End(PerformanceDiagnostics.Stage.Reinforcements, start); }
        }
    }
}
