using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Enums.Damage;
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Parts;

namespace ArdubsEnemyIntelligence;

// This belongs in Squish: a dangerous opponent can still be a useless target
// for this particular attack. Read known defenses only; never deal test damage.
internal static class DamageProtection
{
    internal readonly struct Result
    {
        internal readonly decimal Factor;
        internal readonly string Description;
        internal Result(decimal factor, string description) { Factor = factor; Description = description; }
    }
    internal static Result Unavailable => new Result(1m, "Protection[preview unavailable => 1]");

    internal static Result Evaluate(UnitEntityData target, OffensiveAssessment.Hit hit)
    {
        var components = OnHitAssessment.Facts(target).SelectMany(fact => fact.BlueprintComponents).ToArray();
        var precisionImmune = components.OfType<AddImmunityToPrecisionDamage>().Any();
        var incorporeal = components.OfType<AddIncorporealDamageDivisor>().Any();
        var reduction = target.Get<UnitPartDamageReduction>();
        reduction?.TryInitialize();
        var raw = 0m;
        var useful = 0m;
        var details = new List<string>();
        foreach (var damage in hit.Damage)
        {
            var before = OffensiveAssessment.Mean(damage);
            var after = damage.Precision && precisionImmune ? 0m : Remaining(damage, before);
            raw += before / 10m; useful += after / 10m;
            var label = damage is EnergyDamage energy ? energy.EnergyType.ToString() : damage.Type.ToString();
            details.Add($"{label} {LogNumber.Format(before)} => {LogNumber.Format(after)}");
        }
        var sneak = hit.SneakDice > 0 ? RiskPolicy.MeanDice(hit.SneakDice, hit.SneakSides) : 0m;
        if (sneak > 0m)
        {
            // Sneak shares the ordinary weapon's damage type and bypass facts.
            var ordinary = hit.Damage.FirstOrDefault(damage => !damage.Precision);
            var fraction = ordinary == null ? 1m : Remaining(ordinary, OffensiveAssessment.Mean(ordinary)) /
                OffensiveAssessment.Mean(ordinary);
            raw += sneak / 10m; useful += precisionImmune ? 0m : sneak / 10m * fraction;
            details.Add($"sneak {LogNumber.Format(sneak)} => {LogNumber.Format(precisionImmune ? 0m : sneak * fraction)} potential");
        }
        foreach (var effect in hit.Effects.Effects)
        {
            var immune = effect.NegativeLevels ? components.OfType<AddImmunityToEnergyDrain>().Any() :
                effect.Stat.HasValue && components.OfType<AddImmunityToAbilityScoreDamage>().Any(component =>
                    !component.UseIgnoreRule && (!effect.Drain || component.Drain) &&
                    (component.StatTypes == null || component.StatTypes.Length == 0 || component.StatTypes.Contains(effect.Stat.Value)));
            var remaining = immune ? 0m : effect.Factor;
            if (effect.HpType != null)
            {
                var damage = new DamageDescription { TypeDescription = effect.HpType }.CreateDamage();
                remaining = Remaining(damage, effect.Mean, false) / 10m;
            }
            raw += effect.Factor; useful += remaining;
            details.Add($"{effect.Label} weight {LogNumber.Format(effect.Factor)} => {LogNumber.Format(remaining)}");
        }
        if (raw == 0m) return new Result(1m, "Protection[no supported damage channel => 1]");
        if (useful == 0m && hit.Effects.Unmodeled.Count > 0)
            return new Result(1m, "Protection[supported channels blocked; unmodeled effects; no total-immunity claim => 1]");
        return new Result(useful / raw,
            $"Protection[{string.Join("; ", details)}; useful/raw weight {LogNumber.Format(useful)}/{LogNumber.Format(raw)} => " +
            $"{LogNumber.Format(useful / raw)}]");

        decimal Remaining(BaseDamage damage, decimal mean, bool hasDice = true)
        {
            if (!damage.IgnoreImmunities && damage is EnergyDamage energy && reduction?.IsImmune(energy.EnergyType) == true) return 0m;
            var divisor = 1m;
            if (incorporeal && damage.Reality != DamageRealityType.Ghost)
            {
                if (damage is PhysicalDamage physical)
                {
                    if (physical.EnchantmentTotal <= 0 && !damage.IgnoreImmunities) return 0m;
                    divisor = 0.5m;
                }
                else if (damage is EnergyDamage energyDamage &&
                    energyDamage.EnergyType != DamageEnergyType.Holy && energyDamage.EnergyType != DamageEnergyType.Unholy &&
                    energyDamage.EnergyType != DamageEnergyType.Divine && energyDamage.EnergyType != DamageEnergyType.NegativeEnergy &&
                    energyDamage.EnergyType != DamageEnergyType.PositiveEnergy) divisor = 0.5m;
            }
            var dr = damage.IgnoreReduction || reduction == null ? 0 :
                reduction.m_Chunks.Where(chunk => !chunk.Bypassed(damage, hit.Item))
                    .Select(chunk => chunk.Reduction).DefaultIfEmpty(0).Max();
            if (dr == 0) return mean * divisor;
            if (!hasDice) return mean / (1m + dr / (mean + 1m)) * divisor;
            var dice = damage.Dice.ModifiedValue;
            return RiskPolicy.MeanAfterReduction(dice.Rolls, (int)dice.Dice, damage.TotalBonus, dr) * divisor;
        }
    }
}
