using System;

namespace ArdubsEnemyIntelligence;

internal readonly struct RiskBreakdown
{
    internal decimal Attack { get; }
    internal decimal Damage { get; }
    internal decimal CastingSynergy { get; }
    internal decimal Physical => Attack * Damage * PhysicalMultiplier;
    internal decimal CasterRisk { get; }
    internal decimal PhysicalMultiplier { get; }
    internal decimal CasterMultiplier { get; }
    internal decimal CasterThreat { get; }
    internal decimal Risk => Math.Max(Physical * CastingSynergy, CasterRisk);

    internal RiskBreakdown(int? attackBonus, decimal? meanDamage, int? strength,
        bool? isCaster, int? casterLevel, decimal additionalDamage = 0m, decimal fallbackRoutineWeight = 1m,
        decimal physicalMultiplier = 1m, decimal casterMultiplier = 1m)
    {
        var fallback = strength.HasValue ? TargetingPolicy.StrengthFactor(strength.Value) : 1m;
        Attack = attackBonus.HasValue ? RiskPolicy.AttackFactor(attackBonus.Value) : fallback;
        if (additionalDamage < 0m || fallbackRoutineWeight < 1m || physicalMultiplier < 0m || casterMultiplier < 0m)
            throw new ArgumentOutOfRangeException(nameof(additionalDamage));
        Damage = (meanDamage.HasValue ? RiskPolicy.DamageFactor(meanDamage.Value) :
            fallback * fallbackRoutineWeight) + additionalDamage;
        PhysicalMultiplier = physicalMultiplier;
        CasterMultiplier = casterMultiplier;
        var synergy = casterLevel.HasValue ? RiskPolicy.CastingSynergyFactor(casterLevel.Value) :
            isCaster == true ? 1.25m : 1m;
        // Zero caster weight removes casting influence, not the caster's
        // physical danger. Noncasters never receive a caster multiplier.
        CastingSynergy = 1m + (synergy - 1m) * casterMultiplier;
        // This floor represents recognized spellcasting, not a floor on physical
        // danger. Weak noncasters can still have Risk below 1.
        // Standalone spell danger grows quadratically; do not multiply melee
        // by that whole curve or overvalue strong melee/caster hybrids.
        CasterThreat = isCaster == true
            ? casterLevel.HasValue ? RiskPolicy.CasterThreatFactor(casterLevel.Value) : 1.25m
            : 0m;
        CasterRisk = CasterThreat * casterMultiplier;
    }

    internal string Describe(string attackTerm, string damageTerm, string casterTerm,
        string? casterThreatTerm = null) =>
        $"max(({attackTerm} * {damageTerm}" +
        (PhysicalMultiplier == 1m ? "" : $" * PhysicalWeight[{LogNumber.Format(PhysicalMultiplier)}]") +
        $" * {casterTerm}), " +
        "CasterOnly[" + (casterThreatTerm ?? LogNumber.Format(CasterThreat)) +
        (CasterMultiplier == 1m ? "" :
            $" * CasterWeight[{LogNumber.Format(CasterMultiplier)}] => {LogNumber.Format(CasterRisk)}") + "])";
}

internal static class RiskPolicy
{
    // Capability weights, not HP predictions: five HP-equivalent per stat point,
    // 25% extra lasting danger for drain, ten HP-equivalent per negative level.
    internal static decimal StatFactor(decimal mean, bool drain) =>
        DamageFactor(mean * 5m) * (drain ? 1.25m : 1m);

    internal static decimal NegativeLevelFactor(decimal mean) => DamageFactor(mean * 10m);

    internal static decimal CriticalFactor(int threatFaces, int multiplier, int? bonus, int armor)
    {
        if (threatFaces < 1 || threatFaces > 20 || multiplier < 1)
            throw new ArgumentOutOfRangeException(nameof(threatFaces));
        if (!bonus.HasValue) return 1m + threatFaces / 20m * 0.5m * (multiplier - 1m);
        var confirmations = 0;
        for (var roll = 1; roll <= 20; roll++)
            if (roll == 20 || roll != 1 && (long)roll + bonus.Value >= armor) confirmations++;
        return 1m + threatFaces / 20m * (confirmations / 20m) * (multiplier - 1m);
    }

    internal static decimal MeanAfterReduction(int rolls, int sides, int bonus, int reduction)
    {
        if (reduction < 0) throw new ArgumentOutOfRangeException(nameof(reduction));
        return reduction == 0 ? MeanRawDamage(rolls, sides, bonus) :
            MeanRawDamage(rolls, sides, checked(bonus - reduction + 1)) - 1m;
    }
    internal static decimal AttackFactor(int bonus)
    {
        var difference = bonus - 10m;
        return 1m + difference / (10m + Math.Abs(difference));
    }

    internal static decimal DamageFactor(decimal meanDamage)
    {
        if (meanDamage < 0m) throw new ArgumentOutOfRangeException(nameof(meanDamage));
        return meanDamage / 10m;
    }

    internal static decimal CastingSynergyFactor(int casterLevel)
    {
        if (casterLevel < 0) throw new ArgumentOutOfRangeException(nameof(casterLevel));
        return 1m + casterLevel / 20m;
    }

    internal static decimal CasterThreatFactor(int casterLevel)
    {
        if (casterLevel < 0) throw new ArgumentOutOfRangeException(nameof(casterLevel));
        var levelFactor = 1m + casterLevel / 14m;
        return levelFactor * levelFactor;
    }

    internal static decimal MeanDice(int rolls, int sides, int bonus = 0)
    {
        if (rolls < 0 || sides < 0 || rolls > 0 && sides == 0)
            throw new ArgumentOutOfRangeException(nameof(rolls));
        return rolls * ((sides + 1m) / 2m) + bonus;
    }

    // The game's minimum raw damage is 1, before target reductions. This is
    // a combat rule, not a scoring clamp. Compute E[max(1, dice + bonus)], not
    // max(1, E[dice + bonus]), which is inaccurate for weak weapon attacks.
    internal static decimal MeanRawDamage(int rolls, int sides, int bonus)
    {
        var mean = MeanDice(rolls, sides, bonus);
        var minimum = (long)rolls + bonus;
        var maximum = (long)rolls * sides + bonus;
        if (minimum >= 1) return mean;
        if (maximum <= 1) return 1m;
        var deficit = checked((int)(1 - minimum));
        // Only the low tail can be affected by minimum damage. A diagnostic
        // budget rejects an abnormal modded distribution instead of guessing.
        if (deficit > 4096 || (long)rolls * deficit * sides > 1000000)
            throw new InvalidOperationException("Risk damage distribution exceeds the calculation budget.");
        var probabilities = new decimal[deficit];
        probabilities[0] = 1m;
        for (var roll = 0; roll < rolls; roll++)
        {
            var next = new decimal[deficit];
            for (var sum = 0; sum < deficit; sum++)
            {
                if (probabilities[sum] == 0m) continue;
                for (var extra = 0; extra < sides && sum + extra < deficit; extra++)
                    next[sum + extra] += probabilities[sum] / sides;
            }
            probabilities = next;
        }
        var correction = 0m;
        for (var sum = 0; sum < deficit; sum++)
            correction += probabilities[sum] * (deficit - sum);
        return mean + correction;
    }
}
