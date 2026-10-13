using System;

namespace ArdubsEnemyIntelligence;

[Flags]
internal enum VisibleDefenseCondition
{
    None = 0,
    Paralyzed = 1,
    Helpless = 2,
    Stunned = 4,
    Prone = 8,
    Petrified = 16
}

internal static class ConditionDefensePolicy
{
    internal static bool UsesFlatFooted(VisibleDefenseCondition conditions) =>
        (conditions & (VisibleDefenseCondition.Paralyzed | VisibleDefenseCondition.Helpless |
                       VisibleDefenseCondition.Stunned | VisibleDefenseCondition.Petrified)) != 0;

    // Match the native RuleCalculateAC's condition adjustments without firing
    // rulebook events, rolling attacks or changing any character statistics.
    internal static int ArmorAdjustment(int normalArmor, int flatFootedArmor,
        VisibleDefenseCondition conditions, bool melee)
    {
        var adjustment = UsesFlatFooted(conditions) ? flatFootedArmor - normalArmor : 0;
        if ((conditions & VisibleDefenseCondition.Prone) != 0) adjustment += melee ? -4 : 4;
        if (melee && (conditions & VisibleDefenseCondition.Helpless) != 0) adjustment -= 4;
        return adjustment;
    }

    internal static int AssessedArmor(bool known, int rememberedArmor, int adjustment) =>
        checked((known ? rememberedArmor : 20) + adjustment);
}

internal sealed class ConditionRecognitionMemory
{
    internal VisibleDefenseCondition Conditions { get; private set; }
    internal bool Known { get; private set; }
    private bool rolled;
    private bool rerollUsed;

    internal bool ShouldRoll(VisibleDefenseCondition conditions, bool afterAttack)
    {
        if (conditions != Conditions)
        {
            Conditions = conditions;
            Known = false;
            rolled = false;
            rerollUsed = false;
        }
        if (conditions == VisibleDefenseCondition.None || Known ||
            rolled && (!afterAttack || rerollUsed)) return false;
        rolled = true;
        rerollUsed = afterAttack;
        return true;
    }

    internal void Record(bool recognized) => Known = recognized;
}
