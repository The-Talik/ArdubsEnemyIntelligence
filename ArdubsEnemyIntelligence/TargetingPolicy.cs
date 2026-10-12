using System;

namespace ArdubsEnemyIntelligence;

internal static class TargetingPolicy
{
    internal static decimal EligibleTargetScore(decimal score, bool eligible) => eligible ? score : 0m;

    // Sleep keeps its separate command-cancellation rule. A target actually
    // knocked out or killed is the one exception to the per-turn target lock.
    internal static bool ReleasesDownedTarget(bool unconscious, bool dead, bool sleeping) =>
        dead || unconscious && !sleeping;

    internal static double ReassessmentChance(int intelligence, int zeroAt, int certainAt)
    {
        return KnowledgeChance(intelligence, zeroAt, certainAt);
    }

    internal static double KnowledgeChance(int intelligence, int zeroAt, int certainAt)
    {
        if (certainAt <= zeroAt)
        {
            throw new ArgumentOutOfRangeException(nameof(certainAt));
        }

        if (intelligence <= zeroAt) return 0;
        if (intelligence >= certainAt) return 1;
        return ((double)intelligence - zeroAt) / ((double)certainAt - zeroAt);
    }

    internal static decimal StrengthFactor(int strength)
    {
        return 1m + (strength - 10m) * 0.012m;
    }

    internal static decimal ArmorFactor(int armorClass)
    {
        // A continuous AC comparison that is defined even at zero or negative AC.
        var difference = 20m - armorClass;
        return 1m + difference / (20m + Math.Abs(difference));
    }

    internal static int ObserveWeaponDamage(int currentHits, bool observedWeaponHit,
        int finalDamage, int rawDamage, int damageBeforeReduction, bool immune = false)
    {
        if (!observedWeaponHit)
        {
            return currentHits;
        }

        if (finalDamage > 0)
        {
            return 0;
        }

        // DamageWithoutReduction is already zero for immunity. Raw damage and
        // explicit immunity evidence also cover that earlier defensive stage.
        return finalDamage == 0 && (rawDamage > 0 || damageBeforeReduction > 0 || immune)
            ? checked(currentHits + 1) : currentHits;
    }
}
