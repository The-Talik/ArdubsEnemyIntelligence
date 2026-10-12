using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.Designers.EventConditionActionSystem.Actions;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Items;
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.UnitLogic.Mechanics;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Mechanics.Components;

namespace ArdubsEnemyIntelligence;

// Capability reader only. Alternative branches use maximum potential, not their
// sum. Never execute actions/conditions or simulate poison, buffs, or control.
internal static class OnHitAssessment
{
    internal sealed class Effect
    {
        internal readonly string Key;
        internal readonly string Label;
        internal readonly decimal Mean;
        internal readonly StatType? Stat;
        internal readonly bool Drain;
        internal readonly bool NegativeLevels;
        internal readonly DamageTypeDescription? HpType;
        internal Effect(string key, string label, decimal mean, StatType? stat = null,
            bool drain = false, bool levels = false, DamageTypeDescription? hpType = null)
        { Key = key; Label = label; Mean = mean; Stat = stat; Drain = drain; NegativeLevels = levels; HpType = hpType; }
        internal decimal Factor => NegativeLevels ? RiskPolicy.NegativeLevelFactor(Mean) :
            Stat.HasValue ? RiskPolicy.StatFactor(Mean, Drain) : RiskPolicy.DamageFactor(Mean);
    }

    internal sealed class Potential
    {
        internal readonly List<Effect> Effects = new List<Effect>();
        internal readonly HashSet<string> Unmodeled = new HashSet<string>();
        internal bool Conditional;
        internal decimal Hp => Effects.Where(effect => effect.HpType != null).Sum(effect => effect.Mean);
        internal decimal StatFactor => Effects.Where(effect => effect.HpType == null).Sum(effect => effect.Factor);
        internal string Describe(bool hp) => string.Join(" + ", Effects.Where(effect => (effect.HpType != null) == hp)
            .Select(effect => $"{effect.Label} {LogNumber.Format(effect.Mean)}"));
        internal void Add(Potential other)
        { Effects.AddRange(other.Effects); Unmodeled.UnionWith(other.Unmodeled); Conditional |= other.Conditional; }
        internal static Potential Alternatives(Potential first, Potential second)
        {
            var result = new Potential { Conditional = true };
            foreach (var key in first.Effects.Concat(second.Effects).Select(effect => effect.Key).Distinct())
            {
                var a = first.Effects.Where(effect => effect.Key == key).ToArray();
                var b = second.Effects.Where(effect => effect.Key == key).ToArray();
                result.Effects.AddRange(a.Sum(effect => effect.Mean) >= b.Sum(effect => effect.Mean) ? a : b);
            }
            result.Unmodeled.UnionWith(first.Unmodeled); result.Unmodeled.UnionWith(second.Unmodeled);
            return result;
        }
    }

    internal static IEnumerable<EntityFact> Facts(UnitEntityData unit) =>
        unit.Facts.List.Where(fact => fact.IsActive && fact.IsTurnedOn && !fact.IsDisposed);

    internal static Potential Read(UnitEntityData unit, ItemEntityWeapon weapon)
    {
        var result = new Potential();
        var facts = Facts(unit).Concat(weapon.Enchantments.Cast<EntityFact>()).Distinct();
        foreach (var fact in facts)
        {
            if (fact.BlueprintComponents.OfType<WeaponEnergyBurst>().Any()) result.Unmodeled.Add("critical-only energy burst");
            foreach (var trigger in fact.BlueprintComponents.OfType<AbstractWeaponTrigger>())
            {
                if (trigger.ActionsOnInitiator || !Matches(trigger, weapon)) continue;
                if (trigger.OnMiss || trigger.OnCharge || trigger.OnAttackOfOpportunity || trigger.CriticalHit ||
                    trigger.OnlyNatural20 || trigger.OnlySneakAttack || trigger.ReduceHPToZero ||
                    trigger.DamageMoreTargetMaxHP || trigger.OnlyOnFirstAttack || trigger.OnlyOnFirstHit ||
                    trigger.CheckDistance || trigger.DuelistWeapon || trigger.CheckPhysicalDamageForm ||
                    trigger.OnlyOnFullAttack || trigger.NotCriticalHit || trigger.NotSneakAttack || trigger.NotExtraAttack)
                { result.Unmodeled.Add("conditional weapon trigger"); continue; }
                result.Add(ReadActions(trigger.Action, fact.MaybeContext, new HashSet<BlueprintScriptableObject>(), 0));
            }
        }
        return result;
    }

    private static bool Matches(AbstractWeaponTrigger trigger, ItemEntityWeapon weapon) =>
        (!trigger.CheckWeaponBlueprint || trigger.WeaponBlueprint == weapon.Blueprint) &&
        (trigger.WeaponType == null || trigger.WeaponType == weapon.Blueprint.Type) &&
        (!trigger.CheckWeaponCategory || trigger.Category == weapon.Blueprint.Category) &&
        (!trigger.CheckWeaponGroup || weapon.Blueprint.FighterGroup.Contains(trigger.Group)) &&
        (!trigger.CheckWeaponRangeType || Kingmaker.Enums.WeaponRangeTypeHelper.IsSuitableWeapon(trigger.RangeType, weapon)) &&
        (!trigger.AllNaturalAndUnarmed || weapon.Blueprint.IsNatural || weapon.Blueprint.IsUnarmed);

    private static Potential ReadActions(ActionList? actions, MechanicsContext? context,
        HashSet<BlueprintScriptableObject> visited, int depth)
    {
        if (depth > 16) throw new InvalidOperationException("On-hit blueprint recursion exceeds diagnostic budget.");
        var result = new Potential();
        foreach (var action in actions?.Actions ?? Array.Empty<GameAction>())
        {
            if (action is ContextActionDealDamage damage)
            {
                if (damage.ReadPreRolledFromSharedValue || damage.UseWeaponDamageModifiers ||
                    !TryValue(damage.Value.DiceCountValue, context, out var dice) ||
                    !TryValue(damage.Value.BonusValue, context, out var bonus))
                { result.Unmodeled.Add("execution-dependent on-hit damage"); continue; }
                var mean = RiskPolicy.MeanDice(dice, (int)damage.Value.DiceType, bonus);
                if (mean < 0m) throw new InvalidOperationException("Negative on-hit damage estimate.");
                if (damage.Half || damage.HalfIfSaved) result.Conditional = true;
                if (damage.Half) mean /= 2m;
                if (damage.IsAbilityDamage)
                    result.Effects.Add(new Effect("stat:" + damage.AbilityType + ":" + damage.Drain,
                        damage.AbilityType + (damage.Drain ? " drain" : " damage"), mean, damage.AbilityType, damage.Drain));
                else if (damage.IsEnergyDrain)
                    result.Effects.Add(new Effect("levels", "negative levels", mean, levels: true));
                else if (damage.IsSimpleDamage)
                    result.Effects.Add(new Effect("hp:" + damage.DamageType.Type + ":" + damage.DamageType.Energy,
                        damage.DamageType.Type == DamageType.Energy ? damage.DamageType.Energy.ToString() : damage.DamageType.Type.ToString(),
                        mean, hpType: damage.DamageType));
            }
            else if (action is Conditional conditional)
                result.Add(Potential.Alternatives(ReadActions(conditional.IfTrue, context, visited, depth + 1),
                    ReadActions(conditional.IfFalse, context, visited, depth + 1)));
            else if (action is ContextActionConditionalSaved saved)
                result.Add(Potential.Alternatives(ReadActions(saved.Succeed, context, visited, depth + 1),
                    ReadActions(saved.Failed, context, visited, depth + 1)));
            else if (action is ContextActionSavingThrow save)
            { result.Add(ReadActions(save.Actions, context, visited, depth + 1)); result.Conditional = true; }
            else if (action is ContextActionApplyBuff buff && !buff.ToCaster && buff.Buff != null)
            {
                if (!visited.Add(buff.Buff)) throw new InvalidOperationException("Cycle in on-hit buff effects.");
                var activation = buff.Buff.ComponentsArray.OfType<AddFactContextActions>().ToArray();
                foreach (var component in activation)
                {
                    // An applied buff has its own ranks, not its parent's ranks.
                    // Literal activation damage is safe; unresolved ranks stay unknown.
                    result.Add(ReadActions(component.Activated, null, visited, depth + 1));
                    if (component.NewRound?.Actions?.Length > 0) result.Unmodeled.Add("ongoing buff damage/control");
                }
                if (activation.Length == 0) result.Unmodeled.Add("buff/control effect");
                visited.Remove(buff.Buff);
            }
            else if (!(action is ContextActionSpawnFx) && !(action is ContextActionRemoveSelf))
                result.Unmodeled.Add(action.GetType().Name);
        }
        return result;
    }

    private static bool TryValue(ContextValue value, MechanicsContext? context, out int result)
    {
        result = 0;
        if (value.ValueType == ContextValueType.Simple) { result = value.Value; return true; }
        if (value.ValueType == ContextValueType.Rank && context != null)
        { result = context[value.ValueRank]; return true; }
        return false;
    }
}
