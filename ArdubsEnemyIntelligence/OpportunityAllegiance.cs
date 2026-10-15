using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Kingmaker.Controllers.Combat;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using TurnBased.Controllers;

namespace ArdubsEnemyIntelligence;

// Native engagement and reaction commands can outlive a faction change.
// Always consult current hostility, including the native simulation used by Dist.
internal static class OpportunityAllegiance
{
    internal static bool IsHostile(UnitEntityData? attacker, UnitEntityData? target) =>
        attacker != null && target != null && attacker.IsEnemy(target);

    private static bool Enabled => ArdubsEnemyIntelligenceMain.Settings.ModEnabled &&
        CombatController.IsInTurnBasedCombat();

    internal static bool KnowsOpportunityTarget(UnitGroupMemory targetMemory, UnitEntityData attacker,
        UnitEntityData target)
    {
        try
        {
            // Native reaction eligibility asks whether the provoking unit knows
            // the attacker. Threat awareness belongs to the attacker instead.
            // Do not add either unit to memory or bypass any other native gate.
            if (ArdubsEnemyIntelligenceMain.Settings.FixAttackOfOpportunityAwareness && Enabled)
                return attacker.Memory.Contains(target);
        }
        catch (Exception exception)
        {
            ArdubsEnemyIntelligenceMain.ModEntry.Logger.Error("Attack-of-opportunity awareness: " + exception);
        }
        return targetMemory.Contains(attacker);
    }

    private static void Report(UnitEntityData attacker, UnitEntityData target, string stage) =>
        TargetingLog.Write($"Canceled allied attack of opportunity: {attacker.CharacterName} -> " +
            $"{target.CharacterName}; {stage}; current hostility false (old engagement does not grant an attack)");

    [HarmonyPatch(typeof(UnitEngagementExtension), nameof(UnitEngagementExtension.IsEngage))]
    private static class Engagement
    {
        [HarmonyPostfix]
        private static void Postfix(UnitEntityData __0, UnitEntityData __1, ref bool __result)
        {
            if (!__result || !Enabled) return;
            try { __result = IsHostile(__0, __1); }
            catch (Exception exception)
            {
                ArdubsEnemyIntelligenceMain.ModEntry.Logger.Error("Engagement allegiance: " + exception);
            }
        }
    }

    [HarmonyPatch(typeof(UnitCombatState), nameof(UnitCombatState.AttackOfOpportunity))]
    private static class Reaction
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var contains = AccessTools.Method(typeof(UnitGroupMemory), nameof(UnitGroupMemory.Contains),
                new[] { typeof(UnitEntityData) });
            var replacement = AccessTools.Method(typeof(OpportunityAllegiance), nameof(KnowsOpportunityTarget));
            var count = 0;
            foreach (var code in instructions)
            {
                if (code.Calls(contains))
                {
                    count++;
                    var target = new CodeInstruction(OpCodes.Ldarg_1);
                    target.labels.AddRange(code.labels);
                    target.blocks.AddRange(code.blocks);
                    code.labels.Clear();
                    code.blocks.Clear();
                    yield return target;
                    code.opcode = OpCodes.Call;
                    code.operand = replacement;
                }
                yield return code;
            }
            if (count != 1)
                throw new InvalidOperationException("Cannot identify the game's opportunity-attack awareness check.");
        }

        [HarmonyPrefix]
        private static bool Prefix(UnitCombatState __instance, UnitEntityData __0, bool __2,
            ref bool __result)
        {
            if (!Enabled) return true;
            try
            {
                if (IsHostile(__instance.Unit, __0)) return true;
                __result = false;
                // Simulations must not emit messages or spend a real reaction.
                if (!__2 && __instance.Unit != null && __0 != null)
                    Report(__instance.Unit, __0, "reaction request");
                return false;
            }
            catch (Exception exception)
            {
                ArdubsEnemyIntelligenceMain.ModEntry.Logger.Error("Attack-of-opportunity allegiance: " + exception);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(UnitAttackOfOpportunity), "OnAction")]
    private static class QueuedReaction
    {
        [HarmonyPrefix]
        private static bool Prefix(UnitAttackOfOpportunity __instance, ref UnitCommand.ResultType __result)
        {
            if (!Enabled) return true;
            try
            {
                if (IsHostile(__instance.Executor, __instance.Target)) return true;
                __result = UnitCommand.ResultType.Success;
                if (__instance.Executor != null && __instance.Target != null)
                    Report(__instance.Executor, __instance.Target, "queued reaction; allegiance changed before attack");
                return false;
            }
            catch (Exception exception)
            {
                ArdubsEnemyIntelligenceMain.ModEntry.Logger.Error("Queued attack-of-opportunity allegiance: " + exception);
                return true;
            }
        }
    }
}
