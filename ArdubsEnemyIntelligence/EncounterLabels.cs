using System;
using System.Collections.Generic;

namespace ArdubsEnemyIntelligence;

internal static class EncounterLabels
{
    internal static string Format<T>(T unit, IEnumerable<T> encounterUnits,
        Func<T, bool> isEnemy, Func<T, string> nameOf) where T : class
    {
        var name = nameOf(unit);
        if (!isEnemy(unit))
        {
            return name;
        }

        var ordinal = 0;
        foreach (var candidate in encounterUnits)
        {
            if (!isEnemy(candidate) || !string.Equals(nameOf(candidate), name, StringComparison.Ordinal))
            {
                continue;
            }

            ordinal++;
            if (ReferenceEquals(candidate, unit))
            {
                return $"{name} {ordinal}";
            }
        }

        // A newly spawned unit may not yet appear in the encounter collections.
        return $"{name} {ordinal + 1}";
    }
}
