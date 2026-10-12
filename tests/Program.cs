using System;
using ArdubsEnemyIntelligence;

static void Equal(double expected, double actual, string label)
{
    if (Math.Abs(expected - actual) > 0.000001)
    {
        throw new Exception($"{label}: expected {expected}, got {actual}");
    }
}

static void EqualDecimal(decimal expected, decimal actual, string label)
{
    if (Math.Abs(expected - actual) > 0.000000001m)
    {
        throw new Exception($"{label}: expected {expected}, got {actual}");
    }
}

static void EqualText(string expected, string actual, string label)
{
    if (expected != actual)
    {
        throw new Exception($"{label}: expected {expected}, got {actual}");
    }
}

// Actor scope is independent of target scoring: opt-in allies must target
// their own enemies, without taking over party assets or neutral NPCs.
foreach (var enabled in new[] { false, true })
foreach (var sleeping in new[] { false, true })
foreach (var active in new[] { false, true })
    Equal(enabled && sleeping && active ? 0 : 1,
        SleepingTargetPolicy.Allows(enabled, sleeping, active) ? 1 : 0,
        "sleepers are not a fallback while any active opponent remains");
foreach (var combat in new[] { false, true })
foreach (var hostile in new[] { false, true })
foreach (var conscious in new[] { false, true })
foreach (var sleeping in new[] { false, true })
foreach (var incapacitated in new[] { false, true })
    Equal(combat && hostile && conscious && !sleeping && !incapacitated ? 1 : 0,
        SleepingTargetPolicy.IsActiveOpponent(combat, hostile, conscious, sleeping, incapacitated) ? 1 : 0,
        "combat hostility and incapacity, not path availability, determine active opponents");

static bool Managed(bool enabled = false, bool hostile = false, bool ally = true,
    bool controlled = false, bool party = false, bool playerFaction = false,
    bool pet = false, bool summoned = false, bool combat = true, bool turnBased = true, bool enemies = true) =>
    ManagedActorPolicy.Allows(combat, turnBased, controlled, party, playerFaction,
        hostile, ally, pet, summoned, enabled, enemies);

Equal(0, Managed() ? 1 : 0, "disabled allied NPC option leaves allies vanilla");
Equal(1, Managed(enabled: true) ? 1 : 0, "opt-in uncontrolled allied NPC");
Equal(1, Managed(enabled: true, playerFaction: true) ? 1 : 0,
    "uncontrolled player-faction NPC outside party can opt in");
Equal(0, Managed(enabled: true, ally: false) ? 1 : 0, "neutral NPC is not an ally");
Equal(0, Managed(enabled: true, controlled: true) ? 1 : 0, "player control is excluded");
Equal(0, Managed(enabled: true, party: true) ? 1 : 0, "party members are excluded");
Equal(0, Managed(enabled: true, party: true, hostile: true) ? 1 : 0,
    "party member with lost control is still excluded");
Equal(0, Managed(enabled: true, pet: true) ? 1 : 0, "allied pets are excluded");
Equal(0, Managed(enabled: true, summoned: true) ? 1 : 0, "allied summons are excluded");
foreach (var enemies in new[] { false, true })
foreach (var allies in new[] { false, true })
{
    Equal(enemies ? 1 : 0, Managed(enabled: allies, enemies: enemies, hostile: true, ally: false) ? 1 : 0,
        "enemy targeting independently follows enemy scope");
    Equal(allies ? 1 : 0, Managed(enabled: allies, enemies: enemies) ? 1 : 0,
        "allied targeting independently follows allied scope");
    Equal(0, Managed(enabled: allies, enemies: enemies, controlled: true) ? 1 : 0,
        "AI scope never takes over player-controlled units");
}
foreach (var enabled in new[] { false, true })
{
    Equal(1, Managed(enabled: enabled, hostile: true, ally: false) ? 1 : 0,
        "enemy AI remains enabled regardless of allied option");
    Equal(1, Managed(enabled: enabled, hostile: true, ally: false, pet: true, summoned: true) ? 1 : 0,
        "enemy pets and summons retain improved AI");
    Equal(0, Managed(enabled: enabled, hostile: true, ally: false, playerFaction: true) ? 1 : 0,
        "player-faction hostile actors remain excluded");
    Equal(0, Managed(enabled: enabled, hostile: true, ally: false, combat: false) ? 1 : 0,
        "out-of-combat actors excluded");
    Equal(0, Managed(enabled: enabled, hostile: true, ally: false, turnBased: false) ? 1 : 0,
        "real-time enemies excluded");
}
Equal(0, Managed(enabled: true, combat: false) ? 1 : 0, "out-of-combat allies excluded");
Equal(0, Managed(enabled: true, turnBased: false) ? 1 : 0, "real-time allies excluded");

var friendsFaction = ManagedActorPolicy.IsFriendlyCombatant(false, false, false, false, false, false);
var neutralFaction = ManagedActorPolicy.IsFriendlyCombatant(false, false, false, true, false, false);
Equal(1, friendsFaction ? 1 : 0, "native Friends faction qualifies even when formal IsAlly excludes the player");
Equal(0, neutralFaction ? 1 : 0, "being non-hostile does not make an unrelated Neutral faction friendly");
foreach (var hostile in new[] { false, true })
foreach (var forwardAlly in new[] { false, true })
foreach (var reverseAlly in new[] { false, true })
foreach (var factionIsNeutral in new[] { false, true })
foreach (var alwaysEnemy in new[] { false, true })
foreach (var enemyForEveryone in new[] { false, true })
    Equal(!hostile && (forwardAlly || reverseAlly || !factionIsNeutral && !alwaysEnemy && !enemyForEveryone) ? 1 : 0,
        ManagedActorPolicy.IsFriendlyCombatant(hostile, forwardAlly, reverseAlly, factionIsNeutral, alwaysEnemy, enemyForEveryone) ? 1 : 0,
        "friendly faction/relation recognition never overrides hostility");
var encounterGroup = new[] { neutralFaction, friendsFaction, neutralFaction };
var encounterAlly = ManagedActorPolicy.IsAlliedNpc(false, neutralFaction, encounterGroup);
Equal(1, encounterAlly ? 1 : 0, "neutral Sunrise Sword NPC inherits friendship from its combat group");
Equal(0, ManagedActorPolicy.IsAlliedNpc(false, neutralFaction, new[] { neutralFaction }) ? 1 : 0,
    "an unrelated all-neutral group remains outside allied AI scope");
Equal(0, ManagedActorPolicy.IsAlliedNpc(true, neutralFaction, encounterGroup) ? 1 : 0,
    "hostile actor cannot inherit friendship even from a mixed group");
foreach (var enabled in new[] { false, true })
foreach (var sleeping in new[] { false, true })
foreach (var awakeRemains in new[] { false, true })
{
    var managed = Managed(enabled: enabled, ally: encounterAlly);
    Equal(enabled ? 1 : 0, managed ? 1 : 0, "friendly-group NPC follows Apply to allied NPCs");
    Equal(enabled && sleeping ? 0 : 1,
        SleepingTargetPolicy.Allows(false, sleeping, awakeRemains, alliedNpc: managed) ? 1 : 0,
        "enabled friendly-group NPC never wakes a sleeper, even alone or with enemy sleep protection off");
}
foreach (var enemyOption in new[] { false, true })
foreach (var sleeping in new[] { false, true })
foreach (var active in new[] { false, true })
foreach (var alliedNpc in new[] { false, true })
    Equal(sleeping && (alliedNpc || enemyOption && active) ? 0 : 1,
        SleepingTargetPolicy.Allows(enemyOption, sleeping, active, alliedNpc) ? 1 : 0,
        "allied sleep prohibition is absolute; enemy-only option and fallback remain independent");
Equal(0, Managed(enabled: true, ally: encounterAlly, controlled: true) ? 1 : 0, "friendly group does not take over player commands");
Equal(0, Managed(enabled: true, ally: encounterAlly, party: true) ? 1 : 0, "friendly group does not take over party members");
Equal(0, Managed(enabled: true, ally: encounterAlly, pet: true) ? 1 : 0, "friendly group leaves allied pets vanilla");
Equal(0, Managed(enabled: true, ally: encounterAlly, summoned: true) ? 1 : 0, "friendly group leaves allied summons vanilla");
encounterGroup[1] = false;
Equal(0, ManagedActorPolicy.IsAlliedNpc(false, neutralFaction, encounterGroup) ? 1 : 0,
    "group friendship is re-evaluated after membership or relationship changes");

var bodyStart = new BodyBlockingPolicy.Point(0, 0);
foreach (var hostile in new[] { false, true })
foreach (var reverseHostile in new[] { false, true })
foreach (var ally in new[] { false, true })
foreach (var reverseAlly in new[] { false, true })
{
    var friendly = BodyBlockingPolicy.IsFriendlyForMovement(hostile, reverseHostile, ally, reverseAlly);
    Equal(!hostile && !reverseHostile && (ally || reverseAlly) ? 1 : 0, friendly ? 1 : 0,
        "movement friendship recognizes either direction but never overrides hostility");
    Equal(friendly ? 1 : 0, BodyBlockingPolicy.IgnoreFriendlyForStep(friendly, true, true) ? 1 : 0,
        "friendly five-foot exception applies equally to party assets and outside-party NPCs");
    Equal(friendly ? 0 : 1, BodyBlockingPolicy.BlocksStopping(friendly, true, true, false) ? 1 : 0,
        "five-foot stopping exception does not leak to neutral or hostile units");
}
var outsidePartyAlly = BodyBlockingPolicy.IsFriendlyForMovement(false, false, false, true);
Equal(0, BodyBlockingPolicy.BlocksPassage(false, outsidePartyAlly, false, true, true, true) ? 1 : 0,
    "outside-party friendly NPC allows ordinary passage when enabled");
Equal(1, BodyBlockingPolicy.BlocksStopping(outsidePartyAlly, false, true, false) ? 1 : 0,
    "outside-party friendly NPC still blocks ordinary stopping when overlap is off");
Equal(0, BodyBlockingPolicy.BlocksStopping(outsidePartyAlly, false, true, true) ? 1 : 0,
    "ordinary friendly stopping option includes outside-party NPCs");
var bodyEnd = new BodyBlockingPolicy.Point(6, 0);
var enemyBody = new[] { new BodyBlockingPolicy.Body(new BodyBlockingPolicy.Point(3, 0), 1, true) };
var allyBody = new[] { new BodyBlockingPolicy.Body(new BodyBlockingPolicy.Point(3, 0), 1, false) };
var clearApproach = new[] { new BodyBlockingPolicy.Point(2, 2) };
var occupiedVisual = new[] { new BodyBlockingPolicy.Point(3, 0) };
Equal(0, BodyBlockingPolicy.CommitReplacementRoute(bodyStart, clearApproach,
    occupiedVisual, allyBody, true) ? 1 : 0,
    "occupied visual attack endpoint must not replace a clear searched approach route");
Equal(1, BodyBlockingPolicy.CommitReplacementRoute(bodyStart, clearApproach,
    clearApproach, allyBody, true) ? 1 : 0,
    "legal visual approach replacement keeps native behavior");
Equal(1, BodyBlockingPolicy.CommitReplacementRoute(bodyStart, occupiedVisual,
    occupiedVisual, allyBody, true) ? 1 : 0,
    "a searched route that is already unsafe does not suppress the native handoff");
Equal(0, BodyBlockingPolicy.SimulationStopsHere(true, false, 5, 0.25) ? 1 : 0, "ordinary simulation can continue");
Equal(1, BodyBlockingPolicy.SimulationStopsHere(false, false, 5, 0.25) ? 1 : 0, "native range/LOS stop uses actual endpoint");
Equal(1, BodyBlockingPolicy.SimulationStopsHere(true, true, 5, 0.25) ? 1 : 0, "native exhausted budget uses actual endpoint");
foreach (var budget in new[] { -0.2, 0, 0.1, 0.25 })
    Equal(1, BodyBlockingPolicy.SimulationStopsHere(true, false, budget, 0.25) ? 1 : 0,
        "borrowed final tick does not pass negative budget to geometric route checks");
foreach (var step in new[] { false, true })
foreach (var single in new[] { false, true })
foreach (var autoStop in new[] { false, true })
{
    var useTotal = BodyBlockingPolicy.MayUseTotalMovement(step, single, autoStop);
    Equal(!step && !single && !autoStop ? 1 : 0, useTotal ? 1 : 0,
        "only unrestricted ordinary movement can cross an action boundary");
    Equal(useTotal ? 8 : 4.1, BodyBlockingPolicy.CrossingBudget(4.1, 8, 0, useTotal),
        "legal movement mode selects crossing budget");
}
Equal(3, BodyBlockingPolicy.CrossingBudget(1, 8, 5, true), "preview subtracts travelled route length");
Equal(2, BodyBlockingPolicy.CrossingBudget(2, 8, 6, true), "second segment has no extra movement beyond turn budget");
Equal(0, BodyBlockingPolicy.SimulationStopsHere(true, true, 0.25, 4, 0.25) ? 1 : 0,
    "first action boundary is not an actual stop when second action is available");
Equal(1, BodyBlockingPolicy.SimulationStopsHere(false, true, 0.25, 4, 0.25) ? 1 : 0,
    "actual native endpoint or approach stop is never extended");
Equal(1, BodyBlockingPolicy.SimulationStopsHere(true, true, 0.25, 0.25, 0.25) ? 1 : 0,
    "total exhausted movement remains a real endpoint");
var budgetLargeAlly = new[] { new BodyBlockingPolicy.Body(new BodyBlockingPolicy.Point(3, 0), 2, false) };
var budgetDestination = new[] { new BodyBlockingPolicy.Point(8, 0) };
Equal(1, BodyBlockingPolicy.RouteLimit(bodyStart, budgetDestination, 4.1, budgetLargeAlly, true),
    "reproduction: first move alone ends inside large friendly body");
Equal(8, BodyBlockingPolicy.RouteLimit(bodyStart, budgetDestination,
    BodyBlockingPolicy.CrossingBudget(4.1, 8, 0, true), budgetLargeAlly, true),
    "full legal ordinary movement exits the same large friendly body");
foreach (var ally in new[] { false, true })
foreach (var step in new[] { false, true })
foreach (var ignoreStep in new[] { false, true })
foreach (var allowStop in new[] { false, true })
{
    var ignored = ally && step && ignoreStep;
    Equal(ignored ? 1 : 0, BodyBlockingPolicy.IgnoreFriendlyForStep(ally, step, ignoreStep) ? 1 : 0,
        "only actual enabled five-foot steps bypass friendly passage");
    var stops = !ally || !(allowStop || ignored);
    Equal(stops ? 1 : 0, BodyBlockingPolicy.BlocksStopping(ally, step, ignoreStep, allowStop) ? 1 : 0,
        "friendly stopping option matrix never exempts a hostile body");
}
var openFriendly = new[] { new BodyBlockingPolicy.Body(new BodyBlockingPolicy.Point(3, 0), 1, false, false) };
Equal(3, BodyBlockingPolicy.RouteLimit(bodyStart, new[] { bodyEnd }, 3, openFriendly, true),
    "friendly endpoint exemption allows stopping within an ally despite global overlap protection");
Equal(3, BodyBlockingPolicy.RouteLimit(bodyStart, new[] { new BodyBlockingPolicy.Point(3, 0) }, 6, openFriendly, true),
    "friendly occupied clicked destination remains unchanged");
if (!BodyBlockingPolicy.CanStop(new BodyBlockingPolicy.Point(3, 0), openFriendly))
    throw new Exception("Friendly endpoint exemption must permit an ally center.");
var blockedFriendlyPassage = new[] { new BodyBlockingPolicy.Body(new BodyBlockingPolicy.Point(3, 0), 1, true, false) };
Equal(2, BodyBlockingPolicy.RouteLimit(bodyStart, new[] { bodyEnd }, 6, blockedFriendlyPassage, true),
    "ordinary friendly stopping option does not override an explicitly blocked passage setting");
foreach (var step in new[] { false, true })
{
    var ignore = BodyBlockingPolicy.IgnoreFriendlyForStep(true, step, true);
    var bodies = new[] { new BodyBlockingPolicy.Body(new BodyBlockingPolicy.Point(1, 0), 0.5,
        !ignore, BodyBlockingPolicy.BlocksStopping(true, step, true, false)) };
    Equal(step ? 1 : 0.5, BodyBlockingPolicy.RouteLimit(bodyStart,
        new[] { new BodyBlockingPolicy.Point(1, 0) }, 1, bodies, true),
        "five-foot exception bypasses passage and stopping together; normal movement remains blocked");
}
var mixedBodies = new[] {
    openFriendly[0],
    new BodyBlockingPolicy.Body(new BodyBlockingPolicy.Point(5, 0), 1, false, true)
};
Equal(3, BodyBlockingPolicy.RouteLimit(bodyStart, new[] { bodyEnd }, 3, mixedBodies, true),
    "friendly exemption does not disable every occupied endpoint");
Equal(4, BodyBlockingPolicy.RouteLimit(bodyStart, new[] { bodyEnd }, 5, mixedBodies, true),
    "hostile stopping protection remains active even when passage is allowed, e.g. flight");
if (BodyBlockingPolicy.CanStop(new BodyBlockingPolicy.Point(5, 0), mixedBodies))
    throw new Exception("Friendly endpoint exemption leaked to a hostile body.");
Equal(0, BodyBlockingPolicy.OccupiesSpace(false, false, false, true) ? 1 : 0, "downed/dead body occupies no space");
Equal(0, BodyBlockingPolicy.OccupiesSpace(true, true, false, true) ? 1 : 0, "living sleeping body occupies no space");
Equal(0, BodyBlockingPolicy.OccupiesSpace(true, false, true, true) ? 1 : 0, "unconscious condition occupies no space even with living life state");
Equal(1, BodyBlockingPolicy.OccupiesSpace(true, false, false, true) ? 1 : 0, "awake conscious body remains solid, including prone units");
Equal(1, BodyBlockingPolicy.OccupiesSpace(true, true, false, false) ? 1 : 0, "sleeping-space option can restore conscious-life sleeping blocker");
var sleepingBodies = BodyBlockingPolicy.OccupiesSpace(true, true, false, true) ? enemyBody : Array.Empty<BodyBlockingPolicy.Body>();
Equal(3, BodyBlockingPolicy.RouteLimit(bodyStart, new[] { new BodyBlockingPolicy.Point(3, 0) }, 3, sleepingBodies, true),
    "movement can finish on a sleeping unit's position");
Equal(6, BodyBlockingPolicy.RouteLimit(bodyStart, new[] { bodyEnd }, 6, sleepingBodies, true),
    "movement can cross a sleeping unit's position");
Equal(1, BodyBlockingPolicy.BlocksPassage(true, false, false, true, true, true) ? 1 : 0,
    "grounded mover still blocked by an enemy");
Equal(0, BodyBlockingPolicy.BlocksPassage(true, false, true, true, true, true) ? 1 : 0,
    "flying mover can cross an enemy");
Equal(1, BodyBlockingPolicy.BlocksPassage(true, false, true, true, true, false) ? 1 : 0,
    "disabling flying passage restores hostile blocking");
Equal(1, BodyBlockingPolicy.BlocksPassage(false, true, true, true, false, true) ? 1 : 0,
    "flight exception does not override explicit allied passage setting");
var enemyForFlyer = new[] { new BodyBlockingPolicy.Body(new BodyBlockingPolicy.Point(3, 0), 1,
    BodyBlockingPolicy.BlocksPassage(true, false, true, true, true, true)) };
Equal(1, BodyBlockingPolicy.PassageFraction(bodyStart, bodyEnd, enemyForFlyer), "flyer sweeps through hostile footprint");
Equal(6, BodyBlockingPolicy.RouteLimit(bodyStart, new[] { bodyEnd }, 6, enemyForFlyer, true),
    "flyer with enough movement exits the enemy body");
Equal(2, BodyBlockingPolicy.RouteLimit(bodyStart, new[] { bodyEnd }, 3, enemyForFlyer, true),
    "flyer with insufficient movement cannot stop inside enemy");
var flyingBudgetStop = BodyBlockingPolicy.RouteLimit(bodyStart, new[] { bodyEnd }, 3,
    enemyForFlyer, true);
Equal(2, BodyBlockingPolicy.PositionAlong(bodyStart, new[] { bodyEnd }, flyingBudgetStop).X,
    "ability assessment uses the same legal stop as flying movement");
Equal(3, BodyBlockingPolicy.PositionAlong(bodyStart, new[] {
    new BodyBlockingPolicy.Point(0, 2), new BodyBlockingPolicy.Point(4, 2)
}, 5).X, "ability stop follows the route instead of a straight-line shortcut");
Equal(2, BodyBlockingPolicy.PositionAlong(bodyStart, new[] {
    new BodyBlockingPolicy.Point(0, 2), new BodyBlockingPolicy.Point(4, 2)
}, 5).Z, "ability stop retains the route's second segment");
Equal(2, BodyBlockingPolicy.RouteLimit(bodyStart, new[] { new BodyBlockingPolicy.Point(3, 0) }, 6, enemyForFlyer, true),
    "flying passage never makes an occupied destination legal");
if (!BodyBlockingPolicy.CanTraverse(new BodyBlockingPolicy.Point(3, 0), bodyStart, enemyForFlyer) ||
    BodyBlockingPolicy.CanStop(new BodyBlockingPolicy.Point(3, 0), enemyForFlyer))
    throw new Exception("Flying enemy passage must distinguish traversable and stoppable positions.");
Equal(1.0 / 3, BodyBlockingPolicy.PassageFraction(bodyStart, bodyEnd, enemyBody),
    "swept movement stops at enemy boundary even when a whole body is crossed in one tick");
Equal(1, BodyBlockingPolicy.PassageFraction(bodyStart, bodyEnd, allyBody), "allied passage remains legal");
Equal(2, BodyBlockingPolicy.RouteLimit(bodyStart, new[] { bodyEnd }, 6, enemyBody, true),
    "hostile passage blocked on both planning and movement geometry");
Equal(6, BodyBlockingPolicy.RouteLimit(bodyStart, new[] { bodyEnd }, 6, allyBody, true),
    "enough movement permits crossing and exiting an ally");
Equal(2, BodyBlockingPolicy.RouteLimit(bodyStart, new[] { bodyEnd }, 3, allyBody, true),
    "insufficient movement stops before entering ally instead of ending inside");
Equal(4, BodyBlockingPolicy.RouteLimit(bodyStart, new[] { bodyEnd }, 4, allyBody, true),
    "stopping exactly at exit boundary is legal");
Equal(3, BodyBlockingPolicy.RouteLimit(bodyStart, new[] { bodyEnd }, 3, allyBody, false),
    "stopping-overlap toggle independently restores native endpoint permission");
Equal(2, BodyBlockingPolicy.RouteLimit(bodyStart, new[] { new BodyBlockingPolicy.Point(3, 0) }, 6, allyBody, true),
    "occupied clicked endpoint trims to preceding free position");
Equal(1, BodyBlockingPolicy.PassageFraction(new BodyBlockingPolicy.Point(0, 1),
    new BodyBlockingPolicy.Point(6, 1), enemyBody), "tangent passage does not block");
var detour = new[] { new BodyBlockingPolicy.Point(3, 2), bodyEnd };
Equal(2 * Math.Sqrt(13), BodyBlockingPolicy.RouteLimit(bodyStart, detour, 20, enemyBody, true),
    "native detour around body stays usable rather than rejecting the target outright");
if (!BodyBlockingPolicy.RouteIsClear(bodyStart, detour, enemyBody, true) ||
    BodyBlockingPolicy.RouteIsClear(bodyStart, new[] { bodyEnd }, enemyBody, true))
    throw new Exception("Unsafe raycast shortcut must preserve the clear unsmoothed grid detour.");
var overlapStart = new BodyBlockingPolicy.Point(3.5, 0);
Equal(1, BodyBlockingPolicy.PassageFraction(overlapStart, bodyEnd, enemyBody), "outward escape from pre-existing overlap");
Equal(0, BodyBlockingPolicy.PassageFraction(overlapStart, new BodyBlockingPolicy.Point(3.2, 0), enemyBody),
    "existing overlap cannot get deeper");
Equal(0.2, BodyBlockingPolicy.RouteLimit(overlapStart, new[] { bodyEnd }, 0.2, enemyBody, true),
    "short outward movement helps an overlap even when one turn cannot fully exit");
if (BodyBlockingPolicy.CanTraverse(new BodyBlockingPolicy.Point(3, 0), bodyStart, enemyBody))
    throw new Exception("Native search must not traverse an enemy center.");
if (!BodyBlockingPolicy.CanTraverse(new BodyBlockingPolicy.Point(3, 0), bodyStart, allyBody))
    throw new Exception("Allies must remain traversable when allowed.");
if (!BodyBlockingPolicy.CanTraverse(new BodyBlockingPolicy.Point(3.8, 0), overlapStart, enemyBody) ||
    BodyBlockingPolicy.CanTraverse(new BodyBlockingPolicy.Point(3.2, 0), overlapStart, enemyBody))
    throw new Exception("Search and live movement must agree on escaping, not worsening, an overlap.");
if (!BodyBlockingPolicy.CanTraverse(new BodyBlockingPolicy.Point(4.1, 0), bodyStart, enemyBody))
    throw new Exception("Grid search must allow a free node near the actual body boundary.");
if (BodyBlockingPolicy.CanTraverseEdge(new BodyBlockingPolicy.Point(1.5, 0.8),
        new BodyBlockingPolicy.Point(4.5, 0.8), enemyBody) ||
    !BodyBlockingPolicy.CanTraverseEdge(new BodyBlockingPolicy.Point(1.5, 1.1),
        new BodyBlockingPolicy.Point(4.5, 1.1), enemyBody))
    throw new Exception("Exact swept-edge clearance must reject body crossings without inflated nodes.");
var overlappingAllies = new[] {
    new BodyBlockingPolicy.Body(new BodyBlockingPolicy.Point(3, 0), 1, false),
    new BodyBlockingPolicy.Body(new BodyBlockingPolicy.Point(4, 0), 1, false)
};
Equal(2, BodyBlockingPolicy.RouteLimit(bodyStart, new[] { bodyEnd }, 4.5, overlappingAllies, true),
    "overlapping blocker footprints use the last position outside their union");
Equal(5.5, BodyBlockingPolicy.RouteLimit(bodyStart, new[] { bodyEnd }, 5.5, overlappingAllies, true),
    "a full crossing of both allied footprints is still allowed");
var emptyBodies = Array.Empty<BodyBlockingPolicy.Body>();
Equal(5, BodyBlockingPolicy.RouteLimit(bodyStart, new[] { new BodyBlockingPolicy.Point(3, 0, 4) }, 5, emptyBodies, true),
    "movement budgets retain three-dimensional route length on slopes");
// Independently sample movement/budget results across many approach angles.
for (var angle = 0; angle < 360; angle += 13)
{
    var radians = angle * Math.PI / 180;
    var from = new BodyBlockingPolicy.Point(3 + 4 * Math.Cos(radians), 4 * Math.Sin(radians));
    var to = new BodyBlockingPolicy.Point(3 - 4 * Math.Cos(radians), -4 * Math.Sin(radians));
    Equal(0.375, BodyBlockingPolicy.PassageFraction(from, to, enemyBody), "rotation-invariant swept clearance");
    for (var budget = 0.25; budget <= 8; budget += 0.25)
    {
        var limit = BodyBlockingPolicy.RouteLimit(from, new[] { to }, budget, allyBody, true);
        if (limit > budget + 0.000001 || !BodyBlockingPolicy.CanStop(BodyBlockingPolicy.Point.Lerp(from, to, limit / 8), allyBody))
            throw new Exception("Route limit must fit the movement budget and stop outside allied footprints.");
    }
}

Equal(0, TargetingPolicy.ReassessmentChance(-5, 1, 20), "below zero endpoint");
Equal(0, TargetingPolicy.ReassessmentChance(1, 1, 20), "Int 1 reassessment");
Equal(9.0 / 19, TargetingPolicy.ReassessmentChance(10, 1, 20), "Int 10 reassessment");
Equal(1, TargetingPolicy.ReassessmentChance(20, 1, 20), "Int 20 reassessment");
Equal(1, TargetingPolicy.ReassessmentChance(40, 1, 20), "above certain endpoint");
Equal(0, TargetingPolicy.ReassessmentChance(5, 5, 15), "custom minimum");
Equal(0.5, TargetingPolicy.ReassessmentChance(10, 5, 15), "custom midpoint");
Equal(1, TargetingPolicy.ReassessmentChance(15, 5, 15), "custom maximum");
Equal(0.5, TargetingPolicy.ReassessmentChance(0, int.MinValue, int.MaxValue),
    "extreme custom endpoints do not overflow");
Equal(0, TargetingPolicy.KnowledgeChance(1, 1, 10), "Int 1 Strength");
Equal(1, TargetingPolicy.KnowledgeChance(10, 1, 10), "Int 10 Strength");
Equal(0, TargetingPolicy.KnowledgeChance(10, 10, 25), "Int 10 AC");
Equal(1, TargetingPolicy.KnowledgeChance(25, 10, 25), "Int 25 AC");
Equal(10.0 / 15, TargetingPolicy.KnowledgeChance(20, 10, 25), "Int 20 AC");
EqualDecimal(1.048m, TargetingPolicy.StrengthFactor(14), "Strength 14 factor");
EqualDecimal(1.48m, TargetingPolicy.StrengthFactor(50), "Strength factor has no cap");
EqualDecimal(5m / 6m, TargetingPolicy.ArmorFactor(24), "AC 24 factor");
EqualDecimal(1.5m, TargetingPolicy.ArmorFactor(0), "AC zero stays defined");
EqualDecimal(0.2m, TargetingPolicy.ArmorFactor(100), "high AC factor has no floor");
foreach (var vanillaScore in new[] { -1m, 0m, 0.000001m, 1m, 100m, decimal.MaxValue })
{
    EqualDecimal(0m, TargetingPolicy.EligibleTargetScore(vanillaScore, false),
        "skipped target must not retain its vanilla score");
    EqualDecimal(vanillaScore, TargetingPolicy.EligibleTargetScore(vanillaScore, true),
        "eligible target retains vanilla eligibility, including nonpositive rejections");
}
var emberSkipped = TargetingPolicy.EligibleTargetScore(1m, false);
var shadowScored = new TargetScoreBreakdown(0.8m, 1.048m, 1m, 1m, 1m)
    .WithDistance(0.532010741m).Score;
if (emberSkipped >= shadowScored)
{
    throw new Exception("Skipped Ember must not beat the scored Shadow on a leftover vanilla score.");
}
Equal(1, TargetingPolicy.ObserveWeaponDamage(0, true, 0, 12, 0, true),
    "immunity-blocked hit increments even with zero DamageWithoutReduction");
Equal(2, TargetingPolicy.ObserveWeaponDamage(1, true, 0, 8, 0),
    "positive raw damage still detects an early blocked hit without an immunity flag");
Equal(3, TargetingPolicy.ObserveWeaponDamage(2, true, 0, 0, 0, true),
    "explicit immunity evidence counts a hit even if raw roll and bonus total zero");
Equal(1, TargetingPolicy.ObserveWeaponDamage(0, true, 0, 12, 12),
    "ordinary damage-reduction-blocked hit still increments");
Equal(1, TargetingPolicy.ObserveWeaponDamage(0, true, 0, 0, 1),
    "game minimum damage before reduction still counts");
Equal(0, TargetingPolicy.ObserveWeaponDamage(3, true, 4, 12, 12),
    "successful weapon damage clears prior ineffective evidence");
Equal(0, TargetingPolicy.ObserveWeaponDamage(3, true, 4, 12, 12, true),
    "partly immune mixed damage is effective if any damage gets through");
Equal(3, TargetingPolicy.ObserveWeaponDamage(3, false, 0, 12, 12, true),
    "misses, fake checks, and non-weapon events do not add evidence");
Equal(3, TargetingPolicy.ObserveWeaponDamage(3, false, 4, 12, 12),
    "excluded damage does not clear weapon evidence either");
Equal(3, TargetingPolicy.ObserveWeaponDamage(3, true, 0, 0, 0),
    "zero damage without blocked-damage evidence is ignored");
Equal(3, TargetingPolicy.ObserveWeaponDamage(3, true, -1, 12, 12),
    "a negative result is not a zero-damage hit");
try
{
    TargetingPolicy.ObserveWeaponDamage(int.MaxValue, true, 0, 1, 0, true);
    throw new Exception("Ineffective-hit counter overflow should fail rather than wrap or clamp.");
}
catch (OverflowException)
{
}
try
{
    TargetingPolicy.KnowledgeChance(10, 10, 10);
    throw new Exception("Invalid equal endpoints should fail.");
}
catch (ArgumentOutOfRangeException)
{
}

for (var intelligence = 1; intelligence < 30; intelligence++)
{
    if (TargetingPolicy.ReassessmentChance(intelligence, 1, 20) >
        TargetingPolicy.ReassessmentChance(intelligence + 1, 1, 20))
    {
        throw new Exception("Reassessment chance must not decrease with Intelligence.");
    }
}

var neutral = new TargetScoreBreakdown(0.8m, 1m, 1m, 1m, 1m);
EqualDecimal(1m, neutral.Risk, "unknown offensive knowledge is neutral");
EqualDecimal(1m, neutral.Squish, "unknown defensive knowledge is neutral");
EqualDecimal(1m, neutral.Dist, "Dist is neutral before the game supplies a path");
EqualDecimal(1m, neutral.Score, "positive vanilla score is an eligibility gate, not a multiplier");
var grouped = new TargetScoreBreakdown(0.8m, 1.048m, 1.25m, 5m / 6m, 0.35m);
EqualDecimal(1.31m, grouped.Risk, "Risk groups Strength and caster factors");
EqualDecimal((5m / 6m) * 0.35m, grouped.Squish, "Squish groups AC and ineffective hits");
EqualDecimal(1.048m * 1.25m * (5m / 6m) * 0.35m, grouped.Score,
    "only Risk and Squish contribute before path scoring");
foreach (var gameScore in new[] { 0.000001m, 0.8m, 1m, 100m, decimal.MaxValue })
{
    EqualDecimal(grouped.Score,
        new TargetScoreBreakdown(gameScore, 1.048m, 1.25m, 5m / 6m, 0.35m).Score,
        "all positive vanilla scores produce the same custom score");
    EqualDecimal(grouped.WithDistance(0.25m).Score,
        new TargetScoreBreakdown(gameScore, 1.048m, 1.25m, 5m / 6m, 0.35m, 0.25m).Score,
        "Dist cannot reintroduce a positive vanilla weight");
}
EqualDecimal(0m, new TargetScoreBreakdown(0m, 1.48m, 1.25m, 1.5m, 1m).Score,
    "grouping cannot revive a zero vanilla score");
EqualDecimal(0m, new TargetScoreBreakdown(-1m, 1.48m, 1.25m, 1.5m, 1m).WithDistance(2m).Score,
    "negative vanilla scores stay rejected after distance scoring");
EqualDecimal(0m, new TargetScoreBreakdown(0m, 1.48m, 1.25m, 1.5m, 1m).WithDistance(2m).Score,
    "changing distance cannot revive a rejected target");
var trace = new TargetScoreBreakdown(0.8m, 1.048m, 1m, 1m, 1m)
    .Describe("Str[14 => 1.05]", "Caster[Unknown => 1]", "AC[Unknown => 1]", "Ineffective[0 hits => 1]");
EqualText("Risk: 1.05 = Str[14 => 1.05] * Caster[Unknown => 1]\n" +
    "Squish: 1 = AC[Unknown => 1] * Ineffective[0 hits => 1]\n" +
    "Dist: 1 = pending game path evaluation\n" +
    "Math: 1.05 = Risk[1.05] * Squish[1] * Dist[1]",
    trace, "totals precede each calculation");
EqualText("Risk: 1.05 = Str[14 => 1.05] * Caster[No => 1]\n" +
    "Squish: 0.83 = AC[24 => 0.83] * Ineffective[0 hits => 1]\n" +
    "Dist: 1 = pending game path evaluation\n" +
    "Math: 0.87 = Risk[1.05] * Squish[0.83] * Dist[1]",
    new TargetScoreBreakdown(0.8m, 1.048m, 1m, 5m / 6m, 1m).Describe(
        "Str[14 => 1.05]", "Caster[No => 1]", "AC[24 => 0.83]", "Ineffective[0 hits => 1]"),
    "fractional totals display at most two decimals");

DistancePlan Plan(decimal meters, int opportunities = 0, bool attack = true,
    bool full = true, bool step = false, decimal moveRange = 9m, decimal totalRange = 18m,
    bool fullLater = true) => new DistancePlan(meters, moveRange, totalRange, 9m, 18m,
        4, attack, full, fullLater, step, 1.524m, opportunities);
var inRange = Plan(0m);
if (inRange.Describe(0m, 9m, 18m, 18m, 4, 0, "test", false)
    .Contains("movement and future turns estimated"))
{
    throw new Exception("Dist log must not include the removed estimate suffix.");
}
Equal(4, inRange.AttacksNow, "in-range enemy gets full weapon attack count");
Equal(0, inRange.WaitTurns, "in-range enemy attacks now");
EqualDecimal(1m, inRange.Factor, "in-range full attack has neutral Dist");
var stepPlan = Plan(1.524m, step: true);
Equal(4, stepPlan.AttacksNow, "legal 5ft step preserves full attack");
Equal(1, Plan(1.525m, step: true).AttacksNow, "route beyond step loses full attack");
Equal(1, Plan(1m).AttacksNow, "ordinary movement is a single attack");
Equal(1, Plan(0m, full: false).AttacksNow, "already used move action prevents full attack");
Equal(1, Plan(9m).AttacksNow, "move + attack boundary is inclusive");
var nextTurn = Plan(9.001m);
Equal(0, nextTurn.AttacksNow, "beyond move + attack cannot attack this turn");
Equal(1, nextTurn.WaitTurns, "double move reaches nearby target for next turn");
Equal(4, nextTurn.FirstAttacks, "arriving this turn permits full attack next turn");
Equal(1, Plan(27m).WaitTurns, "double move now plus one move next turn");
Equal(1, Plan(27m).FirstAttacks, "moving next turn allows only single attack");
Equal(2, Plan(27.001m).WaitTurns, "target beyond next move + attack costs another turn");
Equal(4, Plan(31m).FirstAttacks, "extra approach turn can arrive before full attack turn");
Equal(2, Plan(40m).WaitTurns, "longer path needs two waiting turns");
Equal(1, Plan(40m).FirstAttacks, "arrival during attacking turn is a single attack");
Equal(1, Plan(0m, attack: false, full: false).WaitTurns, "no standard action delays attack");
Equal(4, Plan(0m, attack: false, full: false).FirstAttacks, "spent current actions reset next turn");
Equal(1, Plan(18m, fullLater: false).FirstAttacks, "persistent full-attack restriction remains next turn");
EqualDecimal(0.8m, Plan(9m, 1).OpportunityFactor, "one threatened exit penalty");
EqualDecimal(0.5m, Plan(9m, 4).OpportunityFactor, "four available AoOs penalty");
EqualDecimal(1m / 3.5m, Plan(9m, 10).OpportunityFactor, "AoO penalty has no score floor");
EqualDecimal(0.8m, Plan(9m).RouteFactor, "within-turn distance tiebreaker");
var distant = Plan(10000m);
if (distant.Factor <= 0m || distant.Factor >= 0.0001m)
{
    throw new Exception("Distant reachable targets retain an uncapped positive discounted score.");
}
var stationary = new DistancePlan(0m, 0m, 0m, 0m, 0m, 1, true, false, false, false, 0m, 0);
EqualDecimal(1m, stationary.Factor, "zero speed does not invalidate a target already in range");
var restricted = new DistancePlan(10m, 0m, 9m, 0m, 9m, 4, true, false, false, false, 0m, 0);
Equal(2, restricted.WaitTurns, "staggered actor needs separate moving and attacking turns");
try
{
    new DistancePlan(1m, 0m, 0m, 0m, 0m, 1, true, false, false, false, 0m, 0);
    throw new Exception("Moving at zero speed should fail rather than divide by zero or clamp.");
}
catch (ArgumentException)
{
}
var castInRange = new AbilityDistancePlan(0m, 1.524m, 18m, 1.524m, 18m, true);
Equal(0, castInRange.WaitTurns, "in-range full-round ability can act now");
EqualDecimal(1m, castInRange.Factor, "in-range ability has neutral Dist");
var castAfterStep = new AbilityDistancePlan(6m, 1.524m, 18m, 1.524m, 18m, true);
Equal(1, castAfterStep.WaitTurns, "full-round ability cannot follow ordinary movement");
EqualDecimal(0.1m, castAfterStep.TimingFactor, "a cast delayed to next turn is strongly discounted");
Equal(0, new AbilityDistancePlan(6m, 9m, 18m, 9m, 18m, true).WaitTurns,
    "standard ability can follow one move action");
Equal(1, new AbilityDistancePlan(9.001m, 9m, 18m, 9m, 18m, true).WaitTurns,
    "standard ability just beyond move-and-act range waits a turn");
Equal(2, new AbilityDistancePlan(27.001m, 9m, 18m, 9m, 18m, true).WaitTurns,
    "distant ability accounts for additional approach turns");
Equal(1, new AbilityDistancePlan(0m, 0m, 0m, 1.524m, 18m, false).WaitTurns,
    "spent action delays an ability even when already in range");
if (24.83m * castAfterStep.Factor >= 13.37m * castInRange.Factor)
    throw new Exception("Immediate lower-risk cast must outrank a distant high-risk cast.");
try
{
    new AbilityDistancePlan(1m, 0m, 0m, 0m, 0m, true);
    throw new Exception("Moving cast with zero speed should fail instead of dividing by zero.");
}
catch (ArgumentException)
{
}
EqualDecimal(grouped.Score * 0.25m, grouped.WithDistance(0.25m).Score,
    "game path Dist combines with Risk and Squish only");
EqualDecimal(0m, grouped.WithDistance(0m).Score, "unavailable path rejects target");
var distTrace = neutral.WithDistance(Plan(9m).Factor).Describe(
    "Str[Unknown => 1]", "Caster[Unknown => 1]", "AC[Unknown => 1]", "Ineffective[0 hits => 1]",
    Plan(9m).Describe(9m, 9m, 18m, 18m, 4, 0, "threats ignored", false));
if (!distTrace.Contains("Dist: 0.46 = Attacks[0.57] * Turns[wait 0 => 1] * Route[0.8] * AoO[1]") ||
    !distTrace.Contains("\n  Attacks[now 1; first attacking turn 1/4; lost fraction 0.75]") ||
    !distTrace.Contains("Math: 0.46 = Risk[1] * Squish[1] * Dist[0.46]") ||
    distTrace.Contains("Base["))
{
    throw new Exception("The log must include path/action details and the combined Dist score.");
}
for (var meters = 1m; meters < 120m; meters += 0.125m)
{
    if (Plan(meters).Factor < Plan(meters + 0.125m).Factor)
    {
        throw new Exception("Longer otherwise-identical routes must not score better at action/turn boundaries.");
    }
}
if (Plan(9m).Factor <= Plan(9.001m).Factor || Plan(27m).Factor <= Plan(27.001m).Factor)
{
    throw new Exception("A full attack later must not outrank an earlier single attack on distance alone.");
}

var gargoyleOne = new LabelUnit("Gargoyle", true);
var player = new LabelUnit("Seelah", false);
var friendlyGargoyle = new LabelUnit("Gargoyle", false);
var gargoyleTwo = new LabelUnit("Gargoyle", true);
var shadow = new LabelUnit("Mournful Shadow", false);
var demon = new LabelUnit("Demon", true);
var units = new[] { gargoyleOne, player, friendlyGargoyle, gargoyleTwo, shadow, demon };
string Label(LabelUnit unit) => EncounterLabels.Format(unit, units, item => item.Enemy, item => item.Name);
EqualText("Gargoyle 1", Label(gargoyleOne), "first same-name enemy");
EqualText("Gargoyle 2", Label(gargoyleTwo), "second same-name enemy ignores friendly duplicates");
EqualText("Demon 1", Label(demon), "numbering starts over for a different enemy name");
EqualText("Seelah", Label(player), "player character has no suffix");
EqualText("Mournful Shadow", Label(shadow), "player summon has no suffix");
EqualText("Gargoyle", Label(friendlyGargoyle), "friendly NPC has no suffix");
EqualText("Gargoyle 3", Label(new LabelUnit("Gargoyle", true)), "new enemy missing from collections");
EqualText("Gargoyle 1", EncounterLabels.Format(gargoyleTwo, new[] { gargoyleTwo },
    item => item.Enemy, item => item.Name), "renumbers when enemies are removed");

var targetLock = new TurnTargetLock<LabelUnit>();
targetLock.BeginTurn(null);
if (!targetLock.Allows(player) || !targetLock.TryCommit(player) ||
    !targetLock.Allows(player) || targetLock.Allows(shadow) || targetLock.TryCommit(shadow))
{
    throw new Exception("The first selected target must stay locked throughout approach and attack.");
}
if (targetLock.Allows(new LabelUnit(player.Name, player.Enemy)))
{
    throw new Exception("Same-name units must not bypass the reference-based target lock.");
}
// Hidden or unreachable targets do not grant a new choice mid-turn.
if (!ReferenceEquals(targetLock.Target, player) || targetLock.TryCommit(shadow))
{
    throw new Exception("An unavailable target must stay committed until the next turn.");
}
if (targetLock.Release(shadow) || !targetLock.Release(player) ||
    !targetLock.TryCommit(shadow) || !ReferenceEquals(targetLock.Target, shadow))
{
    throw new Exception("Only the actual downed target may release the lock for another choice.");
}
targetLock.BeginTurn(player);
if (targetLock.TryCommit(shadow) || !targetLock.TryCommit(player))
{
    throw new Exception("A failed turn-start reassessment must strictly retain the previous target.");
}
targetLock.BeginTurn(null);
if (!targetLock.TryCommit(shadow) || !ReferenceEquals(targetLock.Target, shadow))
{
    throw new Exception("The next turn's successful reassessment must release the old target.");
}
Equal(0, TargetingPolicy.ReleasesDownedTarget(false, false, false) ? 1 : 0,
    "conscious target retains the turn lock");
Equal(0, TargetingPolicy.ReleasesDownedTarget(true, false, true) ? 1 : 0,
    "sleep keeps its distinct cancellation rule and does not release the lock");
Equal(1, TargetingPolicy.ReleasesDownedTarget(true, false, false) ? 1 : 0,
    "unconscious target releases the turn lock");
Equal(1, TargetingPolicy.ReleasesDownedTarget(false, true, true) ? 1 : 0,
    "death releases the turn lock even if a sleep condition lingers");

EqualDecimal(0.5m, SquishPolicy.SaveFailureChance(9, 20), "ten failing save outcomes");
EqualDecimal(0.45m, SquishPolicy.SaveFailureChance(10, 20), "meeting save DC succeeds");
EqualDecimal(0.05m, SquishPolicy.SaveFailureChance(int.MaxValue, int.MinValue),
    "natural one still fails an otherwise certain ordinary save");
EqualDecimal(0.95m, SquishPolicy.SaveFailureChance(int.MinValue, int.MaxValue),
    "natural twenty still succeeds an otherwise impossible ordinary save");
EqualDecimal(1m, SquishPolicy.SaveFactor(9, 20, false), "50 percent failure is neutral");
EqualDecimal(1m, SquishPolicy.SaveFactor(9, 20, true), "half-save neutral normalization");
EqualDecimal(0.9m, SquishPolicy.SaveFactor(10, 20, false), "save-negates weight");
EqualDecimal(29m / 30m, SquishPolicy.SaveFactor(10, 20, true), "half damage is less save-sensitive");
for (var bonus = -30; bonus <= 30; bonus++)
{
    var ordinary = SquishPolicy.SaveFactor(bonus, 20, false);
    var half = SquishPolicy.SaveFactor(bonus, 20, true);
    if (SquishPolicy.SaveFactor(bonus + 1, 20, false) > ordinary ||
        Math.Abs(half - 1m) > Math.Abs(ordinary - 1m))
    {
        throw new Exception("Stronger saves must not improve Squish; half-save weights must be nearer neutral.");
    }
}
EqualDecimal(1m, SquishPolicy.IneffectiveFactor(100, 2), "Int 2 ignores ineffective damage");
EqualDecimal(1m, SquishPolicy.IneffectiveFactor(1, 5), "Int 5 needs repeated ineffective damage");
EqualDecimal(0.5m, SquishPolicy.IneffectiveFactor(2, 5), "Int 5 repeated evidence");
EqualDecimal(0.35m, SquishPolicy.IneffectiveFactor(1, 13), "Int 13 one ineffective event");
EqualDecimal(0.1m, SquishPolicy.IneffectiveFactor(1, 20), "Int 20 one ineffective event");
var evidence = new EffectivenessMemory();
evidence.Observe("bite:physical", 0, 10, 0, true);
Equal(1, evidence.Hits("bite:physical"), "immune bite is remembered");
Equal(0, evidence.Hits("claw:physical"), "bite does not teach claw immunity");
Equal(0, evidence.Hits("ray:fire"), "physical failure does not teach fire immunity");
evidence.Observe("ray:fire", 0, 10, 0, true);
evidence.Observe("claw:physical", 10, 10, 10, false);
Equal(1, evidence.Hits("bite:physical"), "claw damage does not clear bite evidence");
Equal(1, evidence.Hits("ray:fire"), "claw damage does not clear fire evidence");
evidence.Observe("bite:physical", 1, 10, 10, false);
Equal(0, evidence.Hits("bite:physical"), "effective bite clears only bite evidence");
Equal(1, evidence.Hits("ray:fire"), "successful bite leaves fire evidence intact");
EqualDecimal(0.55m, SquishPolicy.RoutineFactor(new[] { 0.1m, 1m }),
    "immune bite averages with useful claw instead of multiplying away routine");
EqualDecimal(0.7m, SquishPolicy.RoutineFactor(new[] { 0.1m, 1m, 1m }),
    "repeated useful attacks weight routine mean");
EqualDecimal(1m, SquishPolicy.RoutineFactor(Array.Empty<decimal>()), "empty routine is neutral");
EqualDecimal(grouped.Risk * 0.1m * 0.25m,
    grouped.WithDistance(0.25m).WithSquish(0.1m).Score,
    "single-attack replacement preserves Risk and Dist");
EqualDecimal(0m, new TargetScoreBreakdown(0m, 1m, 1m, 1m, 1m).WithSquish(2m).Score,
    "changing routine cannot revive rejected targets");
if (grouped.Describe("Str", "Caster", "Routine[test]", "").Contains(" * \n"))
{
    throw new Exception("Combined Squish terms must not leave a dangling multiplication sign.");
}

EqualDecimal(1m, RiskPolicy.AttackFactor(10), "AB 10 reference");
EqualDecimal(1.5m, RiskPolicy.AttackFactor(20), "AB 20 factor");
EqualDecimal(1m / 3m, RiskPolicy.AttackFactor(-10), "negative AB stays positive without a floor");
EqualDecimal(0m, RiskPolicy.DamageFactor(0m), "no weapon damage means no physical damage threat");
EqualDecimal(1m, RiskPolicy.DamageFactor(10m), "ten damage reference");
EqualDecimal(100m, RiskPolicy.DamageFactor(1000m), "damage has no upper cap");
EqualDecimal(2m, RiskPolicy.CastingSynergyFactor(20), "CL 20 linear melee synergy");
EqualDecimal(6m, RiskPolicy.CastingSynergyFactor(100), "melee synergy has no cap");
EqualDecimal(1m, RiskPolicy.CasterThreatFactor(0), "CL zero quadratic reference");
EqualDecimal(2.25m, RiskPolicy.CasterThreatFactor(7), "CL 7 calibrated standalone threat");
EqualDecimal(289m / 49m, RiskPolicy.CasterThreatFactor(20), "CL 20 quadratic threat");
EqualDecimal(3249m / 49m, RiskPolicy.CasterThreatFactor(100), "standalone caster threat has no cap");
EqualDecimal(1m, new RiskBreakdown(null, null, null, null, null).Risk,
    "fully unknown physical facts and caster are neutral");
var strengthFallback = TargetingPolicy.StrengthFactor(20);
var bothFallback = new RiskBreakdown(null, null, 20, false, null);
EqualDecimal(strengthFallback * strengthFallback, bothFallback.Risk,
    "unknown AB and damage independently use known Strength");
var attackOnly = new RiskBreakdown(20, null, 20, false, null);
EqualDecimal(1.5m * strengthFallback, attackOnly.Risk, "only damage falls back when AB is known");
var damageOnly = new RiskBreakdown(null, 20m, 20, false, null);
EqualDecimal(strengthFallback * 2m, damageOnly.Risk, "only AB falls back when damage is known");
EqualDecimal(3m, new RiskBreakdown(20, 20m, 5, false, 0).Risk,
    "known physical inputs ignore Strength and noncaster CL zero is neutral");
EqualDecimal(0.2m, new RiskBreakdown(0, 4m, null, false, null).Risk,
    "weak noncaster is not raised to a caster floor");
EqualDecimal(0m, new RiskBreakdown(0, 0m, 20, false, 0).Risk,
    "known zero damage is not replaced by Strength");
var weakMage = new RiskBreakdown(null, null, 5, true, 20);
var averageMage = new RiskBreakdown(null, null, 10, true, 20);
EqualDecimal(289m / 49m, weakMage.Risk, "Str 5 CL 20 mage keeps its quadratic caster threat");
EqualDecimal(weakMage.Risk, averageMage.Risk, "Str 5 and Str 10 mage threat agree");
EqualDecimal(289m / 49m, new RiskBreakdown(0, 0m, 5, true, 20).Risk,
    "no weapon attack still leaves recognized caster threat");
EqualDecimal(1.25m, new RiskBreakdown(0, 1m, 5, true, null).Risk,
    "recognized caster with unknown level uses recognition bonus standalone");
EqualDecimal(6m, new RiskBreakdown(20, 20m, null, true, 20).Risk,
    "strong physical caster retains gentler synergy instead of quadratic multiplication");
EqualDecimal(2.25m, new RiskBreakdown(0, 0m, 5, true, 7).Risk,
    "CL 7 mage with no useful melee matches the agreed calibration");
EqualDecimal(2.25m, new RiskBreakdown(null, null, 10, true, 7).Risk,
    "CL 7 standalone threat does not depend on weak Strength");
EqualDecimal(4.05m, new RiskBreakdown(20, 20m, null, true, 7).Risk,
    "CL 7 hybrid multiplies physical 3 by synergy 1.35, not caster threat 2.25");
foreach (var level in new[] { 0, 1, 5, 7, 10, 15, 20, 100, int.MaxValue })
{
    var value = new RiskBreakdown(15, 12m, 10, true, level);
    EqualDecimal(Math.Max(value.Attack * value.Damage * RiskPolicy.CastingSynergyFactor(level),
        RiskPolicy.CasterThreatFactor(level)), value.Risk,
        "recognized caster uses separate synergy and standalone threat in the maximum");
}
for (var level = 0; level < 100; level++)
{
    if (RiskPolicy.CasterThreatFactor(level + 1) <= RiskPolicy.CasterThreatFactor(level))
        throw new Exception("Standalone caster threat must strictly increase with CL.");
    if (RiskPolicy.CasterThreatFactor(level + 2) - RiskPolicy.CasterThreatFactor(level + 1) <=
        RiskPolicy.CasterThreatFactor(level + 1) - RiskPolicy.CasterThreatFactor(level))
        throw new Exception("Quadratic caster threat must accelerate with CL.");
}
foreach (var factor in new Func<int, decimal>[] { RiskPolicy.CastingSynergyFactor, RiskPolicy.CasterThreatFactor })
{
    try
    {
        factor(-1);
        throw new Exception("Negative caster levels must fail rather than clamp.");
    }
    catch (ArgumentOutOfRangeException) { }
}
for (var bonus = -100; bonus < 100; bonus++)
{
    if (RiskPolicy.AttackFactor(bonus) <= 0m ||
        RiskPolicy.AttackFactor(bonus + 1) <= RiskPolicy.AttackFactor(bonus))
        throw new Exception("AB factors must be positive and strictly increase with AB.");
}
EqualDecimal(7m, RiskPolicy.MeanDice(2, 6), "two ordinary sneak dice");
EqualDecimal(9m, RiskPolicy.MeanDice(2, 8), "Knife Master knife sneak dice");
EqualDecimal(5m, RiskPolicy.MeanDice(2, 4), "Knife Master nonknife sneak dice");
EqualDecimal(8.5m, RiskPolicy.MeanRawDamage(1, 6, 5), "ordinary weapon dice and bonus");
EqualDecimal(1m, RiskPolicy.MeanRawDamage(1, 4, -3), "very weak weapon minimum damage");
EqualDecimal(1.25m, RiskPolicy.MeanRawDamage(1, 4, -2), "minimum applies to outcomes, not their average");
EqualDecimal(1m, RiskPolicy.MeanRawDamage(0, 0, -2), "flat damage uses game minimum");
// Enumerate real outcomes independently to verify the exact low-tail average.
for (var bonus = -12; bonus <= 4; bonus++)
{
    var total = 0m;
    for (var first = 1; first <= 6; first++)
        for (var second = 1; second <= 6; second++)
            total += Math.Max(1, first + second + bonus);
    EqualDecimal(total / 36m, RiskPolicy.MeanRawDamage(2, 6, bonus), "2d6 minimum-damage mean");
}
Equal(0, TargetingPolicy.KnowledgeChance(5, 5, 18), "AB knowledge minimum");
Equal(1, TargetingPolicy.KnowledgeChance(18, 5, 18), "AB knowledge maximum");
Equal(0.5, TargetingPolicy.KnowledgeChance(15, 8, 22), "damage knowledge midpoint");
Equal(1, TargetingPolicy.KnowledgeChance(16, 5, 16), "recognition certain before CL");
Equal(0.4, TargetingPolicy.KnowledgeChance(16, 10, 25), "CL harder than caster recognition");
var riskMath = weakMage.Describe("Attack[test]", "Damage[test]", "CL[test]");
EqualText("max((Attack[test] * Damage[test] * CL[test]), CasterOnly[5.9])", riskMath,
    "Risk formula reports the standalone caster alternative");
var expanded = new TargetScoreBreakdown(0.8m, weakMage.Risk, 1m, 0.5m, 1m).WithDistance(0.25m);
EqualDecimal(weakMage.Risk / 8m, expanded.Score, "expanded Risk combines with Squish and Dist, never Base");
if (!expanded.Describe(riskMath, "", "AC[test]", "").StartsWith("Risk: 5.9 = max(") ||
    expanded.Describe(riskMath, "", "AC[test]", "").Contains(" * \n"))
    throw new Exception("Expanded Risk must remain one complete formula without dangling multiplication.");

EqualText("1.32", LogNumber.Format(1.324145455m), "round Risk to two display decimals");
EqualText("0.5", LogNumber.Format(0.500372685m), "remove unnecessary trailing zeros");
EqualText("0.66", LogNumber.Format(1.324145455m * 0.500372685m), "rounded combined score");
EqualText("0", LogNumber.Format(0m), "zero remains an actual zero");
EqualText("<0.01", LogNumber.Format(0.000001m), "tiny positive score is not displayed as rejected zero");
EqualText(">-0.01", LogNumber.Format(-0.000001m), "tiny negative values retain their sign");
var cultureBefore = System.Globalization.CultureInfo.CurrentCulture;
try
{
    System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
    EqualText("1.32", LogNumber.Format(1.324145455m), "numeric logs have invariant separators");
}
finally
{
    System.Globalization.CultureInfo.CurrentCulture = cultureBefore;
}
EqualDecimal(1.048m, TargetingPolicy.StrengthFactor(14), "formatting never rounds scoring inputs");
EqualDecimal(1.048m * 5m / 6m,
    new TargetScoreBreakdown(1m, 1.048m, 1m, 5m / 6m, 1m).Score,
    "combined scoring retains full precision after display changes");

EqualDecimal(1.75m, RiskPolicy.StatFactor(3.5m, false), "Shadow 1d6 Strength damage is meaningful Risk");
EqualDecimal(2.1875m, RiskPolicy.StatFactor(3.5m, true), "permanent stat drain has a separate lasting-danger weight");
EqualDecimal(2m, RiskPolicy.NegativeLevelFactor(2m), "negative levels share drain knowledge, not HP units");
EqualDecimal(1m, new RiskBreakdown(10, 10m, 10, false, 0).Risk,
    "unknown extras do not reveal precision or drain and add no second Strength fallback");
EqualDecimal(2.75m, new RiskBreakdown(10, 10m, 10, false, 0, RiskPolicy.StatFactor(3.5m, false)).Risk,
    "known stat damage adds to ordinary damage independently");
EqualDecimal(2.1m, new RiskBreakdown(10, 10m, 10, false, 0, RiskPolicy.DamageFactor(11m)).Risk,
    "separately learned sneak and elemental damage are additive, not multiplicative");
EqualDecimal(3m, new RiskBreakdown(10, null, 10, false, 0, 0m, 3).Risk,
    "known attack frequency scales the ordinary unknown-damage fallback only");
EqualDecimal(4m, new RiskBreakdown(10, null, 10, false, 0, 1m, 3).Risk,
    "already summed extras are not multiplied twice by frequency");
EqualDecimal(1.025m, RiskPolicy.CriticalFactor(1, 2, 10, 21), "20-only threat with 50% confirmation");
EqualDecimal(1.1425m, RiskPolicy.CriticalFactor(3, 2, 100, 10), "wide threat with natural-one confirmation failure");
EqualDecimal(1.0025m, RiskPolicy.CriticalFactor(1, 2, -100, 100), "natural-twenty confirmation remains possible");
EqualDecimal(1.025m, RiskPolicy.CriticalFactor(1, 2, null, 100),
    "unknown AB uses explicit 50% confirmation, not hidden candidate AB");
var critBase = 10m * RiskPolicy.CriticalFactor(1, 2, 10, 21);
EqualDecimal(1.725m, new RiskBreakdown(10, critBase, 10, false, 0, RiskPolicy.DamageFactor(7m)).Risk,
    "critical multiplier affects ordinary base, never independently added sneak dice");
EqualDecimal(20m, RiskPolicy.StatFactor(40m, false), "stat danger has no upper cap");
for (var bonus = -8; bonus <= 10; bonus++)
for (var reduction = 0; reduction <= 25; reduction++)
{
    var total = 0m;
    for (var first = 1; first <= 6; first++)
    for (var second = 1; second <= 6; second++)
        total += Math.Max(0, Math.Max(1, first + second + bonus) - reduction);
    EqualDecimal(total / 36m, RiskPolicy.MeanAfterReduction(2, 6, bonus, reduction),
        "flat DR evaluates outcomes, not only the mean or a score clamp");
}
EqualDecimal(0.5m, RiskPolicy.MeanAfterReduction(1, 6, 0, 4),
    "mean below DR is not total immunity: high damage rolls can penetrate");
evidence.Observe("shadow-touch", 0, 1, 0, true);
evidence.Observe("shadow-touch", 1, 1, 1, false);
Equal(0, evidence.Hits("shadow-touch"), "successful non-HP outcome clears this profile's ineffective evidence");
var plainLabels = "chosen Risk:\nRisk: 2 = Caster[2]\n  Damage: 1 = Hit[1]\n  Routine: 1 attack\n" +
    "Squish: 0.5 = AC[40]\nDist: 0.25 = Route[4]\nMath: 0.25 = Risk[2] * Squish[0.5] * Dist[0.25]";
var boldLabels = "chosen Risk:\n<b>Risk:</b> 2 = Caster[2]\n  <b>Damage:</b> 1 = Hit[1]\n  <b>Routine:</b> 1 attack\n" +
    "<b>Squish:</b> 0.5 = AC[40]\n<b>Dist:</b> 0.25 = Route[4]\n<b>Math:</b> 0.25 = Risk[2] * Squish[0.5] * Dist[0.25]";
EqualText(boldLabels, LogPresentation.InGame(plainLabels), "bold only line headings, not target names or inline factors");
EqualText(boldLabels, LogPresentation.InGame(boldLabels), "formatting is idempotent");
EqualText("already knowing Risk: 2; target Dist: Unknown", LogPresentation.InGame("already knowing Risk: 2; target Dist: Unknown"),
    "inline words remain plain");
EqualText("<b>Risk:</b> <0.01\r\n<b>Squish:</b> 1", LogPresentation.InGame("Risk: <0.01\r\nSquish: 1"),
    "small-number notation and Windows-delimited input survive formatting");
EqualText("", LogPresentation.InGame(""), "empty presentation is safe");
EqualText("  <b>Risk weight:</b> 4 = Raw[2] ^ Importance[2]", LogPresentation.InGame("  Risk weight: 4 = Raw[2] ^ Importance[2]"),
    "weighted calculation headings are bold too");
if (plainLabels.Contains("<b>")) throw new Exception("Formatting must not mutate the plain source message.");

var casterTuned = new RiskBreakdown(null, null, 5, true, 20, casterMultiplier: 2m);
EqualDecimal(3m, casterTuned.CastingSynergy, "caster multiplier scales synergy bonus without multiplying neutral one");
EqualDecimal(578m / 49m, casterTuned.CasterRisk, "caster priority scales its quadratic standalone alternative");
EqualDecimal(578m / 49m, casterTuned.Risk, "caster tuning makes weak high-CL mage more attractive");
EqualDecimal(1m, new RiskBreakdown(10, 10m, 10, false, 0, casterMultiplier: 5m).Risk,
    "noncasters are unaffected by caster weight");
EqualDecimal(1m, new RiskBreakdown(10, 10m, 10, true, 20, casterMultiplier: 0m).Risk,
    "zero caster weight removes casting, not physical danger");
EqualDecimal(289m / 49m, new RiskBreakdown(10, 10m, 10, true, 20, physicalMultiplier: 0m).Risk,
    "zero physical weight keeps recognized standalone casting");
EqualDecimal(0.5m, new RiskBreakdown(10, 10m, 10, false, 0, physicalMultiplier: 0.5m).Risk,
    "physical weight changes melee independently");
if (!casterTuned.Describe("AB[Unknown]", "Hit[Unknown]", "CL[20 => 3]").Contains("CasterWeight[2]"))
    throw new Exception("Caster tuning must be visible in the math log.");
EqualText("max((Attack[test] * Damage[test] * CastingSynergy[CL 7 => 1.35]), CasterOnly[CL 7 => 2.25])",
    new RiskBreakdown(0, 0m, 5, true, 7).Describe("Attack[test]", "Damage[test]",
        "CastingSynergy[CL 7 => 1.35]", "CL 7 => 2.25"),
    "log distinguishes linear synergy and quadratic standalone threat with their CL input");
EqualText("max((Attack[test] * Damage[test] * CastingSynergy[CL 7 => 1.7]), CasterOnly[CL 7 => 2.25 * CasterWeight[2] => 4.5])",
    new RiskBreakdown(0, 0m, 5, true, 7, casterMultiplier: 2m).Describe("Attack[test]", "Damage[test]",
        "CastingSynergy[CL 7 => 1.7]", "CL 7 => 2.25"),
    "weighted standalone log reports its unweighted source and weighted result separately");
EqualDecimal(4m, ScoringWeights.Importance(2m, 2m), "Risk importance emphasizes dangerous targets");
EqualDecimal(0.25m, ScoringWeights.Importance(0.5m, 2m), "Squish/Dist importance emphasizes unfavorable defenses/path");
EqualDecimal(1m, ScoringWeights.Importance(5m, 0m), "zero importance neutralizes nonzero preference");
EqualDecimal(0m, ScoringWeights.Importance(0m, 0m), "zero importance never revives unavailable damage/path");
EqualDecimal(0.5m, ScoringWeights.Importance(0.25m, 0.5m), "fractional importance softens a penalty");
var weights = new TargetScoreBreakdown(1m, 2m, 1m, 0.5m, 1m, 0.25m, 2m, 2m, 0.5m);
EqualDecimal(0.5m, weights.Score, "weighted Risk/Squish/Dist change their relative contributions");
var weightedSingle = weights.WithSquish(0.25m).WithDistance(0.5m);
EqualDecimal(4m, weightedSingle.Risk, "single-attack replacement doesn't exponentiate Risk twice");
EqualDecimal(0.0625m, weightedSingle.Squish, "single-attack replacement retains Squish importance");
EqualDecimal(0.5m, weights.Dist, "distance weight applied once");
if (!weights.Describe("test", "", "test", "", "test").Contains("Raw[2] ^ Importance[2]"))
    throw new Exception("Nondefault importance must explain raw versus weighted factors.");
EqualDecimal(0m, weights.WithDistance(0m).Score, "vanilla zero path remains unavailable with custom importance");
foreach (var invalid in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.MaxValue })
    if (ScoringWeights.IsValid(invalid)) throw new Exception("Invalid stored tuning weight accepted.");
EqualDecimal(0.1m, ScoringWeights.Read(0.1f), "float settings preserve intended decimal input without representation noise");
EqualDecimal(10m, ScoringWeights.Read(10f), "typing beyond normal slider range is not a scoring cap");
try { ScoringWeights.Importance(10m, 1000m); throw new Exception("Unrepresentable power must not clamp to a score."); }
catch (ArithmeticException) { }
foreach (var singleMove in new[] { false, true })
foreach (var pendingStandard in new[] { false, true })
    Equal(singleMove || pendingStandard ? 1 : 0,
        BodyBlockingPolicy.ReservesSingleMove(singleMove, pendingStandard) ? 1 : 0,
        "queued standard spell cannot spend its casting action crossing a friendly body");
foreach (var limited in new[] { false, true })
foreach (var matching in new[] { false, true })
foreach (var reachable in new[] { false, true })
    Equal(limited && matching && !reachable ? 1 : 0,
        BodyBlockingPolicy.CancelBlockedCast(limited, matching, reachable) ? 1 : 0,
        "only mod-limited matching unusable spell approaches cancel");
var castTarget = new BodyBlockingPolicy.Point(6, 0);
var occupiedCastEnd = new BodyBlockingPolicy.Point(4.5, 0);
var friendlyCastBody = new BodyBlockingPolicy.Body(occupiedCastEnd, 1, false, true);
var trimmedCastLength = BodyBlockingPolicy.RouteLimit(new BodyBlockingPolicy.Point(0, 0),
    new[] { occupiedCastEnd }, 4.5, new[] { friendlyCastBody }, true);
Equal(3.5, trimmedCastLength, "regression: body trimming shortens an in-range endpoint");
Equal(1, BodyBlockingPolicy.CancelBlockedCast(true, true,
    BodyBlockingPolicy.Length(new BodyBlockingPolicy.Point(trimmedCastLength, 0), castTarget) <= 1.6) ? 1 : 0,
    "regression: impossible queued buff is cancelled rather than left pending");
var flyoverBody = new BodyBlockingPolicy.Body(new BodyBlockingPolicy.Point(5, 0), 2, false, false);
if (!BodyBlockingPolicy.TryInsideInterval(new BodyBlockingPolicy.Point(0, 0),
        new BodyBlockingPolicy.Point(10, 0), flyoverBody, out var flyoverEnter, out var flyoverExit))
    throw new Exception("A route crossing a flying-passage footprint must produce a visible interval.");
Equal(0.3, flyoverEnter, "flying-passage visual starts at the footprint edge");
Equal(0.7, flyoverExit, "flying-passage visual ends at the footprint edge");
if (BodyBlockingPolicy.TryInsideInterval(new BodyBlockingPolicy.Point(0, 2),
        new BodyBlockingPolicy.Point(10, 2), flyoverBody, out _, out _))
    throw new Exception("Tangent contact must not create a flying-passage visual.");
Equal(1, BodyBlockingPolicy.FlyoverHeightFraction(new BodyBlockingPolicy.Point(5, 0),
    new[] { flyoverBody }), "the model lift peaks over the footprint center");
Equal(0, BodyBlockingPolicy.FlyoverHeightFraction(new BodyBlockingPolicy.Point(7, 0),
    new[] { flyoverBody }), "the model lift returns to ground at the footprint edge");
Console.WriteLine("All body-clearance, spell-approach, targeting, knowledge, Risk/Squish/Dist, bold headings, tuning weights, and target-lock tests passed.");

sealed record LabelUnit(string Name, bool Enemy);
