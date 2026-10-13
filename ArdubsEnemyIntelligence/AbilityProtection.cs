using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Blueprints.Facts;
using Kingmaker.Designers.EventConditionActionSystem.Actions;
using Kingmaker.ElementsSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Enums;
using Kingmaker.Enums.Damage;
using Kingmaker.RuleSystem.Rules.Damage;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Abilities.Components.Base;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Parts;

namespace ArdubsEnemyIntelligence;

// Read effect types, not damage amounts. Never cast a spell, evaluate its
// conditions, roll damage or execute a buff while assessing a candidate.
// Only reject complete immunity; mixed/unmodeled effects remain selectable.
internal static class AbilityProtection
{
    internal static DamageProtection.Result Evaluate(UnitEntityData target, AbilityData ability)
    {
        var blueprint = ability.Blueprint.StickyTouch?.TouchDeliveryAbility ?? ability.Blueprint;
        var caster = ability.Caster.Unit;
        var spellResistance = target.Get<UnitPartSpellResistance>();
        // Read native immunity entries, including alignment and caster bypasses.
        // Supply descriptors explicitly: the blueprint-only IsImmune overload
        // does not carry them through every native descriptor-immunity check.
        if (spellResistance?.Immunities.Any(immunity => spellResistance.CanApply(immunity,
                ability, caster, ability.SpellDescriptor, blueprint)) == true)
            return new DamageProtection.Result(0m,
                $"Protection[spell immunity; descriptors {ability.SpellDescriptor}; native immunity check => 0]");
        var components = OnHitAssessment.Facts(target).SelectMany(fact => fact.BlueprintComponents).ToArray();
        var assessment = new Assessment(components, energy =>
        {
            var reduction = target.Get<UnitPartDamageReduction>();
            reduction?.TryInitialize();
            return reduction?.IsImmune(energy) == true;
        }, buff => IsBuffImmune(buff, components, ability.SpellDescriptor,
            caster.Descriptor.Alignment.ValueRaw, fact => caster.HasFact(fact),
            caster.State.Features.MythicReduceResistances));
        foreach (var effect in blueprint.ComponentsArray.OfType<AbilityApplyEffect>())
        {
            if (effect is AbilityEffectRunAction run) assessment.Read(run.Actions, 0);
            else assessment.Unmodeled.Add(effect.GetType().Name);
        }
        return assessment.Result();
    }

    internal static DamageProtection.Result EvaluateEffects(ActionList? actions,
        BlueprintComponent[] components, Func<DamageEnergyType, bool>? energyImmunity = null,
        Func<BlueprintBuff, bool>? buffImmunity = null)
    {
        var assessment = new Assessment(components, energyImmunity ?? (_ => false), buffImmunity ?? (_ => false));
        assessment.Read(actions, 0);
        return assessment.Result();
    }

    // Match the native buff-immunity conditions without creating a cast context
    // or firing RuleCanApplyBuff. Control immunity can block just one effect of
    // a mixed damage/control ability, so it must not reject the whole spell.
    internal static bool IsBuffImmune(BlueprintBuff buff, BlueprintComponent[] components,
        SpellDescriptor sourceDescriptors, Alignment? casterAlignment,
        Func<BlueprintUnitFact, bool> casterHasFact, bool mythicReduceResistances)
    {
        foreach (var immunity in components.OfType<SpecificBuffImmunity>())
        {
            if (immunity.Buff != buff) continue;
            var alignmentMatches = casterAlignment.HasValue
                ? casterAlignment.Value.HasComponent(immunity.Alignment) : immunity.Alignment == AlignmentComponent.None;
            if (!alignmentMatches) continue;
            if (immunity.m_CheckCasterFact &&
                (immunity.m_FactToCheck?.Get() is not { } fact || !casterHasFact(fact))) continue;
            return true;
        }
        if (mythicReduceResistances) return false;
        var descriptors = sourceDescriptors | buff.SpellDescriptor;
        return components.OfType<BuffDescriptorImmunity>().Any(immunity =>
            immunity.Descriptor.HasAnyFlag(descriptors) &&
            (immunity.IgnoreFeature == null || !casterHasFact(immunity.IgnoreFeature)) &&
            (!immunity.CheckFact || immunity.FactToCheck != null && casterHasFact(immunity.FactToCheck)));
    }

    internal static bool HasHitPointDamage(ActionList? actions, int depth = 0)
    {
        if (depth > 16) throw new InvalidOperationException("Spell HP blueprint recursion exceeds diagnostic budget.");
        return (actions?.Actions ?? Array.Empty<GameAction>()).Any(action => action switch
        {
            ContextActionDealDamage damage => damage.IsSimpleDamage,
            Conditional conditional => HasHitPointDamage(conditional.IfTrue, depth + 1) ||
                HasHitPointDamage(conditional.IfFalse, depth + 1),
            ContextActionConditionalSaved saved => HasHitPointDamage(saved.Succeed, depth + 1) ||
                HasHitPointDamage(saved.Failed, depth + 1),
            ContextActionSavingThrow save => HasHitPointDamage(save.Actions, depth + 1),
            _ => false
        });
    }

    private sealed class Assessment
    {
        private readonly BlueprintComponent[] components;
        private readonly Func<DamageEnergyType, bool> energyImmunity;
        private readonly Func<BlueprintBuff, bool> buffImmunity;
        private readonly List<string> channels = new List<string>();
        internal readonly HashSet<string> Unmodeled = new HashSet<string>();
        private bool allBlocked = true;

        internal Assessment(BlueprintComponent[] components, Func<DamageEnergyType, bool> energyImmunity,
            Func<BlueprintBuff, bool> buffImmunity)
        {
            this.components = components;
            this.energyImmunity = energyImmunity;
            this.buffImmunity = buffImmunity;
        }

        private void Add(string label, bool immune)
        {
            channels.Add($"{label}; immune {(immune ? "yes" : "no")}");
            allBlocked &= immune;
        }

        internal void Read(ActionList? actions, int depth)
        {
            if (depth > 16) throw new InvalidOperationException("Spell protection blueprint recursion exceeds diagnostic budget.");
            foreach (var action in actions?.Actions ?? Array.Empty<GameAction>())
            {
                if (action is ContextActionDealDamage damage)
                {
                    if (damage.IsEnergyDrain)
                        Add("negative levels", components.OfType<AddImmunityToEnergyDrain>().Any());
                    else if (damage.IsAbilityDamage)
                        Add(damage.AbilityType + (damage.Drain ? " drain" : " damage"),
                            components.OfType<AddImmunityToAbilityScoreDamage>().Any(component =>
                                !component.UseIgnoreRule && (!damage.Drain || component.Drain) &&
                                (component.StatTypes == null || component.StatTypes.Length == 0 ||
                                 component.StatTypes.Contains(damage.AbilityType))));
                    else if (damage.IsSimpleDamage && damage.DamageType != null)
                        Add(damage.DamageType.Type == DamageType.Energy ? damage.DamageType.Energy.ToString() :
                            damage.DamageType.Type.ToString(), damage.DamageType.Type == DamageType.Energy &&
                            energyImmunity(damage.DamageType.Energy));
                    else Unmodeled.Add("unsupported damage channel");
                }
                else if (action is Conditional conditional)
                {
                    Read(conditional.IfTrue, depth + 1);
                    Read(conditional.IfFalse, depth + 1);
                }
                else if (action is ContextActionConditionalSaved saved)
                {
                    Read(saved.Succeed, depth + 1);
                    Read(saved.Failed, depth + 1);
                }
                else if (action is ContextActionSavingThrow save) Read(save.Actions, depth + 1);
                else if (action is ContextActionApplyBuff buff && !buff.ToCaster && buff.Buff != null)
                    Add($"buff {buff.Buff.name}", buffImmunity(buff.Buff));
                else if (!(action is ContextActionSpawnFx) && !(action is ContextActionRemoveSelf))
                    Unmodeled.Add(action.GetType().Name);
            }
        }

        internal DamageProtection.Result Result()
        {
            var blocked = channels.Count > 0 && allBlocked && Unmodeled.Count == 0;
            var details = channels.Count > 0 ? string.Join("; ", channels) : "no supported spell damage channel";
            if (Unmodeled.Count > 0) details += "; unmodeled " + string.Join(", ", Unmodeled.OrderBy(value => value));
            return new DamageProtection.Result(blocked ? 0m : 1m,
                $"Protection[spell; {details}; {(blocked ? "all supported effects blocked => 0" : "no total-immunity claim => 1")}]");
        }
    }
}
