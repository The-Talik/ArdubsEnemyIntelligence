using System;
using System.Linq;
using Kingmaker.UI.Models.Log;
using Kingmaker.UI.Models.Log.CombatLog_ThreadSystem;
using Kingmaker.UI.Models.Log.CombatLog_ThreadSystem.LogThreads.Common;
using UnityEngine;

namespace ArdubsEnemyIntelligence;

internal static class TargetingLog
{
    private static bool combatLogFailureReported;

    internal static void WriteLearning(string message)
    {
        if (ArdubsEnemyIntelligenceMain.Settings.ShowLearningInLog)
        {
            Write(message);
        }
    }

    internal static void Write(string message)
    {
        var start = PerformanceDiagnostics.Start();
        try { WriteCore(message); }
        finally { PerformanceDiagnostics.End(PerformanceDiagnostics.Stage.CombatLogWrite, start); }
    }

    private static void WriteCore(string message)
    {
        if (!ArdubsEnemyIntelligenceMain.Settings.AnyLogging)
        {
            return;
        }

        if (ArdubsEnemyIntelligenceMain.Settings.UmmLogging)
        {
            ArdubsEnemyIntelligenceMain.ModEntry.Logger.Log(message);
        }

        if (!ArdubsEnemyIntelligenceMain.Settings.InGameLogging)
        {
            return;
        }

        try
        {
            var thread = LogThreadService.Instance?
                .GetThreadsByChannelType(new[] { LogChannelType.Common })
                .OfType<MessageLogThread>()
                .FirstOrDefault();

            if (thread == null)
            {
                ReportCombatLogFailure("The in-game combat log is unavailable; targeting entries could not be displayed there.");
                return;
            }

            using (GameLogContext.Scope)
            {
                thread.AddMessage(new CombatLogMessage(ArdubsEnemyIntelligenceMain.DisplayName + ": " + LogPresentation.InGame(message),
                    Color.white, PrefixIcon.RightArrow));
            }
        }
        catch (Exception exception)
        {
            ReportCombatLogFailure("Could not write to the in-game combat log: " + exception);
        }
    }

    private static void ReportCombatLogFailure(string message)
    {
        if (combatLogFailureReported)
        {
            return;
        }

        combatLogFailureReported = true;
        ArdubsEnemyIntelligenceMain.ModEntry.Logger.Error(message);
    }
}
