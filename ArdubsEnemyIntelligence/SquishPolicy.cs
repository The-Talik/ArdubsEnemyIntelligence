using System;
using System.Collections.Generic;
using System.Linq;

namespace ArdubsEnemyIntelligence;

internal enum DefenseKind
{
    ArmorClass,
    TouchArmorClass,
    Fortitude,
    Reflex,
    Will
}

internal static class SquishPolicy
{
    // A relative-health preference, not an absolute HP estimate of kill time.
    // Full HP is neutral; approaching zero smoothly approaches 2. The abs term
    // also handles conscious-at-negative-HP features without a clamp or pole.
    internal static decimal HitPointsFactor(int remaining, int maximum)
    {
        if (maximum <= 0) throw new ArgumentOutOfRangeException(nameof(maximum), "Maximum HP must be positive.");
        return 1m + (maximum - (decimal)remaining) / (maximum + Math.Abs((decimal)remaining));
    }

    // Enumerate the die's outcomes, rather than clipping a calculated percentage.
    // These are ordinary saves: natural 1 fails and natural 20 succeeds.
    internal static decimal SaveFailureChance(int bonus, int difficultyClass)
    {
        var failures = 0;
        for (var die = 1; die <= 20; die++)
        {
            if (die == 1 || die != 20 && (long)die + bonus < difficultyClass)
            {
                failures++;
            }
        }
        return failures / 20m;
    }

    internal static decimal SaveFactor(int bonus, int difficultyClass, bool halfOnSave)
    {
        var failure = SaveFailureChance(bonus, difficultyClass);
        // Normalize against an unknown save's 50% failure estimate. Simple
        // half-on-save damage is less sensitive to saving-throw strength.
        return halfOnSave ? (failure + (1m - failure) * 0.5m) / 0.75m : failure / 0.5m;
    }

    internal static decimal IneffectiveFactor(int hits, int intelligence) =>
        hits > 0 && intelligence >= 10 ? (intelligence >= 20 ? 0.10m : 0.35m) :
        hits >= 2 && intelligence >= 5 ? 0.50m : 1m;

    internal static decimal RoutineFactor(IReadOnlyList<decimal> contributions)
    {
        if (contributions.Count == 0)
        {
            return 1m;
        }
        var sum = 0m;
        foreach (var contribution in contributions)
        {
            sum += contribution;
        }
        return sum / contributions.Count;
    }
}

internal sealed class EffectivenessMemory
{
    private readonly Dictionary<string, int> hits = new Dictionary<string, int>(StringComparer.Ordinal);

    internal int Hits(string profile) => hits.TryGetValue(profile, out var count) ? count : 0;

    internal KeyValuePair<string, int>[] Snapshot() => hits.ToArray();

    internal int Observe(string profile, int finalDamage, int rawDamage,
        int beforeReduction, bool immune)
    {
        var result = TargetingPolicy.ObserveWeaponDamage(Hits(profile), true,
            finalDamage, rawDamage, beforeReduction, immune);
        hits[profile] = result;
        return result;
    }
}
