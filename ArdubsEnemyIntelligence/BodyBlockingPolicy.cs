using System;
using System.Collections.Generic;
using System.Linq;

namespace ArdubsEnemyIntelligence;

// Pure swept-circle geometry. Radii already include the mover's own radius.
// No Unity objects or game state may be read by the pathfinder worker.
internal static class BodyBlockingPolicy
{
    internal readonly struct Point
    {
        internal readonly double X, Z, Y;
        internal Point(double x, double z, double y = 0) { X = x; Z = z; Y = y; }
        internal static Point Lerp(Point a, Point b, double t) =>
            new Point(a.X + (b.X - a.X) * t, a.Z + (b.Z - a.Z) * t, a.Y + (b.Y - a.Y) * t);
    }

    internal readonly struct Body
    {
        internal readonly Point Center;
        internal readonly double Radius;
        internal readonly bool BlocksPassage;
        internal readonly bool BlocksStopping;
        internal Body(Point center, double radius, bool blocksPassage)
            : this(center, radius, blocksPassage, true) { }
        internal Body(Point center, double radius, bool blocksPassage, bool blocksStopping)
        {
            if (radius <= 0 || double.IsNaN(radius) || double.IsInfinity(radius))
                throw new ArgumentOutOfRangeException(nameof(radius));
            Center = center; Radius = radius; BlocksPassage = blocksPassage; BlocksStopping = blocksStopping;
        }
    }

    private const double Epsilon = 0.000001;
    internal static bool IsFriendlyForMovement(bool hostile, bool reverseHostile, bool ally, bool reverseAlly) =>
        !hostile && !reverseHostile && (ally || reverseAlly);

    internal static bool OccupiesSpace(bool conscious, bool sleeping, bool unconsciousCondition, bool ignoreDowned) =>
        conscious && (!ignoreDowned || !sleeping && !unconsciousCondition);

    internal static bool BlocksPassage(bool hostile, bool ally, bool flying,
        bool blockHostile, bool allowAllied, bool allowFlying) =>
        hostile && blockHostile && !(flying && allowFlying) || ally && !allowAllied;

    internal static bool IgnoreFriendlyForStep(bool ally, bool fiveFootStep, bool ignoreFriendlyStep) =>
        ally && fiveFootStep && ignoreFriendlyStep;

    internal static bool BlocksStopping(bool ally, bool fiveFootStep, bool ignoreFriendlyStep, bool allowFriendlyStop) =>
        !(ally && allowFriendlyStop) && !IgnoreFriendlyForStep(ally, fiveFootStep, ignoreFriendlyStep);

    internal static bool MayUseTotalMovement(bool fiveFootStep, bool singleAction, bool stopAfterFirstMove) =>
        !fiveFootStep && !singleAction && !stopAfterFirstMove;

    internal static bool ReservesSingleMove(bool singleAction, bool queuedStandardCast) =>
        singleAction || queuedStandardCast;

    internal static bool CancelBlockedCast(bool clearanceLimited, bool matchingApproach, bool canCastAtEndpoint) =>
        clearanceLimited && matchingApproach && !canCastAtEndpoint;

    // Preview ranges describe the start of the simulation. Subtract distance
    // already simulated along the route, not straight-line distance from origin.
    internal static double CrossingBudget(double actionBudget, double totalBudget, double travelled, bool useTotal) =>
        useTotal && totalBudget - travelled > actionBudget ? totalBudget - travelled : actionBudget;

    // Native simulation can borrow a final waypoint tick or start with no
    // remaining movement. Validate that actual endpoint, not a negative route budget.
    internal static bool SimulationStopsHere(bool continues, bool reachedMaxDistance, double budget, double stepLength) =>
        SimulationStopsHere(continues, reachedMaxDistance, budget, budget, stepLength);

    internal static bool SimulationStopsHere(bool continues, bool reachedMaxDistance,
        double actionBudget, double crossingBudget, double stepLength) =>
        !continues || crossingBudget <= stepLength || reachedMaxDistance && crossingBudget <= actionBudget;

    internal static double Length(Point a, Point b) => Math.Sqrt(Squared(a, b) + (a.Y - b.Y) * (a.Y - b.Y));
    internal static Point PositionAlong(Point start, IEnumerable<Point> waypoints, double distance)
    {
        if (distance < 0 || double.IsNaN(distance) || double.IsInfinity(distance))
            throw new ArgumentOutOfRangeException(nameof(distance));
        var previous = start;
        foreach (var next in waypoints)
        {
            var length = Length(previous, next);
            if (length < Epsilon) continue;
            if (distance <= length) return Point.Lerp(previous, next, distance / length);
            distance -= length;
            previous = next;
        }
        return previous;
    }
    private static double Squared(Point a, Point b) => (a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z);
    internal static bool CanStop(Point p, IReadOnlyList<Body> bodies) =>
        bodies.All(b => !b.BlocksStopping || Squared(p, b.Center) + Epsilon >= b.Radius * b.Radius);

    internal static bool CanTraverse(Point p, Point origin, IReadOnlyList<Body> bodies)
    {
        foreach (var b in bodies)
        {
            if (!b.BlocksPassage) continue;
            var distance = Squared(p, b.Center);
            if (distance + Epsilon >= b.Radius * b.Radius) continue;
            // Let a unit escape its starting occupied footprint, not move deeper.
            var initial = Squared(origin, b.Center);
            if (initial + Epsilon < b.Radius * b.Radius && distance + Epsilon >= initial) continue;
            return false;
        }
        return true;
    }

    // A reachable node can have a legal center while the connection into it
    // clips a body. Check the swept center line, not an inflated node radius.
    internal static bool CanTraverseEdge(Point from, Point to, IReadOnlyList<Body> bodies) =>
        PassageFraction(from, to, bodies) >= 1;

    private static IEnumerable<double> Crossings(Point a, Point z, Body body)
    {
        var dx = z.X - a.X; var dz = z.Z - a.Z;
        var x = a.X - body.Center.X; var y = a.Z - body.Center.Z;
        var aa = dx * dx + dz * dz;
        if (aa < Epsilon * Epsilon) yield break;
        var bb = 2 * (x * dx + y * dz);
        var cc = x * x + y * y - body.Radius * body.Radius;
        var discriminant = bb * bb - 4 * aa * cc;
        if (discriminant <= 0) yield break; // Tangent contact is legal.
        var root = Math.Sqrt(discriminant);
        yield return (-bb - root) / (2 * aa);
        yield return (-bb + root) / (2 * aa);
    }

    // Returns the portion of a segment that lies inside a circular footprint.
    // Tangent contact has no visible interval and remains legal movement.
    internal static bool TryInsideInterval(Point a, Point z, Body body, out double enter, out double exit)
    {
        enter = 0;
        exit = 1;
        var insideA = Squared(a, body.Center) < body.Radius * body.Radius - Epsilon;
        var insideZ = Squared(z, body.Center) < body.Radius * body.Radius - Epsilon;
        var roots = Crossings(a, z, body).Where(t => t > 0 && t < 1).OrderBy(t => t).ToArray();
        if (insideA)
        {
            if (roots.Length > 0) exit = roots[roots.Length - 1];
        }
        else if (insideZ)
        {
            if (roots.Length > 0) enter = roots[0];
        }
        else
        {
            if (roots.Length < 2) return false;
            enter = roots[0];
            exit = roots[roots.Length - 1];
        }
        return exit - enter > Epsilon;
    }

    internal static double FlyoverHeightFraction(Point point, IReadOnlyList<Body> flyoverBodies)
    {
        var fraction = 0.0;
        foreach (var body in flyoverBodies)
        {
            var distance = Math.Sqrt(Squared(point, body.Center));
            if (distance >= body.Radius) continue;
            fraction = Math.Max(fraction, 1 - distance / body.Radius);
        }
        return fraction;
    }

    internal static double PassageFraction(Point a, Point z, IReadOnlyList<Body> bodies)
    {
        var fraction = 1.0;
        foreach (var b in bodies)
        {
            if (!b.BlocksPassage) continue;
            var dx = z.X - a.X; var dz = z.Z - a.Z;
            var dot = (a.X - b.Center.X) * dx + (a.Z - b.Center.Z) * dz;
            if (Squared(a, b.Center) <= b.Radius * b.Radius + Epsilon)
            {
                if (dot < -Epsilon) return 0; // An existing overlap may only get shallower.
                continue;
            }
            foreach (var crossing in Crossings(a, z, b))
                if (crossing >= 0 && crossing < fraction) fraction = crossing;
        }
        return fraction;
    }

    internal static bool RouteIsClear(Point start, IReadOnlyList<Point> waypoints,
        IReadOnlyList<Body> bodies, bool preventOverlap)
    {
        var length = 0.0;
        var previous = start;
        foreach (var point in waypoints)
        {
            length += Length(previous, point);
            previous = point;
        }
        return RouteLimit(start, waypoints, length, bodies, preventOverlap) >= length - 0.0001;
    }

    // The visualizer can replace a legal searched approach route with its
    // simulated route. Do not let that handoff introduce an occupied endpoint.
    internal static bool CommitReplacementRoute(Point start, IReadOnlyList<Point> searched,
        IReadOnlyList<Point> replacement, IReadOnlyList<Body> bodies, bool preventOverlap) =>
        !RouteIsClear(start, searched, bodies, preventOverlap) ||
        RouteIsClear(start, replacement, bodies, preventOverlap);

    // Furthest safe stopping distance within the route/budget. Crossing allies
    // is fine, but only when the available movement can finish outside them.
    internal static double RouteLimit(Point start, IEnumerable<Point> waypoints, double budget,
        IReadOnlyList<Body> bodies, bool preventOverlap)
    {
        if (budget < 0 || double.IsNaN(budget) || double.IsInfinity(budget))
            throw new ArgumentOutOfRangeException(nameof(budget));
        var points = new List<Point> { start };
        var distances = new List<double> { 0 };
        var distance = 0.0;
        var previous = start;
        foreach (var next in waypoints)
        {
            var length = Length(previous, next);
            if (length < Epsilon) continue;
            var fraction = Math.Min(PassageFraction(previous, next, bodies), (budget - distance) / length);
            var endpoint = Point.Lerp(previous, next, fraction);
            distance += length * fraction;
            points.Add(endpoint); distances.Add(distance);
            if (fraction < 1 || distance >= budget - Epsilon) break;
            previous = next;
        }
        if (!preventOverlap || CanStop(points[points.Count - 1], bodies)) return distance;
        for (var index = points.Count - 1; index > 0; index--)
        {
            var a = points[index - 1]; var z = points[index];
            var candidates = new List<double> { 0, 1 };
            foreach (var b in bodies)
                if (b.BlocksStopping) candidates.AddRange(Crossings(a, z, b).Where(t => t >= 0 && t <= 1));
            foreach (var t in candidates.OrderByDescending(t => t))
                if (CanStop(Point.Lerp(a, z, t), bodies))
                    return distances[index - 1] + (distances[index] - distances[index - 1]) * t;
        }
        // Do not freeze a pre-existing overlap when a short outward step helps.
        var end = points[points.Count - 1];
        if (bodies.All(b => !b.BlocksStopping || Squared(end, b.Center) + Epsilon >= b.Radius * b.Radius ||
            Squared(start, b.Center) < b.Radius * b.Radius &&
            Squared(end, b.Center) + Epsilon >= Squared(start, b.Center))) return distance;
        return 0;
    }
}
