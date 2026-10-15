using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UI.MVVM._VM.Tooltip.Bricks;
using Kingmaker.UI.MVVM._VM.Tooltip.Templates;
using Owlcat.Runtime.UI.Tooltips;
using TurnBased.Controllers;

namespace ArdubsEnemyIntelligence;

internal static class KnowledgeInspector
{
    internal static bool ShouldShow(TooltipTemplateType type) =>
        (type == TooltipTemplateType.Tooltip || type == TooltipTemplateType.Info) && ArdubsEnemyIntelligenceMain.Settings != null &&
        ArdubsEnemyIntelligenceMain.Settings.ShowKnowledgeInHoverInspector && ArdubsEnemyIntelligenceMain.Settings.ModEnabled;

    internal static IEnumerable<ITooltipBrick> Append(IEnumerable<ITooltipBrick> original, string knowledge)
    {
        // Keep native/QOL bricks intact. Only replace the result once our complete
        // section is ready, so errors cannot leave a partially modified inspector.
        var bricks = original.ToList();
        bricks.Add(new TooltipBrickSeparator());
        bricks.Add(new TooltipBrickTitle("AI knowledge (developer)", TooltipTitleType.H3));
        bricks.Add(new TooltipBrickText(knowledge));
        return bricks;
    }

    [HarmonyPatch(typeof(TooltipTemplateUnitInspect), nameof(TooltipTemplateUnitInspect.GetBody))]
    private static class UnitInspectorPatch
    {
        [HarmonyPostfix]
        private static void Postfix(TooltipTemplateUnitInspect __instance, TooltipTemplateType type,
            ref IEnumerable<ITooltipBrick> __result)
        {
            if (!ShouldShow(type)) return;
            try
            {
                if (__instance.Unit == null || !CombatController.IsInTurnBasedCombat() || Game.Instance?.State == null)
                    return;
                var units = Game.Instance.State.Units.Concat(Game.Instance.State.ExtraUnits)
                    .Where(unit => unit != null && unit.IsInCombat);
                __result = Append(__result, TargetingState.InspectKnowledge(__instance.Unit, units));
            }
            catch (Exception exception)
            {
                ArdubsEnemyIntelligenceMain.ModEntry.Logger.Error("Could not show AI knowledge in unit inspector: " + exception);
            }
        }
    }
}
