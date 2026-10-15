using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Kingmaker.AI;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Settings;
using Kingmaker.TurnBasedMode;
using Kingmaker.TurnBasedMode.Controllers;
using Kingmaker.TurnBasedMode.Pathfinding;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Parts;
using Kingmaker.View;
using Owlcat.Runtime.Core.Utils;
using Owlcat.Runtime.Visual.RenderPipeline.RendererFeatures.FogOfWar;
using Pathfinding;
using TurnBased.Controllers;
using UnityEngine;
using Point = ArdubsEnemyIntelligence.BodyBlockingPolicy.Point;
using Body = ArdubsEnemyIntelligence.BodyBlockingPolicy.Body;

namespace ArdubsEnemyIntelligence;

internal static class BodyBlocking
{
    private static readonly ConditionalWeakTable<Path, Snapshot> Paths = new ConditionalWeakTable<Path, Snapshot>();
    private static readonly ConditionalWeakTable<UnitCommand, CommandOrigin> AttackOrigins =
        new ConditionalWeakTable<UnitCommand, CommandOrigin>();
    private sealed class CommandOrigin
    {
        internal readonly Point Position;
        internal CommandOrigin(Point position) { Position = position; }
    }
    [ThreadStatic] internal static Snapshot? Smoothing;
    private static readonly ConditionalWeakTable<UnitMovementAgent, BlockedStop> Stopped =
        new ConditionalWeakTable<UnitMovementAgent, BlockedStop>();
    private static readonly ConditionalWeakTable<UnitMovementAgent, MovementTrace> MovementTraces =
        new ConditionalWeakTable<UnitMovementAgent, MovementTrace>();
    private static int nextTraceId;
    private sealed class MovementTrace
    {
        internal Path? Path;
        internal int TraceId;
    }
    private sealed class BlockedStop
    {
        internal readonly Path? Path;
        internal readonly UnitUseAbility? Cast;
        internal readonly Snapshot Snapshot;
        internal readonly Vector3 From;
        internal readonly Vector3 Attempted;
        internal readonly double Allowed;
        internal readonly double Budget;
        internal BlockedStop(Path? path, UnitUseAbility? cast, Snapshot snapshot,
            Vector3 from, Vector3 attempted, double allowed, double budget)
        {
            Path = path; Cast = cast; Snapshot = snapshot; From = from;
            Attempted = attempted; Allowed = allowed; Budget = budget;
        }
    }

    internal sealed class Snapshot
    {
        internal readonly Point Origin;
        internal readonly Body[] Bodies;
        internal readonly Body[] FlyoverBodies;
        internal readonly bool PreventOverlap;
        internal bool CaptureDiagnostics;
        internal bool ClearanceLimited;
        internal int TraceId;
        internal Vector3? RouteBeforeTrimEnd;
        internal double RouteBeforeTrimLength;
        internal double RouteAllowedLength;
        internal string? SearchGraph;
        internal string? BeforeModifiers;
        internal Vector3[]? UnmodifiedRoute;
        internal bool? UnmodifiedRouteClear;
        internal string? AfterRaycast;
        internal bool RestoredUnmodifiedRoute;
        internal int OccupiedApproachStopsRejected;
        internal Snapshot(Point origin, Body[] bodies, Body[] flyoverBodies, bool preventOverlap)
        {
            Origin = origin;
            Bodies = bodies;
            FlyoverBodies = flyoverBodies;
            PreventOverlap = preventOverlap;
        }
    }

    internal readonly struct RouteStop
    {
        internal readonly double Planned;
        internal readonly double Allowed;
        internal readonly Vector3 Position;
        internal bool Limited => Allowed < Planned - 0.0001;
        internal RouteStop(double planned, double allowed, Vector3 position)
        {
            Planned = planned;
            Allowed = allowed;
            Position = position;
        }
    }

    internal static Point ToPoint(Vector3 v) => new Point(v.x, v.z, v.y);
    // Player IsAlly only recognizes party/summoned assets. Friendly outside-
    // party NPCs recognize the player in the reverse direction via faction.
    // Hostility takes precedence; merely neutral units are not exempted.
    internal static bool IsFriendlyForMovement(UnitEntityData actor, UnitEntityData other) =>
        BodyBlockingPolicy.IsFriendlyForMovement(actor.IsEnemy(other), other.IsEnemy(actor),
            actor.IsAlly(other), other.IsAlly(actor));

    private static bool Special(UnitEntityData unit) => unit.Get<UnitPartSwarmAttack>() != null ||
        unit.Get<UnitPartLocustSwarm>() != null || OnHitAssessment.Facts(unit)
            .SelectMany(f => f.BlueprintComponents).OfType<AddIncorporealDamageDivisor>().Any();

    internal static bool OccupiesSpace(UnitEntityData unit) => BodyBlockingPolicy.OccupiesSpace(
        unit.State.IsConscious, unit.State.HasCondition(UnitCondition.Sleeping),
        unit.State.HasCondition(UnitCondition.Unconscious), ArdubsEnemyIntelligenceMain.Settings.IgnoreDownedUnitSpace);

    internal static void RememberAttackOrigin(UnitCommand command)
    {
        if (!(command is UnitAttack)) return;
        var actor = command.Executor?.RiderPart?.SaddledUnit ?? command.Executor;
        if (actor?.View != null) RememberAttackOriginAt(command, ToPoint(actor.Position));
    }

    internal static void RememberAttackOriginAt(UnitCommand command, Point position)
    {
        // Repeated range checks or resubmission must not turn an occupied
        // arrival into the command's origin. Weak keys need no save-state data.
        if (command is UnitAttack) AttackOrigins.GetValue(command, _ => new CommandOrigin(position));
    }

    internal static bool CanStopAttackAt(UnitCommand command, Point current, Snapshot snapshot) =>
        !snapshot.PreventOverlap || (AttackOrigins.TryGetValue(command, out var origin)
            ? BodyBlockingPolicy.CanStopAction(origin.Position, current, snapshot.Bodies)
            : BodyBlockingPolicy.CanStop(current, snapshot.Bodies));

    // An attack can become close enough before its planned path ends. Flying
    // passage permits crossing an enemy, but that intermediate position must
    // not become the command's stopping point while newly occupied. Acting at
    // the original position remains legal even if another unit overlaps it.
    internal static bool CanStopAttackApproach(UnitCommand command)
    {
        if (!ArdubsEnemyIntelligenceMain.Settings.BodyBlockingEnabled || !ArdubsEnemyIntelligenceMain.Settings.PreventStoppingOverlap ||
            !(command is UnitAttack)) return true;
        var actor = command.Executor?.RiderPart?.SaddledUnit ?? command.Executor;
        if (actor?.View == null) return true;
        var snapshot = Capture(actor.View.AgentASP);
        return snapshot == null || CanStopAttackAt(command, ToPoint(actor.Position), snapshot);
    }

    internal static bool UsesFiveFootStep(UnitEntityData unit)
    {
        var turn = Kingmaker.Game.Instance.TurnBasedCombatController.CurrentTurn;
        return turn != null && (ReferenceEquals(unit, turn.Rider) || ReferenceEquals(unit, turn.Mount)) &&
            turn.GetEnabledFiveFootStep(unit);
    }

    private static bool Applies(UnitMovementAgent? agent)
    {
        var unit = agent?.Unit?.EntityData;
        return ArdubsEnemyIntelligenceMain.Settings.BodyBlockingEnabled && CombatController.IsInTurnBasedCombat() &&
            unit != null && unit.IsInCombat && !agent!.m_IsInForceMode &&
            unit.CutsceneControlledUnit?.GetCurrentlyActive() == null &&
            (!agent.IsCharging || ArdubsEnemyIntelligenceMain.Settings.BodyBlockingCharges) &&
            (!ArdubsEnemyIntelligenceMain.Settings.PreserveSpecialBodyMovement || !agent.AvoidanceDisabled && !Special(unit));
    }

    private static Snapshot? Capture(UnitMovementAgent? agent)
    {
        if (!Applies(agent)) return null;
        var actor = agent!.Unit.EntityData;
        // Use the active mechanics feature, not FlyHeight or wing animations.
        // Only passage changes: flying units still use occupied-endpoint checks.
        bool flying = actor.State.Features.Flying;
        var fiveFootStep = !agent.IsCharging && UsesFiveFootStep(actor);
        var bodies = new List<Body>();
        var flyoverBodies = new List<Body>();
        foreach (var other in Kingmaker.Game.Instance.State.Units)
        {
            if (ReferenceEquals(other, actor) || other.View == null || !other.IsInCombat ||
                !OccupiesSpace(other) || ReferenceEquals(other, actor.RiderPart?.SaddledUnit) ||
                ReferenceEquals(other, actor.SaddledPart?.Rider) || other.RiderPart?.SaddledUnit != null ||
                Math.Abs(other.Position.y - actor.Position.y) > 2f) continue;
            var ally = IsFriendlyForMovement(actor, other);
            if (!ally && !actor.Memory.ContainsVisible(other)) continue;
            if (ArdubsEnemyIntelligenceMain.Settings.PreserveSpecialBodyMovement && (other.View.AgentASP.AvoidanceDisabled || Special(other))) continue;
            var radius = agent.Corpulence + other.View.Corpulence + 0.01f;
            if (radius <= 0 || float.IsNaN(radius) || float.IsInfinity(radius))
                throw new InvalidOperationException("Invalid unit radius in combat body blocking.");
            var hostile = actor.IsEnemy(other);
            if (hostile && flying && ArdubsEnemyIntelligenceMain.Settings.BlockHostilePassage &&
                ArdubsEnemyIntelligenceMain.Settings.AllowFlyingEnemyPassage)
                flyoverBodies.Add(new Body(ToPoint(other.Position), radius, false, false));
            var blocks = !BodyBlockingPolicy.IgnoreFriendlyForStep(ally, fiveFootStep,
                ArdubsEnemyIntelligenceMain.Settings.IgnoreFriendlyBlockingOnFiveFootStep) &&
                BodyBlockingPolicy.BlocksPassage(hostile, ally, flying,
                ArdubsEnemyIntelligenceMain.Settings.BlockHostilePassage, ArdubsEnemyIntelligenceMain.Settings.AllowAlliedPassage && !agent.IsCharging,
                ArdubsEnemyIntelligenceMain.Settings.AllowFlyingEnemyPassage);
            var stops = ArdubsEnemyIntelligenceMain.Settings.PreventStoppingOverlap &&
                BodyBlockingPolicy.BlocksStopping(ally, fiveFootStep,
                    ArdubsEnemyIntelligenceMain.Settings.IgnoreFriendlyBlockingOnFiveFootStep, ArdubsEnemyIntelligenceMain.Settings.AllowFriendlyStoppingOverlap);
            if (blocks || stops)
                bodies.Add(new Body(ToPoint(other.Position), radius, blocks, stops));
        }
        var snapshot = new Snapshot(ToPoint(actor.Position), bodies.ToArray(), flyoverBodies.ToArray(),
            ArdubsEnemyIntelligenceMain.Settings.PreventStoppingOverlap);
        snapshot.CaptureDiagnostics = ArdubsEnemyIntelligenceMain.Settings.ShowBodyBlockingInLog && IsSelectedActor(actor);
        return snapshot;
    }

    // Visual consumers need the same current occupant filtering as the live
    // movement guard, even when that guard itself is disabled.
    internal static Snapshot? CaptureVisual(UnitMovementAgent? agent) => Capture(agent);

    private static bool IsHostilePreview(UnitEntityData actor, ABPathWithClearance approach,
        float approachRadius)
    {
        foreach (var other in Kingmaker.Game.Instance.State.Units)
            if (other.View != null && CompleteAttackRoutes.MatchesHostileTarget(
                    approach.originalEndPoint, other.Position, approachRadius, actor.IsEnemy(other)))
                return true;
        return false;
    }

    internal static Path Attach(Path path, SeekerWithClearance seeker)
    {
        // Native unit graph markings can become stale after Sleep/flight changes
        // in the middle of a turn. Refresh changed markings before this search.
        NavmeshBodyRules.RefreshBeforePath();
        // Seeker lives on a child GameObject, not on the movement agent itself.
        var agent = seeker.GetComponentInParent<UnitEntityView>()?.AgentASP;
        var actor = agent?.Unit?.EntityData;
        var target = agent?.m_DestinationAgent?.Unit?.EntityData;
        var approach = path as ABPathWithClearance;
        var hostileApproach = ArdubsEnemyIntelligenceMain.Settings.BodyBlockingEnabled && actor?.IsInCombat == true &&
            approach?.endingCondition is ABPathWithClearance.LosPathConditionForGrid condition &&
            condition.m_ApproachRadius > 0f &&
            (target != null && actor.IsEnemy(target) && agent!.m_Destination.HasValue &&
                (agent.m_Destination.Value - approach.originalEndPoint).sqrMagnitude < 0.000001f ||
             // An attack command can follow this preview path directly as a
             // ForcedPath; it does not necessarily call PathTo afterward.
             IsHostilePreview(actor, approach, condition.m_ApproachRadius));
        CompleteAttackRoutes.Configure(path,
            actor != null && ReferenceEquals(actor, CompleteAttackRoutes.Current?.Unit), hostileApproach);
        var snapshot = Capture(agent);
        Remember(path, snapshot);
        return path;
    }

    internal static Snapshot? For(Path path) => ArdubsEnemyIntelligenceMain.Settings.BodyBlockingEnabled &&
        Paths.TryGetValue(path, out var snapshot) ? snapshot : null;
    internal static void Remember(Path path, Snapshot? snapshot)
    {
        Paths.Remove(path); // Paths are pooled; never retain an earlier owner's rules.
        if (snapshot != null)
        {
            snapshot.TraceId = Interlocked.Increment(ref nextTraceId);
            Paths.Add(path, snapshot);
        }
    }

    // Native commands copy a scored/previewed route into a new ForcedPath.
    // That copy has no path-search snapshot, so attach the current actor's rules
    // before OnPathComplete or movement simulation can consume it. The source
    // route was already postprocessed; do not silently shorten only the copy,
    // which would leave the owning command expecting a different endpoint.
    internal static void PreparePrecomputedPath(UnitMovementAgent agent, Path path)
    {
        Remember(path, Capture(agent));
    }

    internal static RouteStop? AssessStop(UnitEntityData actor, Path path, double budget)
    {
        if (path.vectorPath == null || path.vectorPath.Count == 0) return null;
        var snapshot = For(path) ?? Capture(actor.View?.AgentASP);
        if (snapshot == null) return null;
        var points = path.vectorPath.Select(ToPoint).ToArray();
        var length = 0.0;
        var previous = snapshot.Origin;
        foreach (var point in points)
        {
            length += BodyBlockingPolicy.Length(previous, point);
            previous = point;
        }
        var planned = Math.Min(length, budget);
        var allowed = BodyBlockingPolicy.RouteLimit(snapshot.Origin, points, planned,
            snapshot.Bodies, snapshot.PreventOverlap);
        var stop = BodyBlockingPolicy.PositionAlong(snapshot.Origin, points, allowed);
        return new RouteStop(planned, allowed,
            new Vector3((float)stop.X, (float)stop.Y, (float)stop.Z));
    }

    private static bool InActionRange(DecisionContext context, Vector3 position) =>
        UnitCommand.IsUnitCloseEnough(context.DestinationPoint, position,
            position + LineOfSightGeometry.EyeShift, context.ActionRangeWithCorpulence, true, 0f);

    // Apply the same passage/stopping geometry to ability selection that the
    // preview and live movement guard use. A flying unit may cross a hostile
    // footprint, but its movement-action boundary must still be a legal stop.
    internal static string? AbilityRouteIssue(DecisionContext context)
    {
        if (context.Ability == null || InActionRange(context, context.Unit.Position)) return null;
        var path = context.CurrentPath;
        if (path == null || path.error || path.CompleteState != PathCompleteState.Complete ||
            path.vectorPath == null || path.vectorPath.Count == 0)
            return "no complete native ability approach path";
        if (!InActionRange(context, path.vectorPath[path.vectorPath.Count - 1]))
            return "native ability approach ends outside range/LOS";
        var actor = context.Unit;
        var fullStop = AssessStop(actor, path, double.MaxValue);
        if (fullStop?.Limited == true)
            return "ability route crosses blocked space or ends occupied";
        var turn = Kingmaker.Game.Instance.TurnBasedCombatController.CurrentTurn;
        if (turn == null) return null;
        var singleAction = turn.GetEnabledSingleActionMove(actor) ||
            context.Ability.RuntimeActionType == UnitCommand.CommandType.Standard;
        var useTotal = BodyBlockingPolicy.MayUseTotalMovement(turn.GetEnabledFiveFootStep(actor),
            singleAction, SettingsRoot.Game.TurnBased.AutoStopAfterFirstMoveAction);
        var budget = Math.Max(0, turn.GetRemainingMovementRange(actor, useTotal, singleAction));
        if (turn.GetEnabledFiveFootStep(actor))
            budget = Math.Max(budget, turn.GetRemainingFiveFootStepRange(actor));
        if (budget <= 0) return null; // Vanilla action restrictions decide this case.
        var budgetStop = AssessStop(actor, path, budget);
        if (budgetStop?.Limited != true) return null;
        return InActionRange(context, budgetStop.Value.Position)
            ? null : "no legal stopping point within this turn's movement budget";
    }

    // StartEndModifier may project the accepted graph node onto a connection.
    // Keep its result unless that projection loses range/LOS or body clearance.
    // Prefer the original node when it can attack/cast; otherwise retract an
    // occupied projection along the native connection without extending the path.
    internal static Vector3 RestoreApproachEndpoint(ABPath path, bool start, Vector3 snapped,
        out bool restored, out bool snappedToNode)
    {
        restored = false;
        snappedToNode = false;
        var snapshot = For(path);
        if (start || snapshot == null || path.error || path.path == null || path.path.Count == 0 ||
            !(path is ABPathWithClearance approach) ||
            !(approach.endingCondition is ABPathWithClearance.LosPathConditionForGrid condition)) return snapped;
        bool Valid(Vector3 point) =>
            GeometryUtils.SqrMechanicsDistance(point, path.originalEndPoint) <= condition.m_ApproachRadius * condition.m_ApproachRadius &&
            (!snapshot.PreventOverlap || BodyBlockingPolicy.CanStopAction(snapshot.Origin, ToPoint(point), snapshot.Bodies)) &&
            (!condition.m_CheckLos || !LineOfSightGeometry.Instance.HasObstacle(point, path.originalEndPoint, condition.m_IgnoredBlockerRadius));
        if (Valid(snapped)) return snapped;
        var node = path.path[path.path.Count - 1];
        var accepted = node == path.startNode ? path.originalStartPoint : (Vector3)node.position;
        if (Valid(accepted))
        {
            restored = true;
            snappedToNode = true;
            return accepted;
        }
        if (!snapshot.PreventOverlap || BodyBlockingPolicy.CanStop(ToPoint(snapped), snapshot.Bodies) ||
            !BodyBlockingPolicy.CanStop(ToPoint(accepted), snapshot.Bodies)) return snapped;
        var length = Vector3.Distance(accepted, snapped);
        if (length <= 0.000001f) return snapped;
        var safe = BodyBlockingPolicy.RouteLimit(ToPoint(accepted), new[] { ToPoint(snapped) },
            length, snapshot.Bodies, true);
        if (safe >= length - 0.0001) return snapped;
        var candidate = Vector3.Lerp(accepted, snapped, (float)(safe / length));
        if (!BodyBlockingPolicy.CanStop(ToPoint(candidate), snapshot.Bodies)) return snapped;
        restored = true;
        return candidate;
    }
    internal static void FinishRoute(Path path)
    {
        var snapshot = For(path);
        if (snapshot == null || path.error || path.vectorPath == null || path.vectorPath.Count == 0) return;
        var points = path.vectorPath.Select(ToPoint).ToArray();
        var length = 0.0; var previous = snapshot.Origin;
        foreach (var p in points) { length += BodyBlockingPolicy.Length(previous, p); previous = p; }
        var allowed = BodyBlockingPolicy.RouteLimit(snapshot.Origin, points, length, snapshot.Bodies, snapshot.PreventOverlap);
        // Other modifiers can make the route unsafe before RaycastModifier runs.
        // The graph-search route is still available here and already passed the
        // same body rules; keep it instead of truncating a straight shortcut.
        if (allowed < length - 0.0001 && snapshot.UnmodifiedRoute is { Length: > 0 } original)
        {
            var originalPoints = original.Select(ToPoint).ToArray();
            snapshot.UnmodifiedRouteClear = BodyBlockingPolicy.RouteIsClear(snapshot.Origin,
                originalPoints, snapshot.Bodies, snapshot.PreventOverlap);
            if (snapshot.UnmodifiedRouteClear == true)
            {
                path.vectorPath.Clear();
                path.vectorPath.AddRange(original);
                points = originalPoints;
                length = 0.0;
                previous = snapshot.Origin;
                foreach (var p in points) { length += BodyBlockingPolicy.Length(previous, p); previous = p; }
                allowed = length;
                snapshot.RestoredUnmodifiedRoute = true;
            }
        }
        snapshot.RouteBeforeTrimEnd = path.vectorPath[path.vectorPath.Count - 1];
        snapshot.RouteBeforeTrimLength = length;
        snapshot.RouteAllowedLength = allowed;
        if (allowed >= length - 0.0001) return;
        snapshot.ClearanceLimited = true;
        var origin = new Vector3((float)snapshot.Origin.X, (float)snapshot.Origin.Y, (float)snapshot.Origin.Z);
        Trim(path.vectorPath, origin, allowed);
    }

    private static string RouteStage(Path path, Snapshot snapshot)
    {
        if (path.vectorPath == null || path.vectorPath.Count == 0) return "no vector route";
        var points = path.vectorPath.Select(ToPoint).ToArray();
        var length = 0.0;
        var previous = snapshot.Origin;
        foreach (var point in points)
        {
            length += BodyBlockingPolicy.Length(previous, point);
            previous = point;
        }
        var allowed = BodyBlockingPolicy.RouteLimit(snapshot.Origin, points, length, snapshot.Bodies, false);
        var end = BodyBlockingPolicy.CanStop(points[points.Length - 1], snapshot.Bodies) ? "clear" : "occupied";
        return $"{points.Length} points; passage {allowed:0.00}/{length:0.00}m; end {end}";
    }

    internal static void RecordBeforeModifiers(Path path)
    {
        var snapshot = For(path);
        if (snapshot == null) return;
        snapshot.UnmodifiedRoute = path.vectorPath?.ToArray();
        if (!snapshot.CaptureDiagnostics) return;
        snapshot.SearchGraph = $"{path.path?.Count ?? 0} nodes; " +
            (path.path?.FirstOrDefault()?.GetType().Name ?? "unknown graph");
        snapshot.BeforeModifiers = RouteStage(path, snapshot);
    }

    internal static bool RestoreUnsafeRaycast(Path path, Vector3[]? before)
    {
        var snapshot = For(path);
        if (snapshot == null || before == null || before.Length == 0 ||
            path.error || path.vectorPath == null || path.vectorPath.Count == 0) return false;

        bool ClearRoute(IEnumerable<Vector3> route)
        {
            var points = route.Select(ToPoint).ToArray();
            return BodyBlockingPolicy.RouteIsClear(snapshot.Origin, points,
                snapshot.Bodies, snapshot.PreventOverlap);
        }

        if (ClearRoute(path.vectorPath) || !ClearRoute(before)) return false;
        path.vectorPath.Clear();
        path.vectorPath.AddRange(before);
        return true;
    }

    internal static void RecordAfterRaycast(Path path, bool restored)
    {
        var snapshot = For(path);
        if (snapshot?.CaptureDiagnostics == true)
            snapshot.AfterRaycast = (restored ? "unsafe smoothing restored; " : "") + RouteStage(path, snapshot);
    }

    private static void Trim(List<Vector3> points, Vector3 start, double allowed)
    {
        var result = new List<Vector3> { start };
        var previous = start;
        foreach (var p in points)
        {
            var length = Vector3.Distance(previous, p);
            if (length < 0.000001) continue;
            if (length > allowed) { result.Add(Vector3.Lerp(previous, p, (float)(allowed / length))); break; }
            result.Add(p); allowed -= length; previous = p;
            if (allowed <= 0.000001) break;
        }
        points.Clear(); points.AddRange(result);
    }

    private static IEnumerable<Point> Continuation(Vector3 next, Path? path, int waypoint)
    {
        yield return ToPoint(next);
        if (path?.vectorPath == null) yield break;
        for (var index = waypoint; index < path.vectorPath.Count; index++)
            if (index >= 0) yield return ToPoint(path.vectorPath[index]);
    }

    // The native path/budget, not the destination's approach radius, determines
    // where movement ends. Kept free of scene reads so actual tick integration
    // can be tested with native path objects outside Unity.
    internal static double LimitStep(Snapshot snapshot, Vector3 previous, Vector3 next,
        Path? path, int waypoint, double budget, bool stopsHere)
    {
        var available = stopsHere ? Vector3.Distance(previous, next) : budget;
        return BodyBlockingPolicy.RouteLimit(ToPoint(previous), Continuation(next, path, waypoint),
            available, snapshot.Bodies, snapshot.PreventOverlap);
    }

    internal static double PreviewCrossingBudget(PathVisualizer.VisualPathSettings settings,
        double travelled, double actionBudget, bool stopAfterFirstMove)
    {
        // Match the native preview's permission to advance from move to standard
        // action. A queued attack/ability can reserve that standard action.
        var moveAvailable = settings.MoveActionCooldown < 3 &&
            settings.MovePredictionState != CombatAction.ActionState.Unable &&
            settings.MovePredictionState != CombatAction.ActionState.WillBeUsed &&
            settings.MovePredictionState != CombatAction.ActionState.WillBeLost;
        var useTotal = moveAvailable && settings.StandardPredictionState == CombatAction.ActionState.Available &&
            BodyBlockingPolicy.MayUseTotalMovement(settings.EnabledFiveFeetStep,
                settings.EnabledSingleActionMove, stopAfterFirstMove);
        return BodyBlockingPolicy.CrossingBudget(actionBudget, settings.RemainingMovementRangeTotal, travelled, useTotal);
    }

    private static double SimulationCrossingBudget(UnitMovementAgent agent, Path path, double actionBudget)
    {
        var preview = PathVisualizer.Instance;
        // Other callers (including AI attack simulations) own their own budget.
        // Never extend them using the selected player's movement preview.
        if (preview == null || !ReferenceEquals(preview.m_CurrentPath, path) ||
            preview.m_LastUpdateData == null ||
            !ReferenceEquals(preview.m_LastUpdateData.Settings.Unit, agent.Unit.EntityData)) return actionBudget;
        return PreviewCrossingBudget(preview.m_LastUpdateData.Settings, preview.m_TotalPathLength,
            actionBudget, SettingsRoot.Game.TurnBased.AutoStopAfterFirstMoveAction);
    }

    internal static bool CanStopSimulationApproach(Path path, Vector3 point)
    {
        var snapshot = For(path);
        if (snapshot == null || !snapshot.PreventOverlap ||
            BodyBlockingPolicy.CanStopAction(snapshot.Origin, ToPoint(point), snapshot.Bodies)) return true;
        // Range/LOS alone does not make an intermediate occupied point a legal
        // endpoint. Continue the native tick along its searched route instead
        // of stopping here and forcing GuardSimulation to retract the preview.
        snapshot.OccupiedApproachStopsRejected++;
        return false;
    }

    internal static void GuardSimulation(UnitMovementAgent agent, ref MovementSimulator simulator,
        Vector3 previous, float budget, ref bool continues)
    {
        if (!Applies(agent)) return;
        var path = simulator.Path;
        if (path == null) return;
        var snapshot = For(path);
        if (snapshot == null)
        {
            snapshot = Capture(agent);
            if (snapshot != null) Remember(path, snapshot);
        }
        if (snapshot == null) return;
        var length = Vector3.Distance(previous, simulator.Position);
        if (length <= 0.000001) return;
        var crossingBudget = SimulationCrossingBudget(agent, path, budget);
        // A real native endpoint/range/LOS stop must be unoccupied. Reaching a
        // move-action boundary is not an endpoint if this command can continue
        // into its second action. Leave native action accounting unchanged.
        var allowed = LimitStep(snapshot, previous, simulator.Position, path,
            simulator.WaypointIndex, crossingBudget, BodyBlockingPolicy.SimulationStopsHere(
                continues, simulator.ReachedMaxDistance, budget, crossingBudget, length));
        if (allowed >= length - 0.0001) return;
        snapshot.ClearanceLimited = true;
        simulator.Position = Vector3.Lerp(previous, simulator.Position, (float)(allowed / length));
        simulator.LastMoveLength = (float)allowed;
        simulator.ReachedMaxDistance = true;
        continues = false;
    }

    private static DateTime LastPreviewLogUtc;
    private static string? LastPreviewLogKey;

    private static string At(Vector3 value) => $"({value.x:0.0}, {value.z:0.0})";
    private static string At(Point value) => $"({value.X:0.0}, {value.Z:0.0})";
    private static string At(Vector3? value) => value.HasValue ? At(value.Value) : "none";
    private static string Distance(double? value) => value.HasValue ? $"{value.Value:0.00}m" : "unknown";
    private static Vector3? PathEnd(Path? path) => path?.vectorPath != null && path.vectorPath.Count > 0
        ? path.vectorPath[path.vectorPath.Count - 1] : (Vector3?)null;
    private static string Trace(Path? path) => path == null ? "none" :
        For(path)?.TraceId.ToString() ?? $"untracked@{RuntimeHelpers.GetHashCode(path)}";
    private static string StopStatus(Snapshot? snapshot, Vector3? point) =>
        snapshot == null || !point.HasValue ? "unknown" :
        BodyBlockingPolicy.CanStop(ToPoint(point.Value), snapshot.Bodies) ? "clear" : "occupied";
    private static bool IsSelectedActor(UnitEntityData actor) =>
        ReferenceEquals(actor, CombatController.SelectedUnit) || ReferenceEquals(actor, CombatController.Mount);

    private static string CommandFor(UnitEntityData actor)
    {
        var rider = actor.SaddledPart?.Rider;
        UnitCommand? command = (UnitCommand?)rider?.Commands.UnitUseAbility ??
            (UnitCommand?)rider?.Commands.Attack ??
            (UnitCommand?)actor.Commands.UnitUseAbility ?? actor.Commands.Attack;
        if (command == null) return rider == null ? "none" : $"none (rider {rider.CharacterName})";
        return $"{command.Executor.CharacterName}:{command.GetType().Name} -> " +
            (command.TargetUnit?.CharacterName ?? "point");
    }

    private static string NearBodies(Snapshot? snapshot, Vector3 point, UnitEntityData actor)
    {
        if (snapshot == null) return "snapshot unavailable";
        if (snapshot.Bodies.Length == 0) return "none";
        var units = Kingmaker.Game.Instance.State.Units;
        return string.Join(", ", snapshot.Bodies.Select(body =>
            {
                var dx = point.x - body.Center.X;
                var dz = point.z - body.Center.Z;
                return new { Body = body, Gap = Math.Sqrt(dx * dx + dz * dz) - body.Radius };
            }).OrderBy(item => item.Gap).Take(6).Select(item =>
            {
                var body = item.Body;
                var owner = units.FirstOrDefault(unit => !ReferenceEquals(unit, actor) &&
                    Math.Abs(unit.Position.x - body.Center.X) < 0.05 &&
                    Math.Abs(unit.Position.z - body.Center.Z) < 0.05);
                var rule = body.BlocksPassage && body.BlocksStopping ? "passage+stop" :
                    body.BlocksPassage ? "passage" : body.BlocksStopping ? "stop" : "none";
                return $"{owner?.CharacterName ?? "unknown"} {rule} gap {item.Gap:0.000}m " +
                    $"(combined radius {body.Radius:0.000}m)";
            }));
    }

    internal static void LogPreviewPath(PathVisualizer preview, Path path, Vector3 requestedTarget)
    {
        if (!ArdubsEnemyIntelligenceMain.Settings.ShowBodyBlockingInLog || !ArdubsEnemyIntelligenceMain.Settings.BodyBlockingEnabled ||
            !CombatController.IsInTurnBasedCombat()) return;
        var actor = CombatController.Mount ?? CombatController.SelectedUnit;
        if (actor?.View == null) return;
        var visualEnd = preview.m_VisualPath.Count > 0 ? preview.GetLastPathElement().Position : (Vector3?)null;
        var status = path.CompleteState;
        var pathSnapshot = For(path);
        var snapshot = status == PathCompleteState.Complete ? pathSnapshot ?? Capture(actor.View.AgentASP) : null;
        var goal = (path as ABPath)?.originalEndPoint ?? requestedTarget;
        var routeEnd = PathEnd(path);
        var blocked = status == PathCompleteState.Complete && visualEnd.HasValue &&
            snapshot != null && !BodyBlockingPolicy.CanStop(ToPoint(visualEnd.Value), snapshot.Bodies);
        var missingSnapshot = status == PathCompleteState.Complete && snapshot != null && pathSnapshot == null;
        var rejectedApproach = pathSnapshot?.OccupiedApproachStopsRejected > 0;
        if (status == PathCompleteState.Complete && !blocked && !missingSnapshot && !rejectedApproach) return;
        var key = $"{actor.CharacterName}:{status}:{Math.Round(requestedTarget.x)}:" +
            $"{Math.Round(requestedTarget.z)}:{blocked}:{missingSnapshot}:{rejectedApproach}";
        var now = DateTime.UtcNow;
        if (key == LastPreviewLogKey && now - LastPreviewLogUtc < TimeSpan.FromSeconds(5)) return;
        LastPreviewLogKey = key;
        LastPreviewLogUtc = now;
        if (snapshot == null) snapshot = Capture(actor.View.AgentASP);
        TargetingLog.Write($"Pathfinding preview #{Trace(path)}: {actor.CharacterName}; command {CommandFor(actor)}; " +
            $"request {status}; actor now {At(actor.Position)}; search start " +
            $"{(snapshot == null ? "unknown" : At(snapshot.Origin))} " +
            $"({(snapshot == null ? "unknown" : BodyBlockingPolicy.CanStop(snapshot.Origin, snapshot.Bodies) ? "clear" : "occupied")}); " +
            $"goal {At(goal)} ({StopStatus(snapshot, goal)}); " +
            $"displayed {At(visualEnd)} ({StopStatus(snapshot, visualEnd)}); " +
            $"route end {At(routeEnd)} ({StopStatus(snapshot, routeEnd)}); " +
            $"displayed path end {At(PathEnd(preview.m_CurrentPath))}; " +
            $"displayed path is request {ReferenceEquals(preview.m_CurrentPath, path)}; " +
            $"occupied displayed stop {blocked}; requested path snapshot {(pathSnapshot == null ? "missing" : "present")}; " +
            $"clearance trimmed {pathSnapshot?.ClearanceLimited == true}; " +
            $"occupied in-range stops rejected {pathSnapshot?.OccupiedApproachStopsRejected ?? 0}; " +
            $"before trim {At(pathSnapshot?.RouteBeforeTrimEnd)} " +
            $"({StopStatus(snapshot, pathSnapshot?.RouteBeforeTrimEnd)}); route length " +
            $"{(pathSnapshot == null ? "unknown" : Distance(pathSnapshot.RouteBeforeTrimLength))}; " +
            $"allowed {(pathSnapshot == null ? "unknown" : Distance(pathSnapshot.RouteAllowedLength))}; " +
            $"search [{pathSnapshot?.SearchGraph ?? "unknown"}]; " +
            $"before modifiers [{pathSnapshot?.BeforeModifiers ?? "not captured"}]; " +
            $"after raycast [{pathSnapshot?.AfterRaycast ?? "not run"}]; " +
            $"unmodified fallback clear {pathSnapshot?.UnmodifiedRouteClear?.ToString() ?? "not checked"}; " +
            $"restored unmodified route {pathSnapshot?.RestoredUnmodifiedRoute == true}; " +
            $"nearby [{NearBodies(snapshot, visualEnd ?? goal, actor)}]" +
            (path.error ? $"; error {path.errorLog?.Replace('\r', ' ').Replace('\n', ' ').Trim()}" : ""));
    }

    internal static bool CommitPreviewReplacement(PathVisualizer preview)
    {
        if (!ArdubsEnemyIntelligenceMain.Settings.BodyBlockingEnabled ||
            !CombatController.IsInTurnBasedCombat() ||
            preview.m_LastUpdateData == null || preview.m_LastUpdateData.ApproachRadius <= 0 ||
            preview.m_CurrentPath?.vectorPath == null ||
            preview.m_CurrentPath.vectorPath.Count == 0 ||
            preview.m_ReplacementVectorPath.Count == 0) return true;
        var path = preview.m_CurrentPath;
        var snapshot = For(path);
        if (snapshot == null) return true;
        var searched = path.vectorPath.Select(ToPoint).ToArray();
        var replacement = preview.m_ReplacementVectorPath.Select(ToPoint).ToArray();
        if (BodyBlockingPolicy.CommitReplacementRoute(snapshot.Origin, searched,
            replacement, snapshot.Bodies, snapshot.PreventOverlap)) return true;
        if (ArdubsEnemyIntelligenceMain.Settings.ShowBodyBlockingInLog)
            TargetingLog.Write($"Pathfinding preview #{Trace(path)}: retaining clear searched approach route; " +
                $"visual replacement ends at {At(preview.m_ReplacementVectorPath.Last())} " +
                $"({StopStatus(snapshot, preview.m_ReplacementVectorPath.Last())})");
        return false;
    }

    internal static void LogCommittedPath(UnitMovementAgent agent, Path path)
    {
        if (!ArdubsEnemyIntelligenceMain.Settings.ShowBodyBlockingInLog || !ArdubsEnemyIntelligenceMain.Settings.BodyBlockingEnabled ||
            !CombatController.IsInTurnBasedCombat() || path.CompleteState != PathCompleteState.Complete) return;
        var actor = agent.Unit?.EntityData;
        if (actor == null || !IsSelectedActor(actor)) return;
        var snapshot = For(path) ?? Capture(agent);
        var preview = PathVisualizer.Instance;
        var previewActor = preview?.m_LastUpdateData?.Settings.Unit;
        var end = PathEnd(path);
        TargetingLog.Write($"Pathfinding committed #{Trace(path)}: {actor.CharacterName}; " +
            $"command {CommandFor(actor)}; from {At(actor.Position)}; destination {At(agent.m_Destination)}; " +
            $"route end {At(end)} ({StopStatus(snapshot, end)}); " +
            $"trimmed {snapshot?.ClearanceLimited == true}; " +
            $"same path as displayed {ReferenceEquals(preview?.m_CurrentPath, path)} " +
            $"(preview #{Trace(preview?.m_CurrentPath)}); " +
            $"preview actor {previewActor?.CharacterName ?? "none"}; " +
            $"nearby [{NearBodies(snapshot, end ?? actor.Position, actor)}]");
    }

    private static void LogMovementStart(UnitMovementAgent agent, Snapshot snapshot, Vector3 from)
    {
        if (!ArdubsEnemyIntelligenceMain.Settings.ShowBodyBlockingInLog || !IsSelectedActor(agent.Unit.EntityData)) return;
        var path = agent.Path;
        if (path == null) return;
        var pathSnapshot = For(path);
        var trace = MovementTraces.GetValue(agent, _ => new MovementTrace());
        var traceId = pathSnapshot?.TraceId ?? 0;
        if (ReferenceEquals(trace.Path, path) && trace.TraceId == traceId) return;
        trace.Path = path;
        trace.TraceId = traceId;
        var preview = PathVisualizer.Instance;
        var end = PathEnd(path);
        var grid = AstarPath.active?.data?.gridGraph;
        TargetingLog.Write($"Pathfinding movement start #{Trace(path)}: {agent.Unit.EntityData.CharacterName}; " +
            $"command {CommandFor(agent.Unit.EntityData)}; from {At(from)} ({StopStatus(snapshot, from)}); " +
            $"grid cell {(grid == null ? "unknown" : $"{grid.nodeSize:0.00}m")}; " +
            $"destination {At(agent.m_Destination)}; route end {At(end)} ({StopStatus(snapshot, end)}); " +
            $"trimmed {pathSnapshot?.ClearanceLimited == true}; " +
            $"same path as displayed {ReferenceEquals(preview?.m_CurrentPath, path)} " +
            $"(preview #{Trace(preview?.m_CurrentPath)}); " +
            $"displayed {At(preview?.m_VisualPath.Count > 0 ? preview.GetLastPathElement().Position : (Vector3?)null)}; " +
            $"nearby [{NearBodies(snapshot, end ?? from, agent.Unit.EntityData)}]" +
            FlyingPassageVisual.DescribeMovement(agent, from, snapshot));
    }

    internal static void LogLivePathFailure(UnitMovementAgent agent, Path path)
    {
        if (!ArdubsEnemyIntelligenceMain.Settings.ShowBodyBlockingInLog || !ArdubsEnemyIntelligenceMain.Settings.BodyBlockingEnabled ||
            !CombatController.IsInTurnBasedCombat() || !agent.m_NoPathDestination.HasValue) return;
        var actor = agent.Unit?.EntityData;
        if (actor == null) return;
        var goal = agent.m_NoPathDestination.Value;
        var snapshot = For(path) ?? Capture(agent);
        var preview = PathVisualizer.Instance;
        var previewUnit = preview?.m_LastUpdateData?.Settings.Unit;
        var visualEnd = preview?.m_VisualPath.Count > 0 ? preview.GetLastPathElement().Position : (Vector3?)null;
        TargetingLog.Write($"Pathfinding committed route failed #{Trace(path)}: {actor.CharacterName}; command {CommandFor(actor)}; " +
            $"from {At(actor.Position)}; goal {At(goal)}; path {path.CompleteState}; " +
            $"path end {At(PathEnd(path))}; preview {previewUnit?.CharacterName ?? "none"} at {At(visualEnd)}; " +
            $"nearby [{NearBodies(snapshot, goal, actor)}]" +
            (path.error ? $"; error {path.errorLog?.Replace('\r', ' ').Replace('\n', ' ').Trim()}" : ""));
    }

    internal static Vector3 GuardMove(UnitMovementAgent agent, Vector3 previous, Vector3 next)
    {
        if (!ArdubsEnemyIntelligenceMain.Settings.BodyBlockingMovementGuard) return next;
        var snapshot = Capture(agent);
        if (snapshot == null) return next;
        LogMovementStart(agent, snapshot, previous);
        var length = Vector3.Distance(previous, next);
        if (length <= 0.000001) return next;
        var turn = Kingmaker.Game.Instance.TurnBasedCombatController.CurrentTurn;
        if (turn == null) return next;
        // TickMovement already consumed this tick's movement time; add its actual
        // displacement back when checking whether an allied crossing can finish.
        var actor = agent.Unit.EntityData;
        var singleAction = BodyBlockingPolicy.ReservesSingleMove(turn.GetEnabledSingleActionMove(actor),
            SpellApproach.ReservesStandard(actor.Commands.UnitUseAbility));
        var useTotal = BodyBlockingPolicy.MayUseTotalMovement(turn.GetEnabledFiveFootStep(actor),
            singleAction, SettingsRoot.Game.TurnBased.AutoStopAfterFirstMoveAction);
        // TickMovement accounts one action segment at a time, but a continuous
        // ordinary move may spend both actions. Check its legal crossing budget,
        // not only the distance left before the next action boundary.
        var remaining = turn.GetRemainingMovementRange(actor, useTotal, singleAction);
        // Native movement can borrow a final tick to reach its endpoint. A
        // negative remaining range means no further movement, not a negative budget.
        var available = remaining > 0 ? remaining + length : length;
        var allowed = LimitStep(snapshot, previous, next, agent.Path, agent.m_NextPointIndex, available, false);
        if (allowed >= length - 0.0001) return next;
        Stopped.Remove(agent);
        var cast = actor.Commands.UnitUseAbility;
        Stopped.Add(agent, new BlockedStop(agent.Path, cast?.IsMoveUnit == true ? cast : null,
            snapshot, previous, next, allowed, available));
        return Vector3.Lerp(previous, next, (float)(allowed / length));
    }

    internal static void FinishMovementTick(UnitMovementAgent agent)
    {
        if (!Stopped.TryGetValue(agent, out var stop)) return;
        Stopped.Remove(agent);
        if (!ArdubsEnemyIntelligenceMain.Settings.BodyBlockingEnabled || !ArdubsEnemyIntelligenceMain.Settings.BodyBlockingMovementGuard ||
            agent.Path != null && !ReferenceEquals(agent.Path, stop.Path)) return;
        // Stop before the next tick, never inside MoveUnit: the current tick
        // still reads its waypoints. A replacement command must not inherit this stop.
        if (agent.Path != null) agent.CompleteMovement();
        // Native movement may have completed and cleared Path on the same tick.
        // Only the cast that owned the blocked movement can be cancelled.
        var cast = agent.Unit.EntityData.Commands.UnitUseAbility;
        if (ReferenceEquals(cast, stop.Cast)) SpellApproach.CancelAfterBodyStop(cast);
        if (ArdubsEnemyIntelligenceMain.Settings.ShowBodyBlockingInLog)
            TargetingLog.Write($"Pathfinding #{Trace(stop.Path)}: {agent.Unit.EntityData.CharacterName} stopped at body clearance; " +
                $"from {At(stop.From)} toward {At(stop.Attempted)}; allowed {stop.Allowed:0.00}m of " +
                $"{Vector3.Distance(stop.From, stop.Attempted):0.00}m tick; budget {stop.Budget:0.00}m; " +
                $"route end {At(PathEnd(stop.Path))}; destination {At(agent.m_Destination)}; " +
                $"actual stop {At(agent.Unit.EntityData.Position)} ({StopStatus(stop.Snapshot, agent.Unit.EntityData.Position)}); " +
                $"command {CommandFor(agent.Unit.EntityData)}; " +
                $"nearby [{NearBodies(stop.Snapshot, stop.Attempted, agent.Unit.EntityData)}]; no target change");
    }
}
