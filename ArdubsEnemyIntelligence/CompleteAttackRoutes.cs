using System;
using Kingmaker.AI;
using Kingmaker.TurnBasedMode.Pathfinding;
using Pathfinding;
using UnityEngine;

namespace ArdubsEnemyIntelligence;

// Let the native search for scored attacks and abilities finish at a usable
// approach position rather than an arbitrary one-turn movement-horizon node.
// No second search, custom ending-condition object or persistent route cache.
internal static class CompleteAttackRoutes
{
    [ThreadStatic] internal static DecisionContext? Current;

    // PathVisualizer.FindPath has no destination agent. Its target point is
    // UnitCommand.GetTargetPoint(), which is the target unit's current position.
    internal static bool MatchesHostileTarget(Vector3 endpoint, Vector3 targetPosition,
        float approachRadius, bool hostile) =>
        hostile && approachRadius > 0f &&
        (endpoint - targetPosition).sqrMagnitude < 0.000001f;

    internal static void Configure(Path path, bool matchingActor, bool hostileApproach)
    {
        var context = Current;
        if (!ArdubsEnemyIntelligenceMain.Settings.ModEnabled || !(path is ABPathWithClearance clearance) ||
            !(clearance.endingCondition is ABPathWithClearance.LosPathConditionForGrid condition) ||
            !(hostileApproach || matchingActor && context != null &&
                (clearance.originalEndPoint - context.DestinationPoint).sqrMagnitude < 0.000001f)) return;
        // Native FindPath itself uses MaxValue when no movement horizon applies.
        // Its squared comparison cannot stop at a finite distance. Terrain,
        // tags, unit clearance and native approach/LOS tests remain unchanged.
        clearance.endingCondition = new ABPathWithClearance.LosPathConditionForGrid(clearance,
            condition.m_ApproachRadius, condition.m_CheckLos, condition.m_IgnoredBlockerRadius, float.MaxValue);
    }

    internal static bool PreserveSearchResult(DecisionContext context) => ReferenceEquals(Current, context);
}
