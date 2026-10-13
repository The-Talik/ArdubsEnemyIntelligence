using System;

namespace ArdubsEnemyIntelligence;

// Pure scoring math. Dist discounts lost attacks and waiting turns, not target stats.
internal readonly struct DistancePlan
{
    internal int WaitTurns { get; }
    internal int AttacksNow { get; }
    internal int FirstAttacks { get; }
    internal decimal AttackLoss { get; }
    internal decimal AttackFactor { get; }
    internal decimal TurnFactor => 1m / (1m + WaitTurns);
    internal decimal RouteFactor { get; }
    internal decimal OpportunityFactor { get; }
    internal decimal Factor => AttackFactor * TurnFactor * RouteFactor * OpportunityFactor;

    internal DistancePlan(decimal meters, decimal moveAndAttack, decimal totalMove,
        decimal futureMoveAndAttack, decimal futureTotalMove, int fullAttacks,
        bool canAttackNow, bool canFullAttack, bool canFullAttackLater, bool canFiveFootStep, decimal stepRange,
        int opportunities)
    {
        if (meters < 0m || moveAndAttack < 0m || totalMove < 0m ||
            futureMoveAndAttack < 0m || futureTotalMove < 0m || stepRange < 0m ||
            fullAttacks < 1 || opportunities < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(meters), "Invalid distance scoring inputs.");
        }
        if (meters > 0m && futureTotalMove == 0m)
        {
            throw new ArgumentException("A moving route requires positive movement speed.");
        }

        var full = canAttackNow && canFullAttack &&
            (meters == 0m || canFiveFootStep && meters <= stepRange);
        if (full)
        {
            WaitTurns = 0;
            FirstAttacks = fullAttacks;
        }
        else if (canAttackNow && meters <= moveAndAttack)
        {
            WaitTurns = 0;
            FirstAttacks = 1;
        }
        else
        {
            // This turn can use both actions to approach. Subsequent turns can
            // move twice until the remaining route fits move + standard attack.
            var afterThisTurn = meters > totalMove ? meters - totalMove : 0m;
            var extra = afterThisTurn > futureMoveAndAttack
                ? checked((int)decimal.Ceiling((afterThisTurn - futureMoveAndAttack) / futureTotalMove))
                : 0;
            WaitTurns = checked(1 + extra);
            FirstAttacks = afterThisTurn <= extra * futureTotalMove && canFullAttackLater ? fullAttacks : 1;
        }
        AttacksNow = WaitTurns == 0 ? FirstAttacks : 0;
        AttackLoss = 1m - (decimal)FirstAttacks / fullAttacks;
        // Combining with TurnFactor yields 1 / (1 + wait + lost-attack fraction).
        // This avoids valuing a full attack next turn above a single attack now,
        // and does not require a score floor or exponential discount/underflow.
        AttackFactor = (1m + WaitTurns) / (1m + WaitTurns + AttackLoss);
        // A mild within-turn tiebreaker, in addition to the discrete action cost.
        RouteFactor = meters == 0m ? 1m : 1m / (1m + 0.5m * meters / futureTotalMove);
        OpportunityFactor = 1m / (1m + 0.25m * opportunities);
    }

    internal string Describe(decimal meters, decimal moveRange, decimal totalRange,
        decimal futureTotalRange, int fullAttacks, int opportunities, string opportunityDetails, bool fiveFootStep)
    {
        return $"Attacks[{N(AttackFactor)}] * Turns[wait {WaitTurns} => {N(TurnFactor)}] * " +
            $"Route[{N(RouteFactor)}] * AoO[{N(OpportunityFactor)}]\n" +
            $"  Attacks[now {AttacksNow}; first attacking turn {FirstAttacks}/{fullAttacks}; " +
            $"lost fraction {N(AttackLoss)}]\n" +
            $"  Route[{N(meters)}m; move + attack {N(moveRange)}m; total move {N(totalRange)}m; " +
            $"full-turn budget {N(futureTotalRange)}m; 5ft step {(fiveFootStep ? "yes" : "no")}]\n" +
            $"  AoO[count {opportunities}; {opportunityDetails}]";
    }

    private static string N(decimal value) => LogNumber.Format(value);
}

// Offensive abilities spend their action differently from weapon attacks.
// Prefer a cast that can happen now; keep distant targets as discounted
// fallbacks when the actor has no useful immediate target.
internal readonly struct AbilityDistancePlan
{
    internal int WaitTurns { get; }
    internal bool FiveFootStep { get; }
    internal decimal TimingFactor => 1m / (1m + 9m * WaitTurns);
    internal decimal RouteFactor { get; }
    internal decimal Factor => TimingFactor * RouteFactor;

    internal AbilityDistancePlan(decimal meters, decimal moveAndAct, decimal totalMove,
        decimal futureMoveAndAct, decimal futureTotalMove, bool actionAvailableNow, decimal stepRange = 0m)
    {
        if (meters < 0m || moveAndAct < 0m || totalMove < 0m ||
            futureMoveAndAct < 0m || futureTotalMove < 0m || stepRange < 0m ||
            meters > 0m && futureTotalMove == 0m)
            throw new ArgumentOutOfRangeException(nameof(meters), "Invalid ability-distance inputs.");

        if (actionAvailableNow && meters <= moveAndAct)
            WaitTurns = 0;
        else
        {
            var afterThisTurn = meters > totalMove ? meters - totalMove : 0m;
            var extra = afterThisTurn > futureMoveAndAct
                ? checked((int)decimal.Ceiling((afterThisTurn - futureMoveAndAct) / futureTotalMove))
                : 0;
            WaitTurns = checked(1 + extra);
        }
        // Carry the movement mode used by the timing estimate into the selected
        // command. An ordinary move would consume a full-round cast's action.
        FiveFootStep = WaitTurns == 0 && meters > 0m && meters <= stepRange;
        RouteFactor = meters == 0m ? 1m :
            1m / (1m + 0.5m * meters / futureTotalMove);
    }
}
