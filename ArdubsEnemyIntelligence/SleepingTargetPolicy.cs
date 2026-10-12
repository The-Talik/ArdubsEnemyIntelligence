namespace ArdubsEnemyIntelligence;

internal static class SleepingTargetPolicy
{
    // Deliberately no distance, visibility or action-availability input: a
    // blocked awake opponent must not make a sleeper a fallback target.
    internal static bool IsActiveOpponent(bool inCombat, bool hostile, bool conscious,
        bool sleeping, bool incapacitated) =>
        inCombat && hostile && conscious && !sleeping && !incapacitated;

    // Allied NPCs never select a sleeper, independent of the enemy-only option
    // and of whether any other opponent remains. Callers enforce actor scope.
    internal static bool Allows(bool enabled, bool sleeping, bool hasActiveOpponent, bool alliedNpc = false) =>
        !sleeping || !alliedNpc && (!enabled || !hasActiveOpponent);
}
