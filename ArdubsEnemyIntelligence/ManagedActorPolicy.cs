using System.Collections.Generic;
using System.Linq;

namespace ArdubsEnemyIntelligence;

internal static class ManagedActorPolicy
{
    // The native Friends faction is non-neutral and non-hostile, but its
    // IsAlly behaviour explicitly excludes the player-controlled faction.
    // Absence of hostility alone is not enough for a Neutral-faction NPC.
    internal static bool IsFriendlyCombatant(bool hostileToParty, bool alliedToParty,
        bool partyAlliedToUnit, bool neutralFaction, bool alwaysEnemy, bool enemyForEveryone) =>
        !hostileToParty && (alliedToParty || partyAlliedToUnit ||
            !neutralFaction && !alwaysEnemy && !enemyForEveryone);

    internal static bool IsAlliedNpc(bool hostileToParty, bool friendlySelf, IEnumerable<bool> friendlyGroupMembers) =>
        !hostileToParty && (friendlySelf || friendlyGroupMembers.Any(friendly => friendly));

    internal static bool Allows(bool inCombat, bool turnBased, bool directlyControllable,
        bool inPlayerParty, bool playerFaction, bool hostileToParty, bool alliedToParty,
        bool pet, bool summoned, bool applyToAlliedNpcs, bool applyToEnemies = true)
    {
        if (!inCombat || !turnBased || directlyControllable || inPlayerParty) return false;
        // Preserve enemy pets/summons. The allied option must not take over
        // party assets. The caller resolves friendly combat-group membership;
        // merely neutral, unrelated combatants still do not count as allies.
        if (hostileToParty) return applyToEnemies && !playerFaction;
        return applyToAlliedNpcs && alliedToParty && !pet && !summoned;
    }
}
