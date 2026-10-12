using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Enums;
using Kingmaker.Items;
using Kingmaker.Items.Slots;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Parts;

namespace ArdubsEnemyIntelligence;

internal enum ThreatKnowledge { Sneak, ExtraDamage, StatDrain, Routine }

internal static class OffensiveAssessment
{
    internal sealed class Hit
    {
        internal string Weapon = "";
        internal ItemEntityWeapon Item = null!;
        internal int AttackBonus;
        internal int CriticalRange;
        internal int CriticalMultiplier;
        internal decimal BaseDamage;
        internal int SneakDice;
        internal int SneakSides;
        internal decimal SneakDamage;
        internal decimal ExtraDamage;
        internal BaseDamage[] Damage = Array.Empty<BaseDamage>();
        internal OnHitAssessment.Potential Effects = new OnHitAssessment.Potential();
        internal decimal ExtraMean => ExtraDamage + Effects.Hp;
        internal decimal StatFactor => Effects.StatFactor;
    }

    internal sealed class Snapshot
    {
        internal readonly Hit[] Hits;
        internal Hit First => Hits[0];
        internal string Weapon => First.Weapon;
        internal int AttackBonus => First.AttackBonus;
        internal decimal BaseDamage => First.BaseDamage;
        // No base+sneak property: ordinary damage knowledge reveals no precision.
        internal Snapshot(Hit[] hits) { Hits = hits; }
    }

    internal static Snapshot Read(UnitEntityData unit)
    {
        var hits = new List<Hit>();
        foreach (var info in UnitAttack.EnumerateFullAttack(unit))
        {
            var weapon = info.Hand?.Weapon;
            if (weapon == null) continue;
            var limb = ReferenceEquals(info.Hand, unit.Body.PrimaryHand) ? LimbType.PrimaryHand :
                ReferenceEquals(info.Hand, unit.Body.SecondaryHand) ? LimbType.SecondaryHand : LimbType.AdditionalLimb;
            // Character-sheet stat calculators only, no attacks or damage rolls.
            var stats = Rulebook.Trigger(new RuleCalculateWeaponStats(unit, weapon, limb)
                { DisableBattleLogSelf = true });
            var attack = Rulebook.Trigger(new RuleCalculateAttackBonusWithoutTarget(unit, stats,
                info.AttackBonusPenalty) { DisableBattleLogSelf = true });
            var damage = stats.DamageDescription.Select(description => description.CreateDamage()).ToArray();
            foreach (var entry in damage) if (entry.Precision) entry.ResetBonus();
            var ordinary = damage.FirstOrDefault(entry => !entry.Precision);
            var sneakDice = unit.Get<UnitPartDisableBonusForDamage>()?.DisableSneak == true
                ? 0 : unit.Stats.SneakAttack.ModifiedValue;
            var sneakSides = unit.State.Features.KnifeMaster
                ? weapon.Blueprint.Category.HasSubCategory(WeaponSubCategory.Knives) ? 8 : 4 : 6;
            hits.Add(new Hit
            {
                Weapon = weapon.Name, Item = weapon, AttackBonus = attack.Result,
                CriticalRange = stats.CriticalRange, CriticalMultiplier = stats.CriticalMultiplier,
                BaseDamage = ordinary == null ? 0m : Mean(ordinary), Damage = damage,
                SneakDice = sneakDice, SneakSides = sneakSides,
                SneakDamage = (sneakDice > 0 ? RiskPolicy.MeanDice(sneakDice, sneakSides) : 0m) +
                    damage.Where(entry => entry.Precision).Sum(Mean),
                ExtraDamage = damage.Where(entry => !entry.Precision && !ReferenceEquals(entry, ordinary)).Sum(Mean),
                Effects = OnHitAssessment.Read(unit, weapon)
            });
        }
        if (hits.Count == 0) hits.Add(new Hit { Weapon = "no normal weapon attack", CriticalRange = 1, CriticalMultiplier = 1 });
        return new Snapshot(hits.ToArray());
    }

    internal static decimal Mean(BaseDamage damage)
    {
        var dice = damage.Dice.ModifiedValue;
        return RiskPolicy.MeanRawDamage(dice.Rolls, (int)dice.Dice, damage.TotalBonus);
    }

    internal static int HighestCasterLevel(UnitEntityData unit) =>
        unit.Spellbooks.Select(book => book.EffectiveCasterLevel).DefaultIfEmpty(0).Max();
}
