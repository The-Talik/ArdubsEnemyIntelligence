using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.AI;
using Kingmaker.Controllers.Combat;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.TurnBasedMode.Pathfinding;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.View;
using Owlcat.Runtime.Visual.RenderPipeline.RendererFeatures.FogOfWar;
using Pathfinding;
using TurnBased.Controllers;
using UnityEngine;

namespace ArdubsEnemyIntelligence;

internal static class DistanceScoring
{
    internal sealed class Route
    {
        internal readonly decimal Meters;
        internal readonly string? Rejection;
        internal Route(decimal meters, string? rejection)
        {
            Meters = meters;
            Rejection = rejection;
        }
    }

    internal readonly struct Result
    {
        internal readonly decimal Factor;
        internal readonly string Description;
        internal readonly bool FiveFootStep;
        internal readonly int FirstAttacks;

        internal Result(decimal factor, string description, bool fiveFootStep = false, int firstAttacks = 0)
        {
            Factor = factor;
            Description = description;
            FiveFootStep = fiveFootStep;
            FirstAttacks = firstAttacks;
        }
    }

    // Capture before vanilla can truncate the route to a safe stopping point.
    // The existing native search must now reach attack range/LOS. A completed
    // movement-horizon segment is not evidence of a route to the target.
    internal static Route Capture(DecisionContext context)
    {
        var path = context.CurrentPath;
        if (path == null || path.error || path.CompleteState != PathCompleteState.Complete ||
            path.vectorPath == null || path.vectorPath.Count == 0)
        {
            return new Route(0m, "game path failed or incomplete");
        }
        if (InAttackRange(context, context.Unit.Position))
        {
            return new Route(0m, null);
        }
        var meters = path.GetTotalLength() + Vector3.Distance(context.Unit.Position, path.vectorPath[0]);
        if (float.IsNaN(meters) || float.IsInfinity(meters) || meters < 0f)
        {
            throw new InvalidOperationException("The game returned an invalid attack path length.");
        }
        var endpoint = path.vectorPath[path.vectorPath.Count - 1];
        if (!InAttackRange(context, endpoint))
        {
            return new Route(0m, "complete route to attack range/LOS not found; partial paths are not valid approaches");
        }
        if (BodyBlocking.AssessStop(context.Unit, path, double.MaxValue)?.Limited == true)
        {
            return new Route(0m, "native attack route crosses blocked space or ends occupied");
        }
        return new Route((decimal)meters, null);
    }

    private static bool InAttackRange(DecisionContext context, Vector3 position) =>
        UnitCommand.IsUnitCloseEnough(context.DestinationPoint, position,
            position + LineOfSightGeometry.EyeShift, context.ActionRangeWithCorpulence, true, 0f);

    internal static Result EvaluateAbility(DecisionContext context, decimal gameFactor)
    {
        if (gameFactor <= 0m)
            return new Result(0m, "Unavailable[game ability-path score rejected action => 0]");
        var ability = context.Ability;
        var actor = context.Unit;
        var turn = Kingmaker.Game.Instance.TurnBasedCombatController.CurrentTurn;
        if (ability == null || turn == null)
            throw new InvalidOperationException("Ability distance requires an active ability and turn.");

        decimal meters = 0m;
        if (!InAttackRange(context, actor.Position))
        {
            var path = context.CurrentPath;
            if (path?.vectorPath == null || path.vectorPath.Count == 0)
                return new Result(0m, "Unavailable[no complete ability approach path => 0]");
            var length = path.GetTotalLength() + Vector3.Distance(actor.Position, path.vectorPath[0]);
            if (float.IsNaN(length) || float.IsInfinity(length) || length < 0f)
                throw new InvalidOperationException("The game returned an invalid ability path length.");
            meters = (decimal)length;
        }

        var speed = actor.CurrentSpeedMps;
        if (meters > 0m && speed <= 0f)
            return new Result(0m, "Unavailable[ability requires movement but speed is zero => 0]");
        var fullRound = ability.RequireFullRoundAction;
        var actionType = ability.RuntimeActionType;
        var standard = actionType == UnitCommand.CommandType.Standard;
        var move = actionType == UnitCommand.CommandType.Move;
        var swift = actionType == UnitCommand.CommandType.Swift;
        var actionAvailable = fullRound
            ? actor.HasFullRoundAction() && !actor.IsMoveActionRestricted() &&
              !actor.IsStandardActionRestricted() && !actor.State.HasCondition(UnitCondition.Staggered)
            : standard ? actor.HasStandardAction() && !actor.IsStandardActionRestricted()
            : move ? (actor.HasMoveAction() || actor.HasStandardAction()) &&
                     !actor.IsMoveActionRestricted()
            : !swift || actor.HasSwiftAction();
        var stepRange = turn.HasFiveFootStep(actor)
            ? (decimal)turn.GetRemainingFiveFootStepRange(actor) : 0m;
        var normalMovementRestricted = turn.ShouldRestrictNormalMovement(actor);
        var totalRange = normalMovementRestricted ? 0m :
            (decimal)(turn.GetRemainingTime(actor) * speed);
        var moveRange = normalMovementRestricted ? 0m :
            (decimal)(turn.GetRemainingTimeForAction(actor, UnitCommand.CommandType.Move,
                turn.GetRemainingTime(actor)) * speed);
        var moveAndAct = fullRound ? stepRange : standard || move
            ? Math.Max(stepRange, moveRange) : Math.Max(stepRange, totalRange);
        var fullMovementNextTurn = !actor.IsMoveActionRestricted() &&
            !actor.State.HasCondition(UnitCondition.Staggered);
        var futureTotal = (decimal)speed * (fullMovementNextTurn ? 6m : 3m);
        var futureMoveAndAct = fullRound ? (decimal)TurnController.MetersOfFiveFootStep : standard || move
            ? (fullMovementNextTurn ? (decimal)speed * 3m : 0m) : futureTotal;
        var plan = new AbilityDistancePlan(meters, moveAndAct, totalRange,
            futureMoveAndAct, futureTotal, actionAvailable);
        var factor = gameFactor * plan.Factor;
        var description = $"Ability path[game factor {LogNumber.Format(gameFactor)}] * " +
            $"Timing[{(fullRound ? "full-round" : actionType.ToString().ToLowerInvariant())}; " +
            $"act now {(plan.WaitTurns == 0 ? "yes" : "no")}; wait {plan.WaitTurns} => " +
            $"{LogNumber.Format(plan.TimingFactor)}] * " +
            $"Route[{LogNumber.Format(meters)}m; move + act {LogNumber.Format(moveAndAct)}m; " +
            $"total move {LogNumber.Format(totalRange)}m => {LogNumber.Format(plan.RouteFactor)}]";
        return new Result(factor, description);
    }

    internal static Result Evaluate(DecisionContext context, Route route,
        Func<UnitEntityData, string> label)
    {
        if (route.Rejection != null)
        {
            return new Result(0m, $"Unavailable[{route.Rejection} => 0]");
        }
        var actor = context.Unit;
        var turn = Kingmaker.Game.Instance.TurnBasedCombatController.CurrentTurn;
        var speed = actor.CurrentSpeedMps;
        if (route.Meters > 0m && (speed <= 0f || !turn.HasMovement(actor)))
        {
            return new Result(0m, "Unavailable[movement required but unavailable => 0]");
        }
        var fullAttacks = UnitAttack.EstimateFullAttacks(actor);
        if (fullAttacks < 1)
        {
            return new Result(0m, "Unavailable[no weapon attacks => 0]");
        }
        var canAttack = actor.HasStandardAction() && !actor.IsStandardActionRestricted();
        var fullAllowed = !actor.IsMoveActionRestricted() &&
            !actor.State.HasCondition(UnitCondition.Staggered);
        var canFull = fullAllowed && actor.HasFullRoundAction() &&
            !actor.CombatState.IsFullAttackRestrictedBecauseOfMoveAction;
        var stepAvailable = turn.HasFiveFootStep(actor);
        var stepRange = stepAvailable ? (decimal)turn.GetRemainingFiveFootStepRange(actor) : 0m;
        var fiveFootStep = route.Meters > 0m && route.Meters <= stepRange && stepAvailable;
        var normalMovementRestricted = turn.ShouldRestrictNormalMovement(actor);
        // The selected command sets its own 5ft-step mode. A currently enabled
        // step must not make a different normal-movement candidate look slower.
        var totalRange = fiveFootStep ? stepRange : normalMovementRestricted ? 0m :
            (decimal)(turn.GetRemainingTime(actor) * speed);
        var moveRange = fiveFootStep ? stepRange : normalMovementRestricted ? 0m :
            (decimal)(turn.GetRemainingTimeForAction(actor, UnitCommand.CommandType.Move,
                turn.GetRemainingTime(actor)) * speed);
        if (route.Meters > 0m && totalRange <= 0m)
        {
            return new Result(0m, "Unavailable[no legal movement budget for this route => 0]");
        }
        var path = context.CurrentPath;
        var shortened = route.Meters > 0m &&
            !InAttackRange(context, path.vectorPath[path.vectorPath.Count - 1]);
        if (shortened)
        {
            // Vanilla can shorten a route to avoid ending inside an ally. Do
            // not claim an attack this turn when its actual forced path stops early.
            canAttack = false;
            var stoppingDistance = (decimal)path.GetTotalLength();
            if (stoppingDistance < totalRange)
            {
                totalRange = stoppingDistance;
            }
        }
        var bodyLimited = false;
        if (route.Meters > 0m)
        {
            var attackStop = BodyBlocking.AssessStop(actor, path, (double)moveRange);
            if (attackStop?.Limited == true && !InAttackRange(context, attackStop.Value.Position))
            {
                canAttack = false;
                bodyLimited = true;
            }
            var totalStop = BodyBlocking.AssessStop(actor, path, (double)totalRange);
            if (totalStop?.Limited == true)
            {
                totalRange = Math.Min(totalRange, (decimal)totalStop.Value.Allowed);
                bodyLimited = true;
            }
        }
        // Future turns are estimates using the current speed and persistent action
        // restrictions, not claims about obstacles or buffs on future turns.
        var futureTotal = (decimal)speed * (fullAllowed ? 6m : 3m);
        var futureMoveAttack = fullAllowed ? (decimal)speed * 3m : 0m;
        var intelligence = actor.Stats.Intelligence.ModifiedValue;
        var threats = new List<UnitEntityData>();
        var immuneToDisengagement = actor.State.Features.DisengageWithoutAttackOfOpportunity ||
            turn.ImmuneAttackOfOpportunityOnDisengage;
        if (!fiveFootStep && !immuneToDisengagement && route.Meters > 0m && intelligence >= 10)
        {
            threats = FindOpportunityThreats(context, (float)totalRange, intelligence >= 20);
        }
        var mode = fiveFootStep ? "5ft step; no movement AoOs" : immuneToDisengagement
            ? "actor immune to movement AoOs" : intelligence < 10
            ? $"Int {intelligence}; route threats ignored" : intelligence < 20
                ? "visible threat exits; remaining AoOs unknown"
                : "visible AoOs available now; game disengagement simulation";
        if (threats.Count > 0)
        {
            mode += "; " + string.Join(", ", threats.Select(label));
        }
        var plan = new DistancePlan(route.Meters, moveRange, totalRange,
            futureMoveAttack, futureTotal, fullAttacks, canAttack, canFull,
            fullAllowed && turn.GetEnabledFullAttack(actor),
            fiveFootStep, stepRange, threats.Count);
        var description = plan.Describe(route.Meters, moveRange, totalRange,
            futureTotal, fullAttacks, threats.Count, mode, fiveFootStep);
        description += "\n  Path[complete native route verified to attack range/LOS]";
        if (shortened)
        {
            description += "\n  Path[vanilla shortened route; no attack this turn; future continuation unknown]";
        }
        if (bodyLimited)
            description += "\n  Path[legal stopping position limits movement this turn]";
        return new Result(plan.Factor, description, fiveFootStep, plan.FirstAttacks);
    }

    private static List<UnitEntityData> FindOpportunityThreats(DecisionContext context,
        float movementRange, bool exact)
    {
        var actor = context.Unit;
        var engagement = new EngagementSimulator(actor);
        // Vanilla uses remembered enemies too. Do not reveal unseen threats.
        engagement.m_ValidEnemies.RemoveAll(unit => !OpportunityAllegiance.IsHostile(unit, actor) ||
            !actor.Memory.ContainsVisible(unit));
        engagement.m_EngagedEnemies.RemoveWhere(unit => !OpportunityAllegiance.IsHostile(unit, actor) ||
            !actor.Memory.ContainsVisible(unit));
        var engaged = new HashSet<UnitEntityData>(engagement.m_ValidEnemies.Where(unit =>
            engagement.EngagedBy(unit, actor.Position)));
        var attackers = new HashSet<UnitEntityData>();
        var simulator = new MovementSimulator(actor.Position, context.CurrentPath,
            context.DestinationPoint, context.ActionRangeWithCorpulence, true, 0f);
        var remaining = movementRange;
        for (var tick = 0; remaining > 0.0001f; tick++)
        {
            if (tick == 2048)
            {
                throw new InvalidOperationException("Attack-route simulation exceeded 2048 ticks.");
            }
            var continues = actor.View.AgentASP.SimulateTickMovementForTurnBased(ref simulator, 0.1f, remaining);
            // Include the last step: it can leave a threatened area too.
            if (exact)
            {
                engagement.CheckForAttacksOfOpportunity(simulator.Position);
            }
            else
            {
                foreach (var enemy in engagement.m_ValidEnemies)
                {
                    if (engagement.EngagedBy(enemy, simulator.Position))
                    {
                        engaged.Add(enemy);
                    }
                    else if (engaged.Remove(enemy))
                    {
                        attackers.Add(enemy);
                    }
                }
            }
            if (!continues || simulator.ReachedMaxDistance)
            {
                break;
            }
            if (simulator.LastMoveLength <= 0f)
            {
                throw new InvalidOperationException("Attack-route simulation made no progress.");
            }
            remaining -= simulator.LastMoveLength;
        }
        return exact ? engagement.AttackersByPosition.Keys.ToList() : attackers.ToList();
    }
}
