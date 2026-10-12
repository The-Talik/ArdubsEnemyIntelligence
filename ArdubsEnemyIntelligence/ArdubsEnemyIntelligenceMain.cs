using System;
using System.Reflection;
using HarmonyLib;
using UnityModManagerNet;

namespace ArdubsEnemyIntelligence;

public static class ArdubsEnemyIntelligenceMain
{
    internal const string DisplayName = "Ardub's Enemy Intelligence";
    internal static UnityModManager.ModEntry ModEntry = null!;
    internal static Settings Settings = null!;

    public static bool Load(UnityModManager.ModEntry modEntry)
    {
        ModEntry = modEntry;
        Harmony? harmony = null;
        try
        {
            Settings = UnityModManager.ModSettings.Load<Settings>(modEntry) ?? new Settings();
            Settings.MigrateLegacyLogging();
            Settings.ValidateRanges(modEntry);
            modEntry.OnGUI = _ => Settings.Draw();
            modEntry.OnSaveGUI = entry => Settings.Save(entry);
            modEntry.OnUpdate = (_, deltaTime) =>
            {
                var start = PerformanceDiagnostics.Start();
                try { FlyingPassageVisual.Update(deltaTime); }
                finally
                {
                    PerformanceDiagnostics.End(PerformanceDiagnostics.Stage.FlyingVisual, start);
                    PerformanceDiagnostics.Frame(UnityEngine.Time.unscaledDeltaTime);
                    PerformanceDiagnostics.ReportIfDue();
                }
            };
            harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            modEntry.Logger.Log(DisplayName + " loaded (turn-based attack targeting; enemies " +
                (Settings.ApplyToEnemies ? "enabled" : "disabled") + "; allied NPCs " +
                (Settings.ApplyToAlliedNpcs ? "enabled" : "disabled") + "; body blocking " +
                (Settings.BodyBlockingEnabled ? "enabled" : "disabled") + ").");
            return true;
        }
        catch (Exception exception)
        {
            modEntry.Logger.Error(exception.ToString());
            try
            {
                // PatchAll is not atomic. Remove only our patches if a later
                // patch fails, so a failed load cannot leave misleading logs.
                harmony?.UnpatchAll(modEntry.Info.Id);
                TargetingState.Reset();
            }
            catch (Exception cleanupException)
            {
                modEntry.Logger.Error("Could not remove partially applied patches: " + cleanupException);
            }
            return false;
        }
    }
}
