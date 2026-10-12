using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Kingmaker.AI;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.TurnBasedMode;
using Kingmaker.TurnBasedMode.Pathfinding;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.UnitLogic.Parts;
using Kingmaker.Utility;
using Pathfinding;
using TurnBased.Controllers;
using UnityEngine;

namespace ArdubsEnemyIntelligence;

// Sticky-touch spells cast first, then queue a separate delivery command. The
// game does not copy the original route to that command; without it, nearby
// player heals and AI attacks can abort with a null-forced-path error.
[HarmonyPatch(typeof(AbilityEffectStickyTouch), nameof(AbilityEffectStickyTouch.Apply))]
internal static class TouchDeliveryPath
{
    private sealed class UnitReferenceComparer : IEqualityComparer<UnitEntityData>
    {
        internal static readonly UnitReferenceComparer Instance = new UnitReferenceComparer();
        public bool Equals(UnitEntityData? left, UnitEntityData? right) => ReferenceEquals(left, right);
        public int GetHashCode(UnitEntityData unit) => RuntimeHelpers.GetHashCode(unit);
    }

    private sealed class PendingCast
    {
        internal readonly UnitEntityData Target;
        internal readonly BlueprintAbility Blueprint;
        internal readonly Path Path;

        internal PendingCast(UnitEntityData target, BlueprintAbility blueprint, Path path)
        {
            Target = target;
            Blueprint = blueprint;
            Path = path;
            Path.Claim(this);
        }

        internal void Release() => Path.Release(this);
    }

    private static readonly Dictionary<UnitEntityData, PendingCast> Pending =
        new Dictionary<UnitEntityData, PendingCast>(UnitReferenceComparer.Instance);

    internal static void Clear()
    {
        foreach (var cast in Pending.Values) cast.Release();
        Pending.Clear();
    }

    internal static void Forget(UnitEntityData actor)
    {
        if (!Pending.TryGetValue(actor, out var cast)) return;
        Pending.Remove(actor);
        cast.Release();
    }

    internal static void Remember(UnitCommand command)
    {
        if (command is not UnitUseAbility cast ||
            cast.Ability?.Blueprint.StickyTouch == null || cast.TargetUnit is not { } target ||
            !Handles(cast.Executor, target)) return;
        Forget(cast.Executor);
        var path = cast.ForcedPath;
        if (!Complete(path) && cast.Executor.IsDirectlyControllable)
            path = PreviewPath(cast, target);
        if (!Complete(path))
        {
            if (ArdubsEnemyIntelligenceMain.Settings.ShowBodyBlockingInLog)
                TargetingLog.Write($"Pathfinding: {cast.Executor.CharacterName} queued {cast.Ability.Name} " +
                    $"against {target.CharacterName} without a reusable touch-delivery path");
            return;
        }
        Pending.Add(cast.Executor, new PendingCast(target, cast.Ability.Blueprint, path!));
    }

    private static bool Handles(UnitEntityData caster, UnitEntityData target) =>
        TargetingState.IsManaged(caster) && caster.IsEnemy(target) ||
        ArdubsEnemyIntelligenceMain.Settings.BodyBlockingEnabled && caster.IsDirectlyControllable;

    private static bool Complete(Path? path) => path != null && !path.error &&
        path.CompleteState == PathCompleteState.Complete && path.vectorPath?.Count > 0;

    private static Path? PreviewPath(UnitUseAbility cast, UnitEntityData target)
    {
        var preview = PathVisualizer.Instance;
        var data = preview?.m_LastUpdateData;
        if (data == null || !ReferenceEquals(data.Settings.Unit, cast.Executor) ||
            (data.ApproachPoint - target.Position).sqrMagnitude > 0.25f) return null;
        var path = preview!.CurrentPathForUnit(cast.Executor.View);
        return path is ABPathWithClearance approach &&
            (approach.originalEndPoint - target.Position).sqrMagnitude <= 0.25f ? path : null;
    }

    private static bool ReachesDelivery(UnitUseAbility delivery, Path? path)
    {
        if (!Complete(path)) return false;
        var endpoint = path!.vectorPath[path.vectorPath.Count - 1];
        return UnitCommand.IsUnitCloseEnough(delivery.ApproachPoint, endpoint,
            delivery.Executor.EyePosition + endpoint - delivery.Executor.Position,
            delivery.ApproachRadius, delivery.NeedLoS, delivery.GetTargetLOSObjectFudgeRadius());
    }

    private static void RecoverNearbyPlayerPath(UnitUseAbility delivery, UnitEntityData target)
    {
        var caster = delivery.Executor;
        if (!ArdubsEnemyIntelligenceMain.Settings.BodyBlockingEnabled || !caster.IsDirectlyControllable ||
            !CombatController.IsInTurnBasedCombat() || delivery.IsUnitCloseEnough() ||
            caster.View == null || target.View == null ||
            Vector3.Distance(caster.Position, target.Position) -
                caster.View.Corpulence - target.View.Corpulence > 3f) return;
        var agent = caster.View?.AgentASP;
        if (agent == null) return;
        var path = agent.FindPath(delivery.ApproachPoint, null, delivery.ApproachRadius,
            delivery.NeedLoS, delivery.GetTargetLOSObjectFudgeRadius());
        if (path == null) return;
        var claim = new object();
        path.Claim(claim);
        try
        {
            AstarPath.BlockUntilCalculated(path);
            if (!ReachesDelivery(delivery, path)) return;
            delivery.ForcedPath = path;
            if (ArdubsEnemyIntelligenceMain.Settings.ShowBodyBlockingInLog)
                TargetingLog.Write($"Pathfinding: {caster.CharacterName} recovered a native " +
                    $"{path.vectorPath.Count}-point route to deliver {delivery.Ability.Name} to {target.CharacterName}");
        }
        finally { path.Release(claim); }
    }

    internal static bool IsManagedOffensiveCast(DecisionContext context) =>
        context.Ability?.Blueprint.StickyTouch != null &&
        context.Target?.Unit is { } target && context.Unit.IsEnemy(target) &&
        TargetingState.IsManaged(context.Unit);

    private static void Transfer(AbilityExecutionContext context, TargetWrapper target)
    {
        if (context.MaybeCaster is not { } caster || target.Unit == null ||
            !Handles(caster, target.Unit)) return;
        var delivery = caster.Get<UnitPartTouch>()?.AutoCastCommand as UnitUseAbility;
        if (delivery == null || !ReferenceEquals(delivery.TargetUnit, target.Unit) ||
            delivery.ForcedPath != null) return;
        if (Pending.TryGetValue(caster, out var pending))
        {
            Pending.Remove(caster);
            try
            {
                if (ReferenceEquals(pending.Target, target.Unit) && context.Ability != null &&
                    pending.Blueprint.AssetGuid == context.Ability.Blueprint.AssetGuid &&
                    ReachesDelivery(delivery, pending.Path))
                {
                    // The setter claims the path before our temporary claim ends.
                    delivery.ForcedPath = pending.Path;
                    if (ArdubsEnemyIntelligenceMain.Settings.ShowBodyBlockingInLog)
                        TargetingLog.Write($"Pathfinding: {caster.CharacterName} passed the selected " +
                            $"{pending.Path.vectorPath.Count}-point path to {delivery.Ability.Name} touch delivery " +
                            $"against {target.Unit.CharacterName}");
                }
            }
            finally { pending.Release(); }
        }
        if (delivery.ForcedPath == null)
        {
            RecoverNearbyPlayerPath(delivery, target.Unit);
            if (delivery.ForcedPath == null && !delivery.IsUnitCloseEnough() &&
                ArdubsEnemyIntelligenceMain.Settings.ShowBodyBlockingInLog)
                TargetingLog.Write($"Pathfinding: {caster.CharacterName} created {delivery.Ability.Name} " +
                    $"touch delivery against {target.Unit.CharacterName} without a usable path");
        }
    }

    [HarmonyPostfix]
    private static void Postfix(AbilityExecutionContext __0, TargetWrapper __1)
    {
        try { Transfer(__0, __1); }
        catch (Exception exception)
        {
            ArdubsEnemyIntelligenceMain.ModEntry.Logger.Error("Touch path transfer: " + exception);
        }
    }
}
