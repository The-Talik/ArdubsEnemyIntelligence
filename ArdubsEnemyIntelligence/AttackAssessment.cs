using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.AI;
using Kingmaker.AI.Blueprints;
using Kingmaker.Blueprints.Items.Weapons;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.RuleSystem;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Mechanics.Actions;

namespace ArdubsEnemyIntelligence;

// Read delivery metadata, never infer a touch attack merely from spell range.
// No preview attacks, saves, damage, or casts are executed here.
internal sealed class AttackAssessment
{
    internal readonly string Name;
    internal readonly string EvidenceKey;
    internal readonly DefenseKind? ArmorDefense;
    internal readonly DefenseKind? SaveDefense;
    internal readonly bool HalfOnSave;
    internal readonly AbilityData? Ability;
    internal readonly ItemEntityWeapon? WeaponItem;

    private AttackAssessment(string name, string key, DefenseKind? armor,
        DefenseKind? save = null, bool halfOnSave = false, AbilityData? ability = null,
        ItemEntityWeapon? weapon = null)
    {
        Name = name;
        EvidenceKey = key;
        ArmorDefense = armor;
        SaveDefense = save;
        HalfOnSave = halfOnSave;
        Ability = ability;
        WeaponItem = weapon;
    }

    internal static bool IsScoredAction(AiAction action, DecisionContext context) =>
        action.Blueprint is BlueprintAiAttack || IsSupportedAbility(context.Ability);

    private static bool IsSupportedAbility(AbilityData? ability)
    {
        var blueprint = ability?.Blueprint;
        return blueprint != null && blueprint.CanTargetEnemies && !blueprint.NotOffensive &&
               !blueprint.CanTargetPoint && blueprint.AoERadius.Meters <= 0f &&
               !blueprint.IsAoEDamage && ability!.AbilityTargetsAround == null;
    }

    internal static AttackAssessment[] Resolve(AiAction action, DecisionContext context)
    {
        var ability = context.Ability;
        if (action.Blueprint is BlueprintAiAttack)
        {
            ability = context.Unit.Brain.GetAvailableAutoUseAbility();
            if (ability == null)
            {
                // Enumerate the game's normal attack routine, including offhand
                // and natural limbs. Repeated attacks weight the mean below.
                return UnitAttack.EnumerateFullAttack(context.Unit)
                    .Where(attack => attack.Hand?.MaybeWeapon != null)
                    .Select(attack => Weapon(attack.Hand.MaybeWeapon!)).ToArray();
            }
        }
        return ability == null ? Array.Empty<AttackAssessment>() : new[] { Spell(ability) };
    }

    internal static AttackAssessment Weapon(ItemEntityWeapon weapon) =>
        new AttackAssessment(weapon.Name + " (" + weapon.Blueprint.AttackType + ")",
            WeaponKey(weapon), ArmorKind(weapon.Blueprint), weapon: weapon);

    internal static string WeaponKey(ItemEntityWeapon weapon) =>
        "weapon:" + weapon.Blueprint.AssetGuid + ":" + weapon.Blueprint.AttackType + ":" +
        string.Join(",", weapon.Enchantments.Select(enchantment => enchantment.Blueprint.AssetGuid.ToString())
            .OrderBy(value => value, StringComparer.Ordinal));

    internal static string SpellKey(BlueprintAbility blueprint) =>
        "ability:" + (blueprint.StickyTouch?.TouchDeliveryAbility ?? blueprint).AssetGuid;

    internal static AttackAssessment Spell(AbilityData ability)
    {
        var blueprint = ability.Blueprint.StickyTouch?.TouchDeliveryAbility ?? ability.Blueprint;
        var touch = blueprint.ComponentsArray.OfType<AbilityDeliverTouch>().FirstOrDefault();
        var projectile = blueprint.ComponentsArray.OfType<AbilityDeliverProjectile>().FirstOrDefault();
        DefenseKind? armor = touch?.TouchWeapon != null ? ArmorKind(touch.TouchWeapon) :
            projectile?.NeedAttackRoll == true && projectile.Weapon != null ? ArmorKind(projectile.Weapon) : null;
        var runner = blueprint.ComponentsArray.OfType<AbilityEffectRunAction>().FirstOrDefault();
        var save = runner?.SavingThrowType.ToString();
        DefenseKind? saveKind = save == "Fortitude" ? DefenseKind.Fortitude :
            save == "Reflex" ? DefenseKind.Reflex : save == "Will" ? DefenseKind.Will : (DefenseKind?)null;
        var actions = runner?.Actions.Actions;
        // Do not guess the semantics of arbitrary nested conditions/effects.
        var half = actions != null && actions.Length > 0 &&
            actions.All(value => value is ContextActionDealDamage damage && damage.HalfIfSaved);
        return new AttackAssessment(ability.Name, SpellKey(ability.Blueprint), armor, saveKind, half, ability);
    }

    private static DefenseKind ArmorKind(BlueprintItemWeapon weapon) =>
        weapon.AttackType == AttackType.Touch || weapon.AttackType == AttackType.RangedTouch
            ? DefenseKind.TouchArmorClass : DefenseKind.ArmorClass;

    internal static string Label(DefenseKind kind) => kind == DefenseKind.ArmorClass ? "AC" :
        kind == DefenseKind.TouchArmorClass ? "Touch AC" : kind.ToString();

    internal static int ReadDefense(UnitEntityData target, DefenseKind kind) => kind switch
    {
        DefenseKind.ArmorClass => target.Stats.AC.ModifiedValue,
        DefenseKind.TouchArmorClass => target.Stats.AC.Touch,
        DefenseKind.Fortitude => target.Stats.SaveFortitude.ModifiedValue,
        DefenseKind.Reflex => target.Stats.SaveReflex.ModifiedValue,
        DefenseKind.Will => target.Stats.SaveWill.ModifiedValue,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
